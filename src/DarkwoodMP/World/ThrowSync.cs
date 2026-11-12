using System.Collections.Generic;
using DarkwoodMP.Combat;
using DarkwoodMP.Net;
using DarkwoodMP.Sync;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Thrown things (molotovs, pipe bombs, rocks, flares...), shared. When a player throws one (Player.throwItem),
    /// the same item is thrown in every other game, from the same spot, with the same velocity and target, so
    /// everyone sees it fly, land, explode and burn. One dropped at the player's feet instead (Player.dropItem:
    /// a dropOnReleaseAim item when the player stops aiming, or a held throwable when the player is interrupted)
    /// is sent the same way with distance -1, and dropped (ThrownItem.dropMe) where it fell in the other games.
    ///
    /// Who it hurts is the host's to decide, as with every other hit on its enemies: the host's copy (or its own
    /// throw) deals the damage, and on a client nothing a thrown thing does to the host's enemies is sent as a hit
    /// (not its impact, its explosion, or the flames it leaves; see ThrownHits). A client's own throw used to be
    /// counted from the client's game; now the host's copy of it counts instead.
    ///
    /// Something thrown that can be picked up again (a knife, a brick) lands only in the thrower's game: the
    /// others' copies fly and hit, then go, so it isn't there to pick up twice.
    /// </summary>
    public sealed class ThrowSync
    {
        private readonly Session _session;
        private static readonly AccessTools.FieldRef<Player, GameObject> HeldItem = AccessTools.FieldRefAccess<Player, GameObject>("heldItem");

        public static ThrowSync Instance { get; private set; }

        public ThrowSync(Session session)
        {
            Instance = this;
            _session = session;
            session.Register(MsgType.ItemThrown, OnThrown);
        }

        internal static GameObject Held(Player p) => HeldItem(p);

        /// <summary>The local player threw it (called after Player.throwItem).</summary>
        internal void Thrown(string type, GameObject go, Vector3 from, bool dropped = false)
        {
            if (!WorldActions.Sharing || go == null || string.IsNullOrEmpty(type)) return;
            var ti = go.GetComponent<ThrownItem>();
            var rb = go.GetComponent<Rigidbody>();
            if (ti == null || rb == null) return;
            var w = _session.Begin(MsgType.ItemThrown);
            Write(w, _session.LocalId, type, go.transform.position, go.transform.rotation, dropped ? Vector3.zero : rb.velocity, dropped ? go.transform.position : ti.landTarget, dropped ? Dropped : Vector3.Distance(ti.landTarget, from));
            if (_session.IsHost) _session.Broadcast(w, Channel.Control, reliable: true);
            else _session.SendToHost(w, Channel.Control, reliable: true);
            if (WorldActions.Debug) Plugin.Log.LogInfo($"[world] {(dropped ? "dropped" : "threw")} {type}");
        }

        /// <summary>The copy of a throw doesn't collide with its thrower's stand-in (the host's proxy) or puppet.</summary>
        private static void IgnoreThrower(GameObject go, ulong by)
        {
            var mine = go.GetComponentsInChildren<Collider>(true);
            if (mine.Length == 0) return;
            var theirs = new List<Collider>();
            if (NetPlayerProxy.All.TryGetValue(by, out var proxy) && proxy != null) theirs.AddRange(proxy.GetComponentsInChildren<Collider>(true));
            if (PlayerSync.Instance != null && PlayerSync.Instance.Remotes.TryGetValue(by, out var rp) && rp.Root != null)
                theirs.AddRange(rp.Root.GetComponentsInChildren<Collider>(true));
            foreach (var a in mine)
                foreach (var b in theirs)
                    if (a != null && b != null) Physics.IgnoreCollision(a, b);
        }

        /// <summary>The distance sent for something dropped at the player's feet (Player.dropItem), not thrown.</summary>
        private const float Dropped = -1f;

        private static void Write(PacketWriter w, ulong by, string type, Vector3 pos, Quaternion rot, Vector3 vel, Vector3 target, float distance)
        {
            w.W.Write(by);
            w.WriteStr(type);
            w.Write(pos);
            w.W.Write(rot.x); w.W.Write(rot.y); w.W.Write(rot.z); w.W.Write(rot.w);
            w.Write(vel);
            w.Write(target);
            w.W.Write(distance);
        }

        private void OnThrown(ulong from, PacketReader r)
        {
            var s = _session;
            if (!WorldShare.InWorld) return;
            if (s.IsClient && from != s.HostId) return;
            ulong by = r.R.ReadUInt64();
            string type = r.ReadStr();
            var pos = r.ReadVector3();
            var rot = new Quaternion(r.R.ReadSingle(), r.R.ReadSingle(), r.R.ReadSingle(), r.R.ReadSingle());
            var vel = r.ReadVector3();
            var target = r.ReadVector3();
            float distance = r.R.ReadSingle();
            if (s.IsHost && !Authority.Near(from, pos, 400f))
            {
                Plugin.Log.LogInfo($"[authority] {s.NameOf(from)} threw a {type} from where they aren't: not thrown");
                return;
            }
            if (s.IsHost)
            {
                by = from; // never trust who a client says threw it
                var w = s.Begin(MsgType.ItemThrown);
                Write(w, by, type, pos, rot, vel, target, distance);
                s.Broadcast(w, Channel.Control, reliable: true, except: from);
            }
            if (by == s.LocalId) return;

            var item = Singleton<ItemsDatabase>.Instance?.getItem(type, instantiate: false);
            if (item == null || item.item == null) { Plugin.Log.LogWarning($"Someone threw a {type}, which this game doesn't know"); return; }

            // Who threw it, as the game's code sees them: the host's proxy for them (the AI's view of that player,
            // so the host's enemies blame the right player), else their puppet.
            Transform thrower = null;
            if (NetPlayerProxy.All.TryGetValue(by, out var proxy) && proxy != null) thrower = proxy.transform;
            else if (PlayerSync.Instance != null && PlayerSync.Instance.Remotes.TryGetValue(by, out var rp) && rp.Root != null) thrower = rp.Root.transform;

            GameObject go;
            WorldActions.Replaying = true;
            try { go = Core.AddPrefab(item.item, pos, rot, null); }
            finally { WorldActions.Replaying = false; }
            if (go == null) return;

            var ti = go.GetComponent<ThrownItem>();
            var rb = go.GetComponent<Rigidbody>();
            if (ti == null || rb == null) { Object.Destroy(go); return; }
            var ren = go.GetComponent<Renderer>();
            if (ren != null) ren.enabled = true;
            // The thrower's game makes it inside their player and keeps it from hitting them (Player's aim:
            // Physics.IgnoreCollision with the player). The copy starts inside their stand-in here: without the same,
            // a molotov or gas bomb broke on the thrower at once and set them alight.
            IgnoreThrower(go, by);
            if (distance < 0f)
            {
                // Dropped, as Player.dropItem does it: it lies where it fell and goes off after its delay.
                rb.velocity = Vector3.zero;
                ti.objectThatSpawnedMe = thrower;
                var dex = go.GetComponent<Explodes>();
                if (dex != null) dex.objectThatSpawnedMe = thrower;
                ThrownHits.Copies.Add(go);
                ti.dropMe();
                if (s.IsHost) Core.addToSaveable(go, isDynamic: true);
                if (WorldActions.Debug) Plugin.Log.LogInfo($"[world] {s.NameOf(by)} dropped {type}");
                return;
            }
            rb.drag = 2f;
            rb.velocity = vel;
            if (ti.initialRotationForce == 0f) rb.angularVelocity = Vector3.zero;
            else rb.AddTorque(0f, ti.initialRotationForce * 1000f, 0f);
            ti.landTarget = target;
            ti.thrown = true;
            ti.objectThatSpawnedMe = thrower;
            ti.setFallSpeed(distance);
            var ex = go.GetComponent<Explodes>();
            if (ex != null) ex.objectThatSpawnedMe = thrower;
            ThrownHits.Copies.Add(go);
            if (item.recoverableAfterThrown) Object.Destroy(go, ti.flyTime + 2f); // the thrower's lands; this one goes
            else if (s.IsHost) Core.addToSaveable(go, isDynamic: true); // a flare left burning, a spent molotov: the host's world keeps it
            if (WorldActions.Debug) Plugin.Log.LogInfo($"[world] {s.NameOf(by)} threw {type}");
        }
    }

    /// <summary>
    /// Player.dropItem: an item that's dropped when the player stops aiming (InvItem.dropOnReleaseAim), or the
    /// held throwable when the player is interrupted (force), falls at the player's feet; it's sent like a throw.
    /// </summary>
    [HarmonyPatch(typeof(Player), "dropItem")]
    static class PlayerDropPatch
    {
        static void Prefix(Player __instance, out (GameObject go, string type) __state)
        {
            var cur = __instance.currentItem;
            __state = (ThrowSync.Held(__instance), InvItemClass.isNull(cur) ? null : cur.baseClass?.type);
        }

        static void Postfix(Player __instance, (GameObject go, string type) __state)
        {
            if (__state.go != null && ThrowSync.Held(__instance) == null)
                ThrowSync.Instance?.Thrown(__state.type, __state.go, __instance.transform.position, dropped: true);
        }
    }

    [HarmonyPatch(typeof(Player), "throwItem")]
    static class PlayerThrowPatch
    {
        static void Prefix(Player __instance, out (GameObject go, string type) __state)
        {
            var go = ThrowSync.Held(__instance);
            var cur = __instance.currentItem;
            __state = (go, InvItemClass.isNull(cur) ? null : cur.baseClass?.type);
        }

        static void Postfix(Player __instance, (GameObject go, string type) __state)
        {
            if (__state.go != null) ThrowSync.Instance?.Thrown(__state.type, __state.go, __instance.transform.position);
        }
    }

    /// <summary>
    /// A client's game sends no hits on the host's enemies from anything thrown: the host's copy of the throw
    /// hits them there. That covers the impact (ThrownItem.onCollide), the explosion (Explodes), what it leaves
    /// burning (flames spawned while it lands or explodes), and the client's own player being caught in an
    /// explosion, which the host's copy reports through the client's proxy.
    /// </summary>
    public static class ThrownHits
    {
        /// <summary>Copies of others' throws in this game.</summary>
        public static readonly HashSet<GameObject> Copies = new HashSet<GameObject>();
        /// <summary>What thrown things spawned as they landed or exploded (fire, fuel...), in this game.</summary>
        public static readonly HashSet<GameObject> Spawned = new HashSet<GameObject>();

        /// <summary>How deep in a thrown thing's impact or explosion we are.</summary>
        public static int Depth;
        /// <summary>The flame touching something right now (Flame.onCollideWith).</summary>
        public static Flame CurrentFlame;

        /// <summary>Whether a hit happening now comes from something thrown.</summary>
        public static bool Now()
        {
            if (Depth > 0) return true;
            if (CurrentFlame == null) return false;
            for (var t = CurrentFlame.transform; t != null; t = t.parent)
                if (Spawned.Contains(t.gameObject)) return true;
            return false;
        }

        public static void Clear() { Copies.Clear(); Spawned.Clear(); Depth = 0; CurrentFlame = null; }
    }

    [HarmonyPatch(typeof(ThrownItem), nameof(ThrownItem.onCollide))]
    static class ThrownImpactPatch
    {
        static void Prefix() => ThrownHits.Depth++;
        static void Postfix() => ThrownHits.Depth--;
        static System.Exception Finalizer(System.Exception __exception) { if (__exception != null) ThrownHits.Depth = 0; return __exception; }
    }

    [HarmonyPatch]
    static class ExplosionPatch
    {
        static IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Explodes), "explode");
            yield return AccessTools.Method(typeof(Explodes), "spawnObjects");
            yield return AccessTools.Method(typeof(Explodes), nameof(Explodes.onActivate), new System.Type[0]);
        }
        static void Prefix() => ThrownHits.Depth++;
        static void Postfix() => ThrownHits.Depth--;
        static System.Exception Finalizer(System.Exception __exception) { if (__exception != null) ThrownHits.Depth = 0; return __exception; }
    }

    /// <summary>
    /// A flame touching something (Flame.onCollideWith). Noted for ThrownHits, and:
    /// on the host, an enemy's flame on a proxy hurts that player (EnemyAreaHits.FlameTouch);
    /// on a client, our own weapon's flame (a flamethrower) sends its burn with the hit on the host's enemy
    /// (NetEnemies.HitEffects), and a door, window or furniture it sets alight goes to the host as a request
    /// (FireSync.ClientIgnited).
    /// </summary>
    [HarmonyPatch(typeof(Flame), "onCollideWith")]
    static class FlameTouchPatch
    {
        static void Prefix(Flame __instance, Transform _transform, Collider _collider, out GameObject __state)
        {
            ThrownHits.CurrentFlame = __instance;
            __state = null;
            EnemyAreaHits.FlameTouch(__instance, _transform, _collider);
            if (!NetEnemies.ClientMode || __instance == null || !__instance.isActive || !EnemyAreaHits.ByLocalPlayer(__instance)) return;
            if (__instance.effect != null) NetEnemies.HitEffects = new List<InvItemEffect> { __instance.effect };
            var target = FireSync.Flammable(_transform, _collider);
            if (target != null && target.GetComponent<Burn>() == null) __state = target;
        }

        static void Postfix(GameObject __state)
        {
            ThrownHits.CurrentFlame = null;
            NetEnemies.HitEffects = null;
            if (__state != null && __state.GetComponent<Burn>() != null) FireSync.Instance?.ClientIgnited(__state);
        }

        static System.Exception Finalizer(System.Exception __exception)
        {
            ThrownHits.CurrentFlame = null;
            NetEnemies.HitEffects = null;
            return __exception;
        }
    }

    /// <summary>Note what a landing or exploding thrown thing spawns (fire, fuel), so its flames count as thrown.</summary>
    [HarmonyPatch(typeof(Core), nameof(Core.AddPrefab), new[] { typeof(Object), typeof(Vector3), typeof(Quaternion), typeof(GameObject), typeof(bool) })]
    static class ThrownSpawnPatch
    {
        static void Postfix(GameObject __result) { if (ThrownHits.Depth > 0 && __result != null) ThrownHits.Spawned.Add(__result); }
    }

    [HarmonyPatch(typeof(Core), nameof(Core.AddPrefab), new[] { typeof(string), typeof(Vector3), typeof(Quaternion), typeof(GameObject), typeof(bool) })]
    static class ThrownSpawnByNamePatch
    {
        static void Postfix(GameObject __result) { if (ThrownHits.Depth > 0 && __result != null) ThrownHits.Spawned.Add(__result); }
    }

    /// <summary>Client: an explosion that catches our own player is the host's to report (through our proxy there), effects included.</summary>
    [HarmonyPatch(typeof(Player), nameof(Player.getHit),
        new[] { typeof(float), typeof(Transform), typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(bool) })]
    static class ClientExplosionSelfHitPatch
    {
        static bool Prefix() => !HostReports();

        /// <summary>
        /// Client: whether what's hitting our player now is an explosion, which the host reports through our proxy
        /// (EnemyAreaHits.Explosion). Not while we're away in a place of our own (a location, a dream): explosions there
        /// happen only here.
        /// </summary>
        internal static bool HostReports() =>
            NetEnemies.ClientMode && ThrownHits.Depth > 0 && ThrownHits.CurrentFlame == null && !EnemyAreaHits.PlayerAway() && InExplosion();

        private static bool InExplosion()
        {
            var st = new System.Diagnostics.StackTrace(2, false);
            for (int i = 0; i < st.FrameCount && i < 12; i++)
                if (st.GetFrame(i)?.GetMethod()?.DeclaringType == typeof(Explodes)) return true;
            return false;
        }
    }

    [HarmonyPatch]
    static class ClientExplosionSelfEffectPatch
    {
        static IEnumerable<System.Reflection.MethodBase> TargetMethods() =>
            AccessTools.GetDeclaredMethods(typeof(CharacterEffects)).FindAll(m => m.Name == nameof(CharacterEffects.activate));

        static bool Prefix(CharacterEffects __instance) =>
            !(ThrownHits.Depth > 0 && __instance != null && Player.Instance != null && __instance == Player.Instance.effects && ClientExplosionSelfHitPatch.HostReports());
    }
}
