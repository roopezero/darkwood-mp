using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMP.Combat
{
    /// <summary>
    /// Night attackers and night worms, for every player. The game spawns both around Player.Instance only:
    /// CharacterSpawner.spawnNightChar sends one of the night scenario's attackers at the player while they are at
    /// a hideout (a Location with playerBase), and waitToSpawnWorm sends a night worm at them every five seconds
    /// of a hard night. The host now does the same around each remote player's proxy, and joiners spawn neither
    /// (anything they spawned would only be frozen and hidden there, see NetEnemies.LocalOnly).
    ///
    /// A joiner is at a hideout when the game's own WhereAmI check, run at their proxy's position on the host,
    /// finds a hideout location: the nearest Location collider within 300, or the big location it belongs to.
    /// Each occupied hideout has the night's attackers to itself, as the one hideout of a game played alone: every
    /// spawnInterval, one attacker goes to each hideout that has a player in it (the host's own player, or a
    /// joiner's proxy), aimed at the host's player if they are there, else the first joiner there, and the
    /// scenario's numbers (how many of each kind may be out at once, CharacterToSpawn.amount) are counted per
    /// hideout. Players who share a hideout share its budget: three players in one hideout get the attackers one
    /// player would, not three each interval. A player out of any hideout gets no night attackers (the game sends
    /// none at a player outside), only the worms. The scenario's own count (CharacterToSpawn.spawned) still goes
    /// up and down with every attacker, as the game keeps it; it just no longer decides. Alone (no other
    /// players), the game's own spawnNightChar runs unchanged.
    /// A shadow ward stops the worms of the player who has it: the host's own, and a joiner's from their PlayerState
    /// (NetPlayerProxy.ShadowWard), as the game's own loop checks Player.Instance's.
    /// </summary>
    public static class NightSpawns
    {
        private static readonly Collider[] _near = new Collider[64];

        private sealed class Sent { public NetPlayerProxy Proxy; public Location Loc; }
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Character, Sent> _sent =
            new System.Runtime.CompilerServices.ConditionalWeakTable<Character, Sent>();

        /// <summary>A night attacker this sent at a joiner's hideout: that joiner and the hideout (JoinerAi uses them for its activities).</summary>
        public static bool SentAt(Character c, out NetPlayerProxy proxy, out Location loc)
        {
            proxy = null; loc = null;
            if (c == null || !_sent.TryGetValue(c, out var s)) return false;
            proxy = s.Proxy; loc = s.Loc;
            return true;
        }

        /// <summary>The big location at a position, found the way WhereAmI.checkWhereAmI finds the player's.</summary>
        public static Location BigLocationAt(Vector3 pos)
        {
            int n = Physics.OverlapSphereNonAlloc(pos, 300f, _near, 73402368); // WhereAmI's radius and layers
            Location closest = null; float best = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                var loc = _near[i] != null ? _near[i].GetComponent<Location>() : null;
                if (loc == null) continue;
                float d = Core.trueDistance(pos, loc.transform.position);
                if (d < best) { best = d; closest = loc; }
            }
            if (closest == null || !closest.isSubLocation) return closest;
            var t = closest.transform;
            return t.parent != null && t.parent.parent != null ? t.parent.parent.GetComponent<Location>() : null;
        }

        public static bool AtHideout(Vector3 pos, out Location loc)
        {
            loc = BigLocationAt(pos);
            return loc != null && loc.playerBase;
        }

        private static IEnumerable<NetPlayerProxy> LivingProxies()
        {
            foreach (var p in new List<NetPlayerProxy>(NetPlayerProxy.All.Values))
                if (p != null && p.alive && p.InLocation == null) yield return p; // nobody is sent at night into a separate location
        }

        // Host: the attackers out at each hideout, by kind, and which hideout and kind each one counts against.
        private static readonly Dictionary<Location, Dictionary<string, int>> _out = new Dictionary<Location, Dictionary<string, int>>();
        private sealed class Spent { public Location Loc; public string Kind; }
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Character, Spent> _spent =
            new System.Runtime.CompilerServices.ConditionalWeakTable<Character, Spent>();

        /// <summary>The attackers of a kind out at a hideout now (counted against its budget).</summary>
        public static int OutAt(Location loc, string kind) =>
            loc != null && _out.TryGetValue(loc, out var k) && k.TryGetValue(kind, out int n) ? n : 0;

        /// <summary>Every attacker sent while playing together since the day began: its hideout's name, who it was aimed at (for the map test).</summary>
        public static readonly List<(string Hideout, string At, Character C)> SentLog = new List<(string, string, Character)>();

        /// <summary>
        /// Host, with other players: replaces CharacterSpawner.spawnNightChar. One attacker for each occupied hideout,
        /// aimed at the host's player if they are in it, else at the first joiner there.
        /// </summary>
        public static void NightChars(CharacterSpawner spawner)
        {
            if (spawner == null || !spawner.spawnNocturnalCharacters || Singleton<Dreams>.Instance.dreaming) return;
            var hideouts = new List<(Location Loc, NetPlayerProxy Proxy)>();
            var host = Player.Instance;
            // The host's player as the game checks it (the game sends nobody while the host is in an outside location).
            var hostLoc = host != null && host.whereAmI != null ? host.whereAmI.bigLocation : null;
            if (!Singleton<OutsideLocations>.Instance.playerInOutsideLocation && hostLoc != null && hostLoc.playerBase)
                hideouts.Add((hostLoc, null));
            foreach (var proxy in LivingProxies())
                if (AtHideout(proxy.transform.position, out var loc) && !hideouts.Exists(h => h.Loc == loc)) hideouts.Add((loc, proxy));
            foreach (var h in hideouts) SpawnNightChar(spawner, h.Proxy, h.Loc);
        }

        /// <summary>The body of CharacterSpawner.spawnNightChar, aimed at a proxy (or the host's player, when null) at the
        /// hideout loc, counted against that hideout's budget.</summary>
        public static Character SpawnNightChar(CharacterSpawner spawner, NetPlayerProxy proxy, Location loc)
        {
            var scenarios = Singleton<NightScenarios>.Instance;
            if (scenarios.currentScenario == null) scenarios.setCurrentScenario();
            if (scenarios.currentScenario == null) return null;
            foreach (var toSpawn in scenarios.currentScenario.characters)
            {
                if (toSpawn == null || string.IsNullOrEmpty(toSpawn.characterName) || toSpawn.amount <= 0 || OutAt(loc, toSpawn.characterName) >= toSpawn.amount) continue;
                var at = proxy != null ? proxy.gameObject : Player.Instance.gameObject;
                var c = spawner.spawnCharacterAround(at, Vector3.zero, 1500f, toSpawn.characterName, nocturnal: true);
                if (c == null) return null;
                if (proxy != null)
                {
                    _sent.Remove(c);
                    _sent.Add(c, new Sent { Proxy = proxy, Loc = loc });
                    // spawnCharacterAround gave it the host's location's waypoints (or none); this hideout's are the right ones.
                    if (loc != null && loc.waypoints != null && loc.waypoints.Count > 0) c.setWaypoints(loc.waypoints);
                }
                if (LaterChapter() && Random.Range(0f, 1f) > 0.5f) c.gameObject.AddComponent<ShadowArmor>();
                toSpawn.spawned++;
                if (loc != null)
                {
                    if (!_out.TryGetValue(loc, out var kinds)) _out[loc] = kinds = new Dictionary<string, int>();
                    kinds.TryGetValue(toSpawn.characterName, out int n);
                    kinds[toSpawn.characterName] = n + 1;
                    _spent.Remove(c);
                    _spent.Add(c, new Spent { Loc = loc, Kind = toSpawn.characterName });
                }
                string who = proxy != null ? proxy.OwnerName : "the host";
                SentLog.Add((loc != null ? loc.name : "-", who, c));
                Plugin.Log.LogInfo($"[night] {c.name} sent at {who}'s hideout {(loc != null ? loc.name : "-")} ({OutAt(loc, toSpawn.characterName)} of {toSpawn.amount} {toSpawn.characterName} out there), " +
                                   $"{Core.trueDistance(c.transform.position, at.transform.position):0} from them");
                return c;
            }
            return null;
        }

        /// <summary>A night attacker is gone (CharacterSpawner.removeFromNightChars): one fewer out at its hideout.</summary>
        internal static void Gone(Character c)
        {
            if (c == null || !_spent.TryGetValue(c, out var s)) return;
            _spent.Remove(c);
            if (s.Loc != null && _out.TryGetValue(s.Loc, out var kinds) && kinds.TryGetValue(s.Kind, out int n))
                kinds[s.Kind] = Mathf.Max(0, n - 1);
        }

        /// <summary>A new day (NightScenarios.resetScenarios): every hideout's budget starts again.</summary>
        internal static void NewDay() { _out.Clear(); SentLog.Clear(); }

        private static bool LaterChapter()
        {
            if (Core.randomGeneration) return Singleton<WorldGenerator>.Instance.chapterID > 1;
            var scene = GameObject.Find(Helpers.GetSceneName());
            var l = scene != null ? scene.GetComponent<Location>() : null;
            return l != null && l.chapterId > 1;
        }

        /// <summary>
        /// Replaces CharacterSpawner.waitToSpawnWorm: every five seconds of a hard night (not dreaming), a night
        /// worm 1500 to 2000 from each player without a shadow ward, sent at that player. Joiners don't spawn worms.
        /// </summary>
        public static IEnumerator WormLoop(CharacterSpawner spawner)
        {
            while (true)
            {
                yield return new WaitForSeconds(5f);
                if (NetEnemies.ClientMode || spawner == null) continue;
                try
                {
                    var host = Player.Instance;
                    if (host == null || !Singleton<Controller>.Instance.isHardNight || Core.isDay() || Singleton<Dreams>.Instance.dreaming || host.ignoreNightSickness) continue;
                    if (!host.effects.hasEffectType(CharacterEffectType.shadowWard)) SpawnWorm(spawner, null);
                    foreach (var proxy in LivingProxies()) if (WormWanted(proxy)) SpawnWorm(spawner, proxy);
                }
                catch (System.Exception e) { Plugin.Log.LogWarning($"Night worm spawn failed: {e.Message}"); }
            }
        }

        /// <summary>The worm loop sends worms at this joiner: alive, in the world (not in a separate location the host runs for them), and without a shadow ward.</summary>
        public static bool WormWanted(NetPlayerProxy proxy) => proxy != null && proxy.alive && proxy.InLocation == null && !proxy.ShadowWard;

        /// <summary>A night worm around a proxy (or the host's player, when null), going for them.</summary>
        public static Character SpawnWorm(CharacterSpawner spawner, NetPlayerProxy proxy)
        {
            Vector3 around = proxy != null ? proxy.transform.position : Player.Instance._transform.position;
            Vector3 pos = Core.randomPosAround(around, 1500f, 2000f, canBeInside: true, mustBeInsideGraph: false);
            var go = Core.AddPrefab("characters/fakechars/NightWorms_01", pos, Quaternion.Euler(90f, Random.Range(0, 360), 0f), null);
            var c = go != null ? go.GetComponent<Character>() : null;
            if (c == null) return null;
            if (proxy == null) c.attackPlayer();
            else
            {
                if (c.aggressiveness != Aggressiveness.defensive) c.aggressiveness = Aggressiveness.attackOnSight;
                c.attackCharacter(proxy.transform);
            }
            spawner.nocturnalCharacters.Add(c.gameObject);
            if (proxy != null && Threat.Debug)
                Plugin.Log.LogInfo($"[night] night worm sent at {proxy.OwnerName}, {Core.trueDistance(pos, around):0} from them");
            return c;
        }

        /// <summary>
        /// "--dwmp-test-night": once a remote player (or the test dummy) is in, check their hideout and send one night
        /// worm and one night attacker at them, whatever the time, logging where each was spawned.
        /// </summary>
        public static IEnumerator TestAfterJoin()
        {
            while (NetPlayerProxy.All.Count == 0) yield return new WaitForSeconds(0.5f);
            yield return new WaitForSeconds(2f);
            var spawner = Singleton<CharacterSpawner>.Instance;
            foreach (var proxy in LivingProxies())
            {
                bool home = AtHideout(proxy.transform.position, out var loc);
                Plugin.Log.LogInfo($"[night test] {proxy.OwnerName} at {proxy.transform.position}: location {(loc != null ? loc.name : "none")}, at a hideout: {home}");
                var worm = SpawnWorm(spawner, proxy);
                Plugin.Log.LogInfo(worm != null
                    ? $"[night test] worm for {proxy.OwnerName} at {worm.transform.position}, {Core.trueDistance(worm.transform.position, proxy.transform.position):0} from them, going for {(worm.target != null ? worm.target.name : "nobody")}"
                    : $"[night test] no worm for {proxy.OwnerName}");
                // A night attacker as at a hideout: the host's own location if the proxy isn't at one.
                var at = loc ?? (Player.Instance.whereAmI != null ? Player.Instance.whereAmI.bigLocation : null);
                if (at != null)
                {
                    var c = SpawnNightChar(spawner, proxy, at);
                    Plugin.Log.LogInfo(c != null ? $"[night test] night attacker {c.name} for {proxy.OwnerName} at {c.transform.position}"
                                                 : $"[night test] no night attacker for {proxy.OwnerName} (the scenario has none left)");
                    JoinerAi.Test(c, proxy);
                }
                else Plugin.Log.LogInfo($"[night test] no location to send a night attacker from for {proxy.OwnerName}");
            }
            Plugin.Log.LogInfo("[night test] done");
        }
    }

    /// <summary>Joiners send nobody at night: the host does it for every player, a budget per occupied hideout.</summary>
    [HarmonyPatch(typeof(CharacterSpawner), "spawnNightChar")]
    static class NightCharsPatch
    {
        static bool Prefix(CharacterSpawner __instance)
        {
            if (NetEnemies.ClientMode) return false;
            if (!Proxies.Any) return true; // alone: the game's own
            try { NightSpawns.NightChars(__instance); }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Night attacker spawn failed: {e.Message}"); }
            return false;
        }
    }

    /// <summary>A night attacker gone (dead, or despawned) is one fewer out at the hideout it was sent to.</summary>
    [HarmonyPatch(typeof(CharacterSpawner), nameof(CharacterSpawner.removeFromNightChars))]
    static class NightCharGonePatch
    {
        static void Prefix(Character _character)
        {
            if (_character != null && !Core.isDay()) NightSpawns.Gone(_character);
        }
    }

    /// <summary>At dawn the scenarios start again (NightScenarios.resetScenarios), and so does every hideout's budget.</summary>
    [HarmonyPatch(typeof(NightScenarios), nameof(NightScenarios.resetScenarios))]
    static class NightBudgetResetPatch
    {
        static void Postfix() => NightSpawns.NewDay();
    }

    /// <summary>The worm coroutine is replaced by NightSpawns.WormLoop (the same rule, for each player).</summary>
    [HarmonyPatch(typeof(CharacterSpawner), "waitToSpawnWorm")]
    static class NightWormsPatch
    {
        static bool Prefix(CharacterSpawner __instance, ref IEnumerator __result)
        {
            __result = NightSpawns.WormLoop(__instance);
            return false;
        }
    }
}
