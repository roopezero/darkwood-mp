using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMP.Combat
{
    /// <summary>
    /// An enemy's attack is a short-lived MeleeSensor trigger; the game applies it to a Character or a Player it
    /// touches. A proxy is neither, so its hits are handled here: the same checks the game makes for a player
    /// (not ignored, the attacker can see it), then the damage and effects go to the proxy's player.
    /// </summary>
    [HarmonyPatch(typeof(MeleeSensor), "OnTriggerEnter")]
    public static class MeleeSensorProxyPatch
    {
        private static bool Prefix(MeleeSensor __instance, Collider _collider)
        {
            if (_collider == null) return true;
            var proxy = _collider.GetComponent<NetPlayerProxy>();
            if (proxy == null) return true;

            var s = __instance;
            if (s.type != MeleeSensor.MeleeSensorType.character || s.attackerTransform == null) return false; // players don't hit players
            // Only the colliders: the game clears them as the pooled sensor is spawned again (MeleeSensor.OnSpawned),
            // but never its gameObjectsToIgnore, which it fills and never reads. Checking those meant a pooled
            // sensor that had once hit a player's stand-in never hit that stand-in again, for any enemy that uses it.
            if (s.collidersToIgnore.Contains(_collider)) return false;
            s.collidersToIgnore.Add(_collider);
            s.gameObjectsToIgnore.Add(proxy.gameObject);
            if (!s.doesNotNeedLineOfSight && !Core.canSeeAttack(s.attackerTransform, proxy.transform))
            {
                if (World.MapScenarioDogWatch.On) Plugin.Log.LogInfo($"[dogwatch] {s.attackerTransform.name}'s hit on {proxy.OwnerName}'s stand-in blocked: no line of sight");
                return false;
            }
            if (World.MapScenarioDogWatch.On) Plugin.Log.LogInfo($"[dogwatch] {s.attackerTransform.name} hit {proxy.OwnerName}'s stand-in for {s.damage:0}");

            var attacker = s.attackerTransform.GetComponent<CharBase>();
            float damage = s.damage * (attacker != null ? attacker.strengthModifier : 1f);
            s.onHit?.Invoke();
            PlayerDamage.Send(proxy.Owner, damage, s.attackerTransform, s.shadowSensor, s.effects);
            return false;
        }
    }

    /// <summary>Damage a player deals to an enemy is threat for that player.</summary>
    [HarmonyPatch(typeof(Character), nameof(Character.getHit),
        new[] { typeof(float), typeof(Transform), typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(bool) })]
    public static class CharacterGetHitThreatPatch
    {
        private static void Prefix(Character __instance, out float __state) => __state = __instance.health;

        private static void Postfix(Character __instance, Transform attackerTransform, bool normalHit, float __state)
        {
            // The game splats blood for a normal hit that took health (not the night trader); show it to the clients too.
            if (normalHit && __state - __instance.health > 0f && !__instance.isNightTrader && Net.Session.Instance != null && Net.Session.Instance.IsHost)
            {
                float y = attackerTransform != null ? (attackerTransform.rotation * Quaternion.Euler(90f, 180f, 0f)).eulerAngles.y : __instance.transform.eulerAngles.y;
                NetEnemies.Instance?.HostBlood(__instance, y);
            }
            if (!Threat.Enabled || attackerTransform == null) return;
            float dealt = __state - __instance.health;
            if (dealt > 0f) Threat.OnDamaged(__instance, attackerTransform.GetComponent<CharBase>(), dealt);
        }
    }

    /// <summary>Around each sight check: order the candidates by threat before it, credit being seen after it.</summary>
    [HarmonyPatch(typeof(Character), "canSeeEnemy")]
    public static class CharacterSightThreatPatch
    {
        private static void Prefix(Character __instance) => Threat.BeforeSightCheck(__instance);
        private static void Postfix(Character __instance) => Threat.AfterSightCheck(__instance);
    }

    /// <summary>Noise is threat for the player who made it.</summary>
    [HarmonyPatch(typeof(Character), nameof(Character.heardSound))]
    public static class CharacterHearingThreatPatch
    {
        private static void Prefix(Character __instance, Vector3 Position, float volume)
        {
            if (Threat.Enabled && !__instance.deaf && __instance.alive) Threat.OnHeard(__instance, Position, volume);
        }
    }

    /// <summary>
    /// The game shows (and runs) only the part of the world near the player: WorldGrid enters the grid's nodes
    /// near the player and leaves the rest. With other players in the host's world, the nodes near each of them
    /// are kept entered too, so the enemies near them are there and awake. While the host's own player is in a
    /// separate location or a dream, WorldGrid.currentGrid is that location's grid (OutsideLocations.transportToLocation
    /// leaves every node of the world's grid and calls setGrid), so the world's grid is also walked, around the other
    /// players only, and the enemies near them keep running. In the same way, the grid of each separate location the
    /// host runs for a joiner in it (NetPlayerProxy.InLocation, World.Locations.KeepRunning) is walked around the
    /// joiners there, whether or not the host's player is in it.
    ///
    /// One pass runs per grid at a time: a new refresh stops the grid's pass still running (one that entered one node
    /// a frame round a centre that has since jumped would otherwise leave, with the old centres, nodes the new one had
    /// just entered, and the locations there would flicker out and in again, running their exit and enter events).
    /// The nodes it entered stay entered, so the next pass goes on from there. The area kept round another player is
    /// what their own game keeps round them (ViewRects: their screen may be another shape than ours). While the host's own
    /// player is being taken to a place (OutsideLocations.loading, when the game's refreshPlayerPos skips its
    /// refresh), the others' areas are still entered round them (RefreshWhileLoading).
    /// </summary>
    [HarmonyPatch(typeof(WorldGrid), nameof(WorldGrid.refreshPosition))]
    public static class SharedCullingPatch
    {
        private static bool _wasAway;
        /// <summary>Host: our own player is in a separate location or a dream (the world's grid isn't the current one).</summary>
        public static bool HostAway => _wasAway;

        private static readonly Dictionary<WorldGrid.Grid, Coroutine> _running = new Dictionary<WorldGrid.Grid, Coroutine>();
        private static float _loadingRefreshAt;

        private struct Centre { public Vector3 Pos; public int Dx, Dy; }

        private static bool Prefix(WorldGrid __instance, Vector3 pos, bool instant, bool force)
        {
            if (__instance.currentGrid == null) return true;
            if (!Proxies.Any)
            {
                // Alone (or a joiner): the game's own refresh, except that its passes don't overlap either. A jump (a
                // respawn, a trip back from a place) otherwise has an older pass leave what the newer one entered.
                var s = Net.Session.Instance;
                if (instant || s == null || !s.Active) return true;
                Distances(out int dx, out int dy);
                Run(__instance, __instance.currentGrid, new List<Centre> { new Centre { Pos = pos, Dx = dx, Dy = dy } }, false, force);
                return false;
            }
            RefreshAll(__instance, __instance.currentGrid, pos, instant, force);
            World.FarEvents.Tick();
            return false;
        }

        /// <summary>
        /// Host, every frame: while our own player is taken to a place (OutsideLocations.loading: the loading screen,
        /// a few seconds), the game's refreshPlayerPos skips its refresh. The others' areas are still entered round
        /// them as they move: the world's grid round the players in the world, and the grid of each location run for
        /// a joiner round the joiners there. Nothing is left meanwhile (the game is switching our own grids over);
        /// the first refresh after the load leaves what's too far.
        /// </summary>
        public static void RefreshWhileLoading()
        {
            if (!Proxies.Any) return;
            var o = Singleton<OutsideLocations>.Instance;
            var wg = Singleton<WorldGrid>.Instance;
            if (o == null || !o.loading || wg == null || wg.disableRefresh || Player.Instance == null || Core.loadingGame || !Core.worldGenFinished()) return;
            if (Time.realtimeSinceStartup < _loadingRefreshAt) return;
            _loadingRefreshAt = Time.realtimeSinceStartup + 0.5f;
            var world = wg.getGrid("World");
            var grids = new Dictionary<WorldGrid.Grid, List<Centre>>();
            foreach (var p in NetPlayerProxy.All.Values)
            {
                if (p == null) continue;
                WorldGrid.Grid g = p.InLocation != null ? wg.getGrid(Core.getTrueLocationName(p.InLocation.name))
                                 : p.Away ? null : world;
                if (g == null) continue;
                if (!grids.TryGetValue(g, out var list)) grids[g] = list = new List<Centre>();
                list.Add(Of(p));
            }
            foreach (var kv in grids)
            {
                if (_running.TryGetValue(kv.Key, out var old) && old != null) wg.StopCoroutine(old);
                _running[kv.Key] = wg.StartCoroutine(EnterSpread(kv.Key, kv.Value));
            }
            if (grids.Count > 0) LoadingRefreshes++;
        }

        private static IEnumerator EnterSpread(WorldGrid.Grid g, List<Centre> centres)
        {
            var nodes = g.nodes;
            for (int i = 0; i < nodes.Count; i++)
                if (!nodes[i].entered && Near(nodes[i].position, centres)) { nodes[i].enter(); yield return null; }
        }

        /// <summary>For the tests: refreshes done while our player was being taken to a place.</summary>
        public static int LoadingRefreshes;

        private static void RefreshAll(WorldGrid wg, WorldGrid.Grid current, Vector3 pos, bool instant, bool force)
        {
            Distances(out int hx, out int hy);
            var centres = new List<Centre> { new Centre { Pos = pos, Dx = hx, Dy = hy } };
            foreach (var p in NetPlayerProxy.All.Values) if (p != null) centres.Add(Of(p));
            var world = wg.getGrid("World");
            bool away = world != null && world != current;
            // The other grids, each with the players in it: the world's (only while we're away), and the locations
            // run for joiners.
            var others = new Dictionary<WorldGrid.Grid, List<Centre>>();
            if (away) others[world] = new List<Centre>();
            foreach (var p in NetPlayerProxy.All.Values)
            {
                if (p == null) continue;
                WorldGrid.Grid g = p.InLocation != null ? wg.getGrid(Core.getTrueLocationName(p.InLocation.name))
                                 : p.Away ? null : world;
                if (g == null || g == current) continue;
                if (!others.TryGetValue(g, out var list)) others[g] = list = new List<Centre>();
                list.Add(Of(p));
            }
            if (away != _wasAway)
            {
                _wasAway = away;
                Plugin.Log.LogInfo(away ? $"The host is away ({current.name}): the world is kept running around {(others.TryGetValue(world, out var w) ? w.Count : 0)} other player(s)"
                                        : "The host is back in the world");
            }
            Run(wg, current, centres, instant, force);
            foreach (var kv in others)
                Run(wg, kv.Key, kv.Value, instant, force);
        }

        /// <summary>Refresh a grid, stopping the pass still running on it first.</summary>
        private static void Run(WorldGrid wg, WorldGrid.Grid g, List<Centre> centres, bool instant, bool force)
        {
            if (_running.TryGetValue(g, out var old))
            {
                if (old != null) wg.StopCoroutine(old);
                _running.Remove(g);
            }
            if (instant) Refresh(g, centres, force);
            else _running[g] = wg.StartCoroutine(RefreshSpread(g, centres, force));
        }

        internal static void Distances(out int dx, out int dy)
        {
            float m = Mathf.Max(Core.ResolutionWidthModifier, Core.ResolutionHeightModifier);
            float zoom = Singleton<Controller>.Instance.cameraZoom;
            dx = (int)((Screen.width + 500f * m) / zoom);
            dy = (int)((Screen.height + 500f * m) / zoom);
        }

        /// <summary>Another player's centre, with the rect their own game keeps running (ViewRects).</summary>
        private static Centre Of(NetPlayerProxy p)
        {
            ViewRects.For(p.Owner, out int dx, out int dy);
            return new Centre { Pos = p.transform.position, Dx = dx, Dy = dy };
        }

        private static bool Near(Vector2 node, List<Centre> centres)
        {
            foreach (var c in centres)
                if (Mathf.Abs(c.Pos.x - node.x) <= c.Dx && Mathf.Abs(c.Pos.z - node.y) <= c.Dy) return true;
            return false;
        }

        private static void Refresh(WorldGrid.Grid g, List<Centre> centres, bool force)
        {
            foreach (var n in g.nodes)
                if (Near(n.position, centres)) n.enter(force);
            foreach (var n in g.nodes)
                if (!Near(n.position, centres)) n.leave(force); // after the enters (see RefreshSpread)
        }

        // As the game's refreshNodes: enter one node a frame, so showing a region doesn't stall a frame. The nodes to
        // enter go first and the ones to leave after: a location (a large cullable) spans several nodes and is left
        // when none of them is entered, so leaving its far node before entering its near one would switch it off and
        // on again (its exit and enter events, its characters sent home).
        private static IEnumerator RefreshSpread(WorldGrid.Grid g, List<Centre> centres, bool force)
        {
            var nodes = g.nodes;
            var leave = new List<WorldGrid.Node>();
            for (int i = 0; i < nodes.Count; i++)
            {
                if (Near(nodes[i].position, centres))
                {
                    if (force || !nodes[i].entered) { nodes[i].enter(force); yield return null; }
                }
                else leave.Add(nodes[i]);
            }
            foreach (var n in leave) n.leave(force);
        }
    }
}

namespace DarkwoodMP.Combat
{
    /// <summary>Client: its own enemies never start; the host's are driven by the host (see NetEnemies).</summary>
    [HarmonyPatch(typeof(Character), nameof(Character.Start))]
    public static class ClientFreezeCharacterPatch
    {
        /// <summary>Set while Thaw starts a character itself.</summary>
        public static bool Starting;

        private static bool Prefix(Character __instance)
        {
            if (Starting || !NetEnemies.ClientMode || !NetEnemies.Eligible(__instance) || NetEnemies.LocalOnly(__instance)) return true;
            bool puppet = NetEnemies.Instance != null && NetEnemies.Instance.IsPuppet(__instance, out _);
            NetEnemies.Freeze(__instance, hide: !puppet);
            return false;
        }
    }

    /// <summary>
    /// Client: a frozen character stays frozen when the game switches its area on again (Location.activateCharacters
    /// as a separate location is entered, a culling node shown: Character.enableComponents(true) would turn its AI,
    /// pathfinding and animator back on and start its idle sound). NetEnemies.Thaw unfreezes the ones that are ours.
    /// </summary>
    [HarmonyPatch(typeof(Character), nameof(Character.enableComponents))]
    public static class ClientFrozenStaysPatch
    {
        private static bool Prefix(Character __instance, bool enable)
        {
            if (!enable || !NetEnemies.ClientMode || !NetEnemies.Frozen.Contains(__instance)) return true;
            // A copy of the host's enemy plays the host's clips on its sprite animator, which enableComponents(false)
            // (Location.activateCharacters as its area was left) switched off: that one is switched back on.
            if (NetEnemies.Instance != null && NetEnemies.Instance.IsPuppet(__instance, out _))
            {
                var a = __instance.animator;
                if (a != null) a.enabled = true;
            }
            return false;
        }
    }

    /// <summary>Client: copies of the host's dead enemies made from their prefab are switched off and on with their location (NetEnemies.LocationCharacters).</summary>
    [HarmonyPatch(typeof(Location), nameof(Location.activateCharacters))]
    public static class ClientCorpsesWithLocationPatch
    {
        private static void Postfix(Location __instance, bool active)
        {
            if (NetEnemies.ClientMode) NetEnemies.Instance?.LocationCharacters(__instance, active);
        }
    }

    /// <summary>Client: damage to a host's enemy is the host's to apply.</summary>
    [HarmonyPatch(typeof(Character), nameof(Character.getHit),
        new[] { typeof(float), typeof(Transform), typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(bool) })]
    public static class ClientForwardHitPatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(Character __instance, float Damage, Transform attackerTransform, bool canCutInHalf, bool byPlayer, bool canInterrupt, bool normalHit)
        {
            var ne = NetEnemies.Instance;
            if (ne == null || !NetEnemies.ClientMode) return true;
            if (!ne.IsPuppet(__instance, out uint id))
            {
                if (NetEnemies.Frozen.Contains(__instance)) return false; // a frozen one isn't really there
                // A friendly NPC our player hits: the host's copy takes it, and turns on us there.
                return !HostileNpcs.ClientHit(__instance, Damage, attackerTransform, canCutInHalf, byPlayer, canInterrupt, normalHit);
            }
            // A flame of no damage can still set it alight (its burn goes with the hit).
            if (Damage <= 0f && (NetEnemies.HitEffects == null || NetEnemies.HitEffects.Count == 0)) return false;
            // Something thrown (its impact, explosion or the fire it left) hits the enemy in the host's copy of the throw.
            if (World.ThrownHits.Now()) return false;
            // Of flames, only our own weapon's are ours to send (Flame calls every hit byPlayer); any other fire burns on the host.
            var flame = World.ThrownHits.CurrentFlame;
            if (flame != null && !EnemyAreaHits.ByLocalPlayer(flame)) return false;
            // Only what our own player does to it is ours to send. Anything else here (a trap, fire, an explosion)
            // happened on the host too, where it hurt the enemy already.
            bool fromPlayer = byPlayer || (attackerTransform != null && attackerTransform.GetComponent<Player>() != null);
            if (!fromPlayer) return false;
            ne.ClientHit(id, Damage, canCutInHalf, fromPlayer, canInterrupt, normalHit, NetEnemies.HitEffects);
            return false;
        }
    }

    /// <summary>Host: sounds the host's enemies make are played on the clients that see them.</summary>
    [HarmonyPatch(typeof(AudioController), "_PlayAsSound")]
    public static class HostEnemySoundPatch
    {
        private static void Postfix(string audioID, float volume, Transform parentObj)
        {
            var s = Net.Session.Instance;
            if (parentObj == null || s == null || !s.IsHost || s.Peers.Count == 0 || NetEnemies.Instance == null) return;
            NetEnemies.Instance.HostSound(parentObj, audioID, volume);
        }
    }

    /// <summary>
    /// Host: an enemy's loop that stops without another taking its place (CharacterSounds.playIdleLoop with no loop:
    /// a dog's aggressive loop is none, so its defensive one stops as it starts chasing) plays nothing, so
    /// HostEnemySoundPatch doesn't see it: the clients are sent an empty EnemySound, and their copy's loop stops too.
    /// </summary>
    [HarmonyPatch(typeof(CharacterSounds), nameof(CharacterSounds.playIdleLoop))]
    public static class HostLoopStopPatch
    {
        private static void Prefix(CharacterSounds __instance, out bool __state) => __state = __instance != null && __instance.idleAudioObject != null;

        private static void Postfix(CharacterSounds __instance, string loopName, bool __state)
        {
            if (!__state || !string.IsNullOrEmpty(loopName) || __instance == null || __instance.idleAudioObject != null) return;
            var s = Net.Session.Instance;
            if (s == null || !s.IsHost || s.Peers.Count == 0 || NetEnemies.Instance == null) return;
            NetEnemies.Instance.HostSound(__instance.transform, "", 0f);
        }
    }
}

namespace DarkwoodMP.Combat
{
    /// <summary>
    /// Character.alertInArea is how the game makes noise enemies hear. Raised from the Player's own code (steps,
    /// running, shots), it's that player's noise: on the host it's threat for them; on a client it's also sent to
    /// the host, which raises it there for that player's proxy, so the host's enemies hear remote players too.
    /// </summary>
    [HarmonyPatch(typeof(Character), nameof(Character.alertInArea))]
    public static class PlayerNoisePatch
    {
        private static void Prefix(Vector3 _pos, float range, bool dangerousSound, float volume, bool gunshot, out CharBase __state)
        {
            __state = Threat.NoiseSource;
            if (Threat.NoiseSource != null) return; // set by whoever raised it (a remote player's noise)
            if (OwnNoise <= 0 && !CalledFromPlayer()) return;
            var s = Net.Session.Instance;
            if (s != null && s.IsClient && NetEnemies.ClientMode)
            {
                var w = s.Begin(Net.MsgType.PlayerNoise);
                w.Write(_pos);
                w.W.Write(range); w.W.Write(volume);
                w.W.Write((byte)((dangerousSound ? 1 : 0) | (gunshot ? 2 : 0)));
                s.SendToHost(w, Net.Channel.Control, reliable: true);
            }
            Threat.NoiseSource = Player.Instance;
        }

        private static void Postfix(CharBase __state) => Threat.NoiseSource = __state;

        private static bool CalledFromPlayer()
        {
            var st = new System.Diagnostics.StackTrace(2, false);
            for (int i = 0; i < st.FrameCount && i < 6; i++)
            {
                var t = st.GetFrame(i)?.GetMethod()?.DeclaringType;
                if (t == null || t == typeof(PlayerNoisePatch) || t == typeof(Character)) continue;
                return t == typeof(Player) || (t.DeclaringType == typeof(Player)); // Player's methods, or its lambdas/iterators
            }
            return false;
        }

        /// <summary>
        /// Above zero inside the game's code that makes noise only for this game's own player (PlayerNoiseSourcePatch):
        /// those methods are patched too, so their stack frames can't be told apart by type.
        /// </summary>
        [System.ThreadStatic] public static int OwnNoise;

        /// <summary>Host: a client's character made noise; raise it here, as theirs.</summary>
        public static void OnRemoteNoise(ulong from, Net.PacketReader r)
        {
            var s = Net.Session.Instance;
            if (s == null || !s.IsHost) return;
            Vector3 pos = r.ReadVector3();
            float range = Mathf.Clamp(r.R.ReadSingle(), 0f, 2000f);
            float volume = Mathf.Clamp(r.R.ReadSingle(), 0f, 5f);
            byte f = r.R.ReadByte();
            if (!NetPlayerProxy.All.TryGetValue(from, out var proxy) || proxy == null) return;
            if ((proxy.transform.position - pos).sqrMagnitude > 500f * 500f) return; // it's meant to be where they are
            var before = Threat.NoiseSource;
            Threat.NoiseSource = proxy;
            try { Character.alertInArea(pos, range, (f & 1) != 0, volume, (f & 2) != 0); }
            finally { Threat.NoiseSource = before; }
        }
    }

    /// <summary>
    /// Noise the game makes only for its own player, outside Player's code: a door the player opens or closes
    /// (Door alerts only with a Player as the opener), the player's melee hit on a door, window or barricade
    /// (MeleeSensor alerts only for type player), and furniture the player pushes or drags (ItemSounds checks
    /// Player.Instance). Counted as the player's noise; on a client it's sent to the host like their steps.
    /// </summary>
    [HarmonyPatch]
    static class PlayerNoiseSourcePatch
    {
        static IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Door), nameof(Door.open), new[] { typeof(Vector3), typeof(Transform), typeof(float) });
            yield return AccessTools.Method(typeof(Door), nameof(Door.close), new[] { typeof(Transform) });
            yield return AccessTools.Method(typeof(MeleeSensor), "OnTriggerEnter");
            yield return AccessTools.Method(typeof(ItemSounds), nameof(ItemSounds.alertCharactersInArea));
        }

        static void Prefix(object __instance, object[] __args, out bool __state)
        {
            __state = false;
            var player = Player.Instance;
            if (player == null) return;
            switch (__instance)
            {
                case Door _: __state = __args.Length > 0 && __args[__args.Length > 1 ? 1 : 0] is Transform t && t != null && t.GetComponent<Player>() != null; break;
                case MeleeSensor m: __state = m.type == MeleeSensor.MeleeSensorType.player; break;
                case ItemSounds _: __state = true; break; // it checks our player itself
            }
            if (__state) PlayerNoisePatch.OwnNoise++;
        }

        static System.Exception Finalizer(System.Exception __exception, bool __state)
        {
            if (__state) PlayerNoisePatch.OwnNoise--;
            return __exception;
        }
    }

    /// <summary>Client: frozen and host-driven characters don't hear anything here.</summary>
    [HarmonyPatch(typeof(Character), nameof(Character.heardSound))]
    public static class ClientDeafPatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(Character __instance) => !(NetEnemies.ClientMode && NetEnemies.Frozen.Contains(__instance));
    }
}

namespace DarkwoodMP.Combat
{
    /// <summary>Client: note the weapon's effects while our player's melee hit lands, to send them with it.</summary>
    [HarmonyPatch(typeof(MeleeSensor), "OnTriggerEnter")]
    static class ClientHitEffectsPatch
    {
        static void Prefix(MeleeSensor __instance)
        {
            if (NetEnemies.ClientMode && __instance.type == MeleeSensor.MeleeSensorType.player) NetEnemies.HitEffects = __instance.effects;
        }
        static void Postfix() => NetEnemies.HitEffects = null;
        static System.Exception Finalizer(System.Exception __exception) { NetEnemies.HitEffects = null; return __exception; }
    }

    /// <summary>Client: effects don't run on the host's enemies here; the host runs them (they came with the hit).</summary>
    [HarmonyPatch]
    static class ClientNoPuppetEffectsPatch
    {
        // Every overload: weapons use the InvItemEffect one, fire and others the ones taking an effect type.
        static IEnumerable<System.Reflection.MethodBase> TargetMethods() =>
            AccessTools.GetDeclaredMethods(typeof(CharacterEffects)).FindAll(m => m.Name == nameof(CharacterEffects.activate));

        static bool Prefix(CharacterEffects __instance)
        {
            if (!NetEnemies.ClientMode || NetEnemies.Instance == null) return true;
            var c = __instance.GetComponent<Character>();
            return c == null || (!NetEnemies.Instance.IsPuppet(c, out _) && !NetEnemies.Frozen.Contains(c));
        }
    }
}

namespace DarkwoodMP.Combat
{
    /// <summary>"Attack the player" (scripted attacks, spawns that come for the player) means the player with the most threat.</summary>
    [HarmonyPatch(typeof(Character), nameof(Character.attackPlayer))]
    static class AttackPlayerThreatPatch
    {
        static bool Prefix(Character __instance)
        {
            if (!Threat.Enabled || __instance.dummy) return true;
            // An NPC a joiner's event sends after "the player" (a dialogue's outcome, run here for them) goes for them.
            var requester = __instance.npc != null ? World.Authority.Requester : null;
            if (requester != null) { HostileNpcs.SendAfter(__instance, requester); return false; }
            var best = Threat.BestPlayerFor(__instance);
            if (best == null || best is Player) return true; // the host's player: as the game does it
            if (__instance.aggressiveness != Aggressiveness.defensive) __instance.aggressiveness = Aggressiveness.attackOnSight;
            __instance.attackCharacter(best.transform);
            if (Threat.Debug) Plugin.Log.LogInfo($"[threat] {__instance.name} sent after the player: goes for {((NetPlayerProxy)best).OwnerName}");
            return false;
        }
    }

    /// <summary>
    /// Enemies that pursue the player constantly aim at Player.Instance.transform in their sight check. With others
    /// in the world, that's the player they have the most threat for: the transpiler swaps exactly that
    /// (Player.Instance followed by .transform) for Threat.PursuitTarget(this). The check's other uses of the
    /// host's player (its wards) are about the host's player and stay.
    /// </summary>
    [HarmonyPatch(typeof(Character), "canSeeEnemy")]
    static class ThreatPursuitTranspiler
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var getInstance = AccessTools.PropertyGetter(typeof(Player), nameof(Player.Instance));
            var getTransform = AccessTools.PropertyGetter(typeof(Component), nameof(Component.transform));
            var pursuit = AccessTools.Method(typeof(Threat), nameof(Threat.PursuitTarget));
            var list = new List<CodeInstruction>(instructions);
            int swapped = 0;
            for (int i = 0; i < list.Count - 1; i++)
            {
                if (list[i].Calls(getInstance) && list[i + 1].Calls(getTransform))
                {
                    var labels = list[i].labels;
                    list[i] = new CodeInstruction(System.Reflection.Emit.OpCodes.Ldarg_0) { labels = labels };
                    list[i + 1] = new CodeInstruction(System.Reflection.Emit.OpCodes.Call, pursuit);
                    swapped++;
                }
            }
            if (swapped == 0) Plugin.Log.LogWarning("ThreatPursuitTranspiler: no Player.Instance.transform in canSeeEnemy (game changed?)");
            return list;
        }
    }
}

namespace DarkwoodMP.Combat
{
    /// <summary>
    /// The game's enemy code measures "the player" as Player.Instance._transform.position: on the host that's only
    /// the host's own player. These transpilers swap exactly that read (Player.Instance, ._transform, .position)
    /// for a helper that knows the other players, in the methods listed below. With nobody else in the world the
    /// helpers return the host's player, so single player is unchanged.
    /// </summary>
    static class PlayerPositionTranspiler
    {
        /// <param name="characterArg">The argument that holds the Character: 0 (`this`) in Character's own methods.</param>
        public static IEnumerable<CodeInstruction> Swap(IEnumerable<CodeInstruction> instructions, System.Reflection.MethodInfo helper, string where, int characterArg = 0)
        {
            var loadCharacter = characterArg == 0 ? System.Reflection.Emit.OpCodes.Ldarg_0 : System.Reflection.Emit.OpCodes.Ldarg_1;
            var getInstance = AccessTools.PropertyGetter(typeof(Player), nameof(Player.Instance));
            var playerTransform = AccessTools.Field(typeof(Player), "_transform");
            var getPosition = AccessTools.PropertyGetter(typeof(Transform), nameof(Transform.position));
            var list = new List<CodeInstruction>(instructions);
            int swapped = 0;
            for (int i = 0; i < list.Count - 2; i++)
            {
                if (!list[i].Calls(getInstance) || !list[i + 1].LoadsField(playerTransform) || !list[i + 2].Calls(getPosition)) continue;
                var labels = list[i].labels;
                list[i] = new CodeInstruction(loadCharacter) { labels = labels };
                list[i + 1] = new CodeInstruction(System.Reflection.Emit.OpCodes.Call, helper) { labels = list[i + 1].labels };
                list[i + 2] = new CodeInstruction(System.Reflection.Emit.OpCodes.Nop) { labels = list[i + 2].labels };
                swapped++;
            }
            if (swapped == 0) Plugin.Log.LogWarning($"PlayerPositionTranspiler: no Player.Instance._transform.position in {where} (game changed?); it still uses the host's player");
            return list;
        }

        public static System.Reflection.MethodInfo Nearest => AccessTools.Method(typeof(Threat), nameof(Threat.NearestPlayerPos));
        public static System.Reflection.MethodInfo Flee => AccessTools.Method(typeof(Threat), nameof(Threat.FleeFrom));
    }

    /// <summary>
    /// Host: a spawned enemy (temporarySpawned, beyond 3500) or one on its way out (wantToDespawn, beyond 1500) is
    /// removed by its distance to the nearest player, not to the host's, so one chasing a far-off joiner stays.
    /// </summary>
    [HarmonyPatch(typeof(Character), "checkStuff")]
    static class DespawnByNearestPlayerPatch
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
            PlayerPositionTranspiler.Swap(instructions, PlayerPositionTranspiler.Nearest, "Character.checkStuff");
    }

    /// <summary>Host: a stalker told to flee (stalkerRunaway_onAttack starts setToFlee) runs from the player it was chasing.</summary>
    [HarmonyPatch(typeof(Character), "setToFlee")]
    static class FleeFromTargetPatch
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
            PlayerPositionTranspiler.Swap(instructions, PlayerPositionTranspiler.Flee, "Character.setToFlee");
    }

    /// <summary>Host: an enemy that runs away after an attack (runAwayAfterAttacking) runs from the player it attacked.</summary>
    [HarmonyPatch(typeof(Character), nameof(Character.attacking), MethodType.Setter)]
    static class RunAwayAfterAttackPatch
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
            PlayerPositionTranspiler.Swap(instructions, PlayerPositionTranspiler.Flee, "Character.attacking (setter)");
    }

    /// <summary>
    /// Host: a scripted runAway activity with no target (Character.Activity.run: "run from the player") runs from
    /// the player the character is after, or the nearest one, not always from the host's.
    /// </summary>
    [HarmonyPatch(typeof(Character.Activity), nameof(Character.Activity.run))]
    static class RunAwayActivityPatch
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
            PlayerPositionTranspiler.Swap(instructions, PlayerPositionTranspiler.Flee, "Character.Activity.run", characterArg: 1);
    }

    /// <summary>
    /// Host, with other players in the world: an escaping enemy runs from whichever player comes within 500 of it
    /// (the game checks only the host's), and one on its way out runs from the player it was after or the
    /// nearest. Otherwise the game's own method runs.
    /// </summary>
    [HarmonyPatch(typeof(Character), "checkIfBeingChased")]
    static class FleeFromAnyPlayerPatch
    {
        private static readonly Collider[] _near = new Collider[256];

        static bool Prefix(Character __instance)
        {
            if (!Threat.Enabled) return true;
            var c = __instance;
            if (c.summonsAfterEscaping) return false;
            if (c.wantToDespawn) { c.runAway(Threat.FleeFrom(c)); return false; }
            if (c.behaviour != Character.Behaviour.escaping) return false;
            Vector3 me = c.transform.position;
            int n = Physics.OverlapSphereNonAlloc(me, 500f, _near, 2099200); // the game's radius and layers
            CharBase nearest = null; float best = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                var cb = _near[i] != null ? _near[i].GetComponent<CharBase>() : null;
                if (!Proxies.IsPlayer(cb) || (cb is NetPlayerProxy && !cb.alive)) continue;
                float d = Core.trueDistance(cb.transform.position, me);
                if (d < best) { best = d; nearest = cb; }
            }
            if (nearest != null) c.runAway(nearest.transform.position);
            return false;
        }
    }
    /// <summary>
    /// The game's own Character.cutMeInHalf (a chomper cut by a blade) destroys the upper half's Rigidbody but leaves
    /// it active until its Cut_half animation ends, so Character.Update reads the destroyed body's position every frame
    /// of that animation and throws (about 40 NullReferenceExceptions per cut, in the game without the mod too). The
    /// exception only ended that frame's Update early; it's dropped for a character being cut in half.
    /// </summary>
    [HarmonyPatch(typeof(Character), nameof(Character.Update))]
    static class CutInHalfUpdatePatch
    {
        static System.Exception Finalizer(Character __instance, System.Exception __exception)
        {
            if (__exception is System.NullReferenceException && __instance != null && __instance.cuttingInHalf && __instance.GetComponent<Rigidbody>() == null) return null;
            return __exception;
        }
    }
}
