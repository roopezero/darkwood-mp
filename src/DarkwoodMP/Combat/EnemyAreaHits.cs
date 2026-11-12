using System.Collections.Generic;
using DarkwoodMP.Net;
using DarkwoodMP.World;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMP.Combat
{
    /// <summary>
    /// Enemy damage that doesn't come through a melee sensor or a bullet, for remote players. The game's code for
    /// these only looks for a Player component or at Player.Instance, so on the host it hurts the host's player
    /// and never a remote player's proxy. Here each also sends PlayerDamage to every proxy it would have caught,
    /// with the game's own falloff:
    ///
    /// - explosions (Explodes.explode: enemy bombs, and every other explosion in the host's world, since a
    ///   client's own game doesn't hurt its player with explosions; see ThrowSync's ClientExplosionSelfHitPatch),
    ///   damage * (radius - distance) / radius, and the explosion's effect;
    /// - damage auras (Character.waitToDamageAroundMe), aroundMeDamage * (range / 2 - distance) / range;
    /// - a diving flier (Flier.Update), 5 to whoever it dove at, not always the host;
    /// - a Shooter on one of the host's enemies, damage / distance to each proxy it can see;
    /// - flames an enemy spawned (its projectiles and what their explosions leave burning), which the proxy
    ///   otherwise drops: Flame calls every hit byPlayer.
    ///
    /// The host decides all of it. A client's frozen copies of the host's enemies don't dive or shoot. Explosions of
    /// the host's enemies happen only in the host's game, so the host also sends EnemyExplosion (the sound and the
    /// flash) to the clients.
    /// </summary>
    public static class EnemyAreaHits
    {
        /// <summary>What the host's enemies spawned (projectiles, and what their explosions spawned: fire).</summary>
        private static readonly HashSet<GameObject> EnemySpawned = new HashSet<GameObject>();
        /// <summary>What the local player's weapon spawned (a flamethrower's flames).</summary>
        private static readonly HashSet<GameObject> PlayerSpawned = new HashSet<GameObject>();
        internal static int EnemyDepth, PlayerDepth;

        public static void Register(Session s) => s.Register(MsgType.EnemyExplosion, OnExplosion);

        public static void Clear() { EnemySpawned.Clear(); PlayerSpawned.Clear(); EnemyDepth = 0; PlayerDepth = 0; }

        private static bool Hosting => Proxies.Any && (Session.Instance == null || !Session.Instance.IsClient);

        internal static void Spawned(GameObject go)
        {
            if (go == null) return;
            if (EnemyDepth > 0 && Hosting) Add(EnemySpawned, go);
            if (PlayerDepth > 0) Add(PlayerSpawned, go);
        }

        private static void Add(HashSet<GameObject> set, GameObject go)
        {
            if (set.Count > 512) set.RemoveWhere(g => g == null);
            set.Add(go);
        }

        private static bool In(HashSet<GameObject> set, Transform t)
        {
            if (set.Count == 0) return false;
            for (; t != null; t = t.parent) if (set.Contains(t.gameObject)) return true;
            return false;
        }

        /// <summary>Host: whether one of the host's enemies spawned this (or it is one).</summary>
        public static bool ByEnemy(Transform t, Transform spawnedBy = null)
        {
            if (spawnedBy != null && IsEnemy(spawnedBy.GetComponent<Character>())) return true;
            return In(EnemySpawned, t);
        }

        private static bool IsEnemy(Character c) => c != null && NetEnemies.Eligible(c);

        /// <summary>Whether the local player's weapon spawned this flame.</summary>
        public static bool ByLocalPlayer(Flame f) => f != null && In(PlayerSpawned, f.transform);

        /// <summary>Test hook: count this as spawned by one of the host's enemies.</summary>
        internal static void MarkEnemy(GameObject go) { if (go != null) Add(EnemySpawned, go); }

        /// <summary>
        /// Client: whether the local player is somewhere the host doesn't run (a dream, or a separate location the
        /// host didn't place for us), so nothing the host's enemies do reaches them there.
        /// </summary>
        public static bool PlayerAway() => !World.Locations.HostRunsHere();

        private static IEnumerable<NetPlayerProxy> LivingProxies()
        {
            foreach (var p in NetPlayerProxy.All.Values)
            {
                if (p == null || !p.alive) continue;
                var col = p.GetComponent<Collider>();
                if (col != null && !col.enabled) continue;
                yield return p;
            }
        }

        // ---- explosions -------------------------------------------------------------------------------

        /// <summary>Host, before Explodes.explode: the proxies it catches, as explode does for the host's Player.</summary>
        internal static void Explosion(Explodes ex)
        {
            if (ex == null || ex.effect == null || !Hosting) return;
            var at = ex.transform;
            if (ex.affectsPlayer && ex.radius > 0f)
            {
                foreach (var p in LivingProxies())
                {
                    float d = Core.trueDistance(p.transform.position, at.position);
                    if (d >= ex.radius || !Core.canSee(at, p.transform)) continue;
                    float damage = ex.damage * ((ex.radius - d) / ex.radius);
                    if (damage <= 0f) continue;
                    PlayerDamage.Send(p.Owner, damage, ex.objectThatSpawnedMe != null ? ex.objectThatSpawnedMe : at, shadow: false,
                        effects: ex.hasEffect ? new List<InvItemEffect> { ex.effect } : null);
                }
            }
            if (ByEnemy(at, ex.objectThatSpawnedMe)) SendExplosion(ex);
        }

        /// <summary>
        /// EnemyExplosion, host -> clients: vec3 position, str sound (the game's audio id, "" none), str effect (the
        /// explosion prefab's name, "" none). One of the host's enemies' explosions, which happen only in the host's game.
        /// </summary>
        private static void SendExplosion(Explodes ex)
        {
            var s = Session.Instance;
            if (s == null || !s.IsHost || s.Peers.Count == 0) return;
            string sound = ex.explodeSound ?? "";
            string fx = ex.explosionPrefab != null ? ex.explosionPrefab.name : "";
            if (sound.Length == 0 && fx.Length == 0) return;
            var w = s.Begin(MsgType.EnemyExplosion);
            w.Write(ex.transform.position);
            w.WriteStr(sound);
            w.WriteStr(fx);
            s.Broadcast(w, Channel.Control, reliable: true);
            if (WorldActions.Debug) Plugin.Log.LogInfo($"[world] enemy explosion ({fx}, {sound}) at {ex.transform.position}");
        }

        private static readonly Dictionary<string, GameObject> FxByName = new Dictionary<string, GameObject>();

        private static void OnExplosion(ulong from, PacketReader r)
        {
            var s = Session.Instance;
            if (s == null || !s.IsClient || from != s.HostId || !WorldShare.InWorld) return;
            var pos = r.ReadVector3();
            string sound = r.ReadStr();
            string fx = r.ReadStr();
            var me = Player.Instance;
            if (me == null || PlayerAway() || Core.trueDistance(me.transform.position, pos) > 4000f) return;
            if (sound.Length > 0) AudioController.Play(sound, pos);
            var prefab = Fx(fx);
            if (prefab == null) return;
            // Only the look: anything in it that could hurt counts as part of an explosion (the host already dealt it).
            WorldActions.Replaying = true;
            ThrownHits.Depth++;
            try { Core.AddPrefab(prefab, pos, Quaternion.Euler(90f, 0f, 0f), null, worldSpace: true); }
            finally { ThrownHits.Depth--; WorldActions.Replaying = false; }
        }

        /// <summary>An explosion prefab by name, among the assets this game has loaded (the enemy that throws it is here too).</summary>
        private static GameObject Fx(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (FxByName.TryGetValue(name, out var go) && go != null) return go;
            go = null;
            foreach (var g in Resources.FindObjectsOfTypeAll<GameObject>())
                if (g != null && g.name == name && !g.scene.IsValid() && g.transform.parent == null) { go = g; break; }
            if (go == null) Plugin.Log.LogInfo($"The host's enemy exploded with {name}, which this game hasn't loaded: sound only");
            FxByName[name] = go;
            return go;
        }

        // ---- auras, fliers, shooters, flames ------------------------------------------------------------

        /// <summary>Host, before Character.waitToDamageAroundMe: the proxies in its aura.</summary>
        internal static void Aura(Character c)
        {
            if (c == null || !c.alive || !Hosting || c.aroundMeDamageRange <= 0f) return;
            foreach (var p in LivingProxies())
            {
                float d = Core.trueDistance(p.transform.position, c.transform.position);
                if (d > c.aroundMeDamageRange) continue;
                float f = (c.aroundMeDamageRange / 2f - d) / c.aroundMeDamageRange;
                if (f > 0f) PlayerDamage.Send(p.Owner, c.aroundMeDamage * f, c.transform, shadow: false, effects: null);
            }
        }

        /// <summary>Flier.Update's "Player.Instance.getHit(5)": hits whoever the flier dove at.</summary>
        public static void FlierHit(Flier flier, float damage)
        {
            var c = flier != null ? flier.GetComponent<Character>() : null;
            var target = c != null ? c.target : null;
            var proxy = target != null ? target.GetComponent<NetPlayerProxy>() : null;
            if (proxy != null) PlayerDamage.Send(proxy.Owner, damage, flier.transform, shadow: false, effects: null);
            else if (Player.Instance != null) Player.Instance.getHit(damage);
        }

        /// <summary>Host, before Shooter.shoot on one of its enemies: each proxy it can see is hit as the host's player is.</summary>
        internal static void Shot(Shooter sh)
        {
            if (sh == null || sh.target == null || !Hosting) return;
            if (!IsEnemy(sh.GetComponentInParent<Character>())) return; // a Shooter of the world's own runs in every game
            foreach (var p in LivingProxies())
            {
                if (!Core.canSee(sh.transform, p.transform)) continue;
                float d = Mathf.Max(1f, Core.trueDistance(sh.transform, p.transform));
                PlayerDamage.Send(p.Owner, sh.damage / d, sh.transform, shadow: false, effects: null);
            }
        }

        /// <summary>Host, in Flame.onCollideWith: an enemy's flame touching a proxy hurts its player (the proxy drops byPlayer hits).</summary>
        internal static void FlameTouch(Flame f, Transform t, Collider col)
        {
            if (f == null || !f.isActive || !Hosting) return;
            var p = col != null ? col.transform.GetComponent<NetPlayerProxy>() : null;
            if (p == null && t != null) p = t.GetComponent<NetPlayerProxy>();
            if (p == null || !p.alive || !ByEnemy(f.transform)) return;
            PlayerDamage.Send(p.Owner, f.contactDamage, f.transform, shadow: false,
                effects: f.effect != null ? new List<InvItemEffect> { f.effect } : null);
        }
    }

    /// <summary>
    /// "--dwmp-test-blast" (host): once a player (not the dummy) has joined, three of the host's enemies' kinds of
    /// area damage go off at them: an enemy's bomb exploding beside them (DarkwoodMP_TestBlast, with a real bomb's
    /// sound and flash, so EnemyExplosion is sent), the nearest enemy's damage aura (named DarkwoodMP_TestAura while
    /// it hurts), and an enemy's flame touching them (DarkwoodMP_TestFlame). Each should reach them as PlayerDamage.
    /// </summary>
    static class AreaHitsTest
    {
        private static readonly System.Reflection.MethodInfo AuraTick = AccessTools.Method(typeof(Character), "waitToDamageAroundMe");
        private static readonly System.Reflection.MethodInfo FlameTouch = AccessTools.Method(typeof(Flame), "onCollideWith");

        internal static System.Collections.IEnumerator Run()
        {
            NetPlayerProxy p = null;
            while (p == null)
            {
                foreach (var x in NetPlayerProxy.All.Values) if (x != null && x.Owner != Sync.PlayerSync.DummyId) p = x;
                yield return null;
            }
            yield return new WaitForSeconds(4f);
            if (p == null) yield break;
            var at = p.transform.position;

            // A bomb: a real one's sound and flash, no fire.
            var go = new GameObject("DarkwoodMP_TestBlast");
            go.transform.position = at + new Vector3(10f, 0f, 0f);
            var ex = go.AddComponent<Explodes>();
            var db = Singleton<ItemsDatabase>.Instance;
            foreach (var key in db != null ? db.itemsDict.Keys : (IEnumerable<string>)new string[0])
            {
                var item = db.getItem(key, instantiate: false);
                var real = item != null && item.item is GameObject g ? g.GetComponent<Explodes>() : null;
                if (real == null || real.explosionPrefab == null) continue;
                ex.explosionPrefab = real.explosionPrefab; ex.explodeSound = real.explodeSound;
                break;
            }
            ex.radius = 300f; ex.damage = 30f; ex.affectsPlayer = true; ex.effect = new InvItemEffect();
            EnemyAreaHits.MarkEnemy(go);
            Plugin.Log.LogInfo($"--dwmp-test-blast: an enemy bomb ({(ex.explosionPrefab != null ? ex.explosionPrefab.name : "no flash")}) beside {p.OwnerName}");
            ex.onActivate();
            if (go != null) Object.Destroy(go);
            yield return new WaitForSeconds(1f);

            // The nearest enemy's damage aura, made wide enough to reach them.
            Character c = null;
            foreach (var x in AllCharacters.Active())
                if (x != null && x.alive && NetEnemies.Eligible(x) && (c == null || (x.transform.position - at).sqrMagnitude < (c.transform.position - at).sqrMagnitude)) c = x;
            if (c != null)
            {
                string name = c.name; int dmg = c.aroundMeDamage; float range = c.aroundMeDamageRange;
                c.name = "DarkwoodMP_TestAura";
                c.aroundMeDamage = 6;
                c.aroundMeDamageRange = 2f * Core.trueDistance(c.transform.position, p.transform.position) + 200f;
                Plugin.Log.LogInfo($"--dwmp-test-blast: {name}'s damage aura");
                try { AuraTick.Invoke(c, null); }
                finally { c.name = name; c.aroundMeDamage = dmg; c.aroundMeDamageRange = range; }
            }
            else Plugin.Log.LogInfo("--dwmp-test-blast: no enemy for the damage aura");
            yield return new WaitForSeconds(1f);

            // An enemy's flame.
            if (p == null) yield break;
            var fgo = new GameObject("DarkwoodMP_TestFlame");
            fgo.transform.position = p.transform.position;
            var f = fgo.AddComponent<Flame>();
            f.contactDamage = 4;
            f.effect = new InvItemEffect { type = CharacterEffectType.burn, duration = 3f, modifier = 1f, interval = 1f };
            EnemyAreaHits.MarkEnemy(fgo);
            Plugin.Log.LogInfo($"--dwmp-test-blast: an enemy's flame on {p.OwnerName}");
            FlameTouch.Invoke(f, new object[] { p.transform, p.GetComponent<Collider>(), true });
            Object.Destroy(fgo, 2f);
            Plugin.Log.LogInfo("--dwmp-test-blast: done");
        }
    }

    // ---- what enemies and the player's weapon spawn ----------------------------------------------------

    [HarmonyPatch]
    static class EnemyShotPatch
    {
        static IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Character), "rangedAttack");
            yield return AccessTools.Method(typeof(Character), nameof(Character.spawnProjectile));
        }
        static void Prefix() => EnemyAreaHits.EnemyDepth++;
        static void Postfix() => EnemyAreaHits.EnemyDepth--;
        static System.Exception Finalizer(System.Exception __exception) { if (__exception != null) EnemyAreaHits.EnemyDepth = 0; return __exception; }
    }

    /// <summary>What an enemy's projectile spawns as it explodes (fire) is the enemy's too.</summary>
    [HarmonyPatch(typeof(Explodes), nameof(Explodes.onActivate), new System.Type[0])]
    static class EnemyExplosionSpawnPatch
    {
        static void Prefix(Explodes __instance, out bool __state)
        {
            __state = EnemyAreaHits.ByEnemy(__instance.transform, __instance.objectThatSpawnedMe);
            if (__state) EnemyAreaHits.EnemyDepth++;
        }
        static void Postfix(bool __state) { if (__state) EnemyAreaHits.EnemyDepth--; }
        static System.Exception Finalizer(System.Exception __exception) { if (__exception != null) EnemyAreaHits.EnemyDepth = 0; return __exception; }
    }

    [HarmonyPatch(typeof(Player), "spawnBullet")]
    static class PlayerBulletSpawnPatch
    {
        static void Prefix() => EnemyAreaHits.PlayerDepth++;
        static void Postfix() => EnemyAreaHits.PlayerDepth--;
        static System.Exception Finalizer(System.Exception __exception) { if (__exception != null) EnemyAreaHits.PlayerDepth = 0; return __exception; }
    }

    [HarmonyPatch]
    static class AreaHitsSpawnPatch
    {
        static IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Core), nameof(Core.AddPrefab), new[] { typeof(Object), typeof(Vector3), typeof(Quaternion), typeof(GameObject), typeof(bool) });
            yield return AccessTools.Method(typeof(Core), nameof(Core.AddPrefab), new[] { typeof(string), typeof(Vector3), typeof(Quaternion), typeof(GameObject), typeof(bool) });
        }
        static void Postfix(GameObject __result) { if (EnemyAreaHits.EnemyDepth > 0 || EnemyAreaHits.PlayerDepth > 0) EnemyAreaHits.Spawned(__result); }
    }

    [HarmonyPatch]
    static class AreaHitsPooledSpawnPatch
    {
        static IEnumerable<System.Reflection.MethodBase> TargetMethods() =>
            AccessTools.GetDeclaredMethods(typeof(Core)).FindAll(m => m.Name == nameof(Core.AddPooledPrefab) && m.ReturnType == typeof(Transform));
        static void Postfix(Transform __result) { if (__result != null && (EnemyAreaHits.EnemyDepth > 0 || EnemyAreaHits.PlayerDepth > 0)) EnemyAreaHits.Spawned(__result.gameObject); }
    }

    // ---- the hits ------------------------------------------------------------------------------------

    [HarmonyPatch(typeof(Explodes), "explode")]
    static class ExplosionProxyPatch
    {
        static void Prefix(Explodes __instance) => EnemyAreaHits.Explosion(__instance);
    }

    [HarmonyPatch(typeof(Character), "waitToDamageAroundMe")]
    static class DamageAuraProxyPatch
    {
        static void Prefix(Character __instance) => EnemyAreaHits.Aura(__instance);
    }

    /// <summary>
    /// Host: a diving flier hurts the player it dove at (the game hurts Player.Instance). The transpiler swaps
    /// "Player.Instance.getHit(5f)" for EnemyAreaHits.FlierHit(this, 5f). Client: a frozen copy doesn't dive.
    /// </summary>
    [HarmonyPatch(typeof(Flier), "Update")]
    static class FlierTargetPatch
    {
        static bool Prefix(Flier __instance) =>
            !(NetEnemies.ClientMode && NetEnemies.Frozen.Contains(__instance.GetComponent<Character>()));

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var getInstance = AccessTools.PropertyGetter(typeof(Player), nameof(Player.Instance));
            var getHit = AccessTools.Method(typeof(Player), nameof(Player.getHit), new[] { typeof(float) });
            var getHitBase = AccessTools.Method(typeof(CharBase), nameof(CharBase.getHit), new[] { typeof(float) });
            var flierHit = AccessTools.Method(typeof(EnemyAreaHits), nameof(EnemyAreaHits.FlierHit));
            var list = new List<CodeInstruction>(instructions);
            int swapped = 0;
            for (int i = 0; i < list.Count - 2; i++)
            {
                if (!list[i].Calls(getInstance)) continue;
                if (!(list[i + 2].Calls(getHit) || list[i + 2].Calls(getHitBase))) continue;
                list[i] = new CodeInstruction(System.Reflection.Emit.OpCodes.Ldarg_0) { labels = list[i].labels };
                list[i + 2] = new CodeInstruction(System.Reflection.Emit.OpCodes.Call, flierHit) { labels = list[i + 2].labels };
                swapped++;
            }
            if (swapped == 0) Plugin.Log.LogWarning("FlierTargetPatch: no Player.Instance.getHit in Flier.Update (game changed?)");
            return list;
        }
    }

    [HarmonyPatch(typeof(Shooter), "shoot")]
    static class ShooterProxyPatch
    {
        static bool Prefix(Shooter __instance)
        {
            // A client's frozen copy of the host's enemy doesn't shoot.
            if (NetEnemies.ClientMode && NetEnemies.Frozen.Contains(__instance.GetComponentInParent<Character>())) return false;
            EnemyAreaHits.Shot(__instance);
            return true;
        }
    }
}
