using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DarkwoodMP.Combat;
using HarmonyLib;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;
using Object = UnityEngine.Object;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Steps of the two-game map test for players far apart (the order they run in is
    /// tools/tests/map_scenario_farevents.py): the client jumps to a far world location, and the host checks how it
    /// ran that location's enter and exit events (World.FarEvents) and that its culling didn't make locations flicker
    /// (Combat.SharedCullingPatch).
    /// </summary>
    public static class MapScenarioFarEvents
    {
        private static float _since;          // fe-reset
        private static float _hostArrived;    // host: fe-jump to-client
        private static string _shared;        // host: the location both players were at
        internal static bool WatchSteps;      // host: record the player steps run here
        internal static readonly List<string> PlayerStepsRun = new List<string>();
        internal static int Dialogues;

        public static void Register()
        {
            // "fe-reset": start counting (enter and exit events, location switches, player steps run, dialogues).
            MapScenario.Add("fe-reset", Reset);
            // "fe-jump loc:auto|loc:NAME|off:DX,DZ|gen:far|to-client": our player is put there at once (a jump, as a respawn or
            // a trip to a place does).
            MapScenario.Add("fe-jump", Jump, timeout: 10f);
            // "fe-check-host" (host): the location by the client was entered here for them, its enter events run
            // without our player's part; none of our player's steps ran, and no dialogue opened.
            MapScenario.Add("fe-check-host", CheckHost, timeout: 15f);
            // "fe-check-client" (client): the location by our player ran its enter events as our player's and the
            // shared part.
            MapScenario.Add("fe-check-client", CheckClient, timeout: 15f);
            // "fe-churn" (host): over 5 s, the locations by the client don't switch, and since fe-reset no location
            // was left and entered again within a second (an older culling pass leaving what a newer one entered).
            MapScenario.Add("fe-churn", Churn, timeout: 15f);
            // "fe-hostin" (host): we came to the client's location: our player's part of its enter events ran.
            MapScenario.Add("fe-hostin", HostIn, timeout: 15f);
            // "fe-stay" (host): the client went: the location stays entered (we're there), no exit events ran.
            MapScenario.Add("fe-stay", Stay, timeout: 15f);
            // "fe-left" (host): we went too: the location is left and its exit events ran in full.
            MapScenario.Add("fe-left", Left, timeout: 20f);
            // "fe-generator" (host): the generator nearest the client drains by the client's skill, not ours.
            MapScenario.Add("fe-generator", GeneratorSkill, timeout: 10f);
            // "fe-loading" (host): while our player was taken to a place, the culling round the client was refreshed.
            MapScenario.Add("fe-loading", Loading, timeout: 10f);
        }

        private static string Fmt(Vector3 p) => FormattableString.Invariant($"{p.x:0} {p.z:0}");
        private static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);
        private static string Name(Location l) => Core.getTrueLocationName(l.name);

        private static int _refreshesAtReset;

        private static IEnumerator Reset(string[] a, Result r)
        {
            _since = Time.realtimeSinceStartup;
            PlayerStepsRun.Clear();
            Dialogues = 0;
            WatchSteps = Sync.PlayerSync.Instance != null && Net.Session.Instance != null && Net.Session.Instance.IsHost;
            _refreshesAtReset = SharedCullingPatch.LoadingRefreshes;
            var locs = WorldLocations().OrderBy(l => StateDump.Flat(l.transform.position - Centre)).ToList();
            Plugin.Log.LogInfo($"[map] fe-reset: {locs.Count} world locations: " +
                string.Join(", ", locs.Select(l => $"{Name(l)} at {Fmt(l.transform.position)} ({EventCount(l)} enter/exit triggers{(Safe(l) ? "" : ", not safe")})")));
            yield return Info(r, $"{locs.Count} world locations");
        }

        private static IEnumerator Info(Result r, string info) { r.Info = info; yield break; }

        private static IEnumerable<Location> WorldLocations() =>
            Object.FindObjectsOfType<Location>(true).Where(FarEvents.WorldLocation);

        private static IEnumerable<global::EventTrigger> LocationTriggers(Location l) =>
            l.events.Where(e => e != null).SelectMany(e => e.eventTriggers).Where(t => t != null &&
                (t.type == global::EventTrigger.Type.onEnterLocation || t.type == global::EventTrigger.Type.onExitLocation));

        private static int EventCount(Location l) => LocationTriggers(l).Count();

        private static readonly HashSet<GameEvent.Type> Unsafe = new HashSet<GameEvent.Type>
        {
            GameEvent.Type.openDialogue, GameEvent.Type.startDream, GameEvent.Type.endDream, GameEvent.Type.returnToWorld,
            GameEvent.Type.transportToOutsideLocation, GameEvent.Type.transportPlayerToObject, GameEvent.Type.forbidInputs,
            GameEvent.Type.timeScale, GameEvent.Type.endGame, GameEvent.Type.moveInventory, GameEvent.Type.tweenTime,
        };

        /// <summary>No enter or exit step that would take the player somewhere, stop the test or end the game.</summary>
        private static bool Safe(Location l)
        {
            if (l.name.IndexOf("hideout", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            foreach (var t in LocationTriggers(l))
                foreach (var g in new[] { t.gameEvents, t.gameEventsExit, t.gameEventsElse })
                    if (g != null && g.events != null && g.events.Any(e => e != null && (Unsafe.Contains(e.type) || Chapters.IsStoryStep(e)))) return false;
            return true;
        }

        /// <summary>The far location the client goes to: the nearest safe one with enter or exit triggers 5000 or more from the centre.</summary>
        private static Location Auto()
        {
            var far = WorldLocations().Where(l => StateDump.Flat(l.transform.position - Centre) >= 5000f && Safe(l))
                .OrderBy(l => StateDump.Flat(l.transform.position - Centre)).ToList();
            return far.FirstOrDefault(l => EventCount(l) > 0) ?? far.FirstOrDefault();
        }

        private static NetPlayerProxy Joiner() =>
            NetPlayerProxy.All.Values.FirstOrDefault(p => p != null && !p.Away && p.Owner != Sync.PlayerSync.DummyId);

        private static Location NearestTo(Vector3 at, float within) =>
            WorldLocations().Where(l => StateDump.Flat(l.transform.position - at) <= within)
                .OrderBy(l => StateDump.Flat(l.transform.position - at)).FirstOrDefault();

        private static void Put(Vector3 to)
        {
            var p = Player.Instance;
            to.y = p.transform.position.y;
            p.transform.position = to;
            var rb = p.GetComponent<Rigidbody>();
            if (rb != null) rb.position = to;
        }

        private static IEnumerator Jump(string[] a, Result r)
        {
            var p = Player.Instance;
            if (p == null || a.Length == 0) { r.Fail("no player or no target"); yield break; }
            string t = a[0];
            Vector3 to; string what;
            if (t == "loc:auto" || t.StartsWith("loc:"))
            {
                var l = t == "loc:auto" ? Auto() : WorldLocations().FirstOrDefault(x => Name(x) == t.Substring(4));
                if (l == null) { r.Fail($"no location for {t}"); yield break; }
                to = l.transform.position + new Vector3(150f, 0f, 0f);
                what = $"{Name(l)} ({EventCount(l)} enter/exit triggers)";
            }
            else if (t.StartsWith("off:"))
            {
                var xz = t.Substring(4).Split(',');
                to = Centre + new Vector3(F(xz[0]), 0f, F(xz[1]));
                what = "off " + t.Substring(4);
            }
            else if (t == "gen:far")
            {
                var g = FuelSync.Generators.Where(x => x != null && !x.isSaw).OrderByDescending(x => StateDump.Flat(x.transform.position - Centre)).FirstOrDefault();
                if (g == null) { r.Fail("no generator"); yield break; }
                to = g.transform.position + new Vector3(100f, 0f, 0f);
                what = $"by {g.name}, {StateDump.Flat(g.transform.position - Centre):0} from the centre";
            }
            else if (t == "to-client")
            {
                var j = Joiner();
                if (j == null) { r.Fail("no joiner"); yield break; }
                to = j.transform.position + new Vector3(150f, 0f, 0f);
                what = $"beside {j.OwnerName}";
                _hostArrived = Time.realtimeSinceStartup;
                var l = NearestTo(j.transform.position, 2500f);
                _shared = l != null ? Name(l) : null;
            }
            else { r.Fail($"unknown target {t}"); yield break; }
            WatchSteps = false; // our own player moves: what it sets off from here on is its own
            float jumped = StateDump.Flat(to - p.transform.position);
            Put(to);
            yield return null;
            r.Info = $"at {Fmt(to)}, {what}, jumped {jumped:0}";
        }

        private static List<FarEvents.Entry> Since(List<FarEvents.Entry> list, float t, string loc = null) =>
            list.Where(e => e.At >= t && (loc == null || e.Location == loc)).ToList();

        private static string Show(IEnumerable<FarEvents.Entry> es) => string.Join(", ", es.Select(e => $"{e.Kind} {e.Part}"));

        private static IEnumerator CheckHost(string[] a, Result r)
        {
            var j = Joiner();
            if (j == null) { r.Fail("no joiner"); yield break; }
            Location l = null;
            float until = Time.realtimeSinceStartup + 10f;
            // The location by the client is entered, and its enter events have run.
            while (Time.realtimeSinceStartup < until)
            {
                l = NearestTo(j.transform.position, 2500f);
                if (l != null && l.entered && l.finishedLoading && Since(FarEvents.Log, _since, Name(l)).Any(e => e.Kind == "enter")) break;
                yield return new WaitForSeconds(0.25f);
            }
            if (l == null) { r.Fail($"no world location within 2500 of the client at {Fmt(j.transform.position)}"); yield break; }
            var es = Since(FarEvents.Log, _since, Name(l));
            string info = $"{Name(l)}: entered {l.entered}, its events here [{Show(es)}], our player's steps run [{string.Join(", ", PlayerStepsRun)}], dialogues {Dialogues}";
            if (!l.entered) r.Fail("not entered here for the client: " + info);
            else if (es.Any(e => e.Part == ScriptedEvents.Part.All || e.Part == ScriptedEvents.Part.PlayerOnly)) r.Fail("its events ran for our player: " + info);
            else if (!es.Any(e => e.Kind == "enter" && e.Part == ScriptedEvents.Part.HostAndShared)) r.Fail("its enter events didn't run (as the host's and shared part): " + info);
            else if (PlayerStepsRun.Count > 0 || Dialogues > 0) r.Fail("our player got steps of an event it didn't set off: " + info);
            r.Info = info;
        }

        private static IEnumerator CheckClient(string[] a, Result r)
        {
            var p = Player.Instance;
            Location l = null;
            float until = Time.realtimeSinceStartup + 10f;
            while (Time.realtimeSinceStartup < until)
            {
                l = NearestTo(p.transform.position, 2500f);
                if (l != null && l.entered && Since(FarEvents.Log, _since, Name(l)).Any(e => e.Kind == "enter")) break;
                yield return new WaitForSeconds(0.25f);
            }
            if (l == null) { r.Fail("no world location by our player"); yield break; }
            var es = Since(FarEvents.Log, _since, Name(l));
            r.Info = $"{Name(l)}: entered {l.entered}, its events here [{Show(es)}]";
            if (!es.Any(e => e.Kind == "enter" && e.Part == ScriptedEvents.Part.PlayerAndShared)) r.Fail("its enter events didn't run as our player's and the shared part: " + r.Info);
            else if (es.Any(e => e.Part != ScriptedEvents.Part.PlayerAndShared)) r.Fail("its events ran in another part: " + r.Info);
        }

        /// <summary>Locations left and entered again within a second, since t.</summary>
        private static List<string> Flickers(float t)
        {
            var list = new List<string>();
            foreach (var g in Since(FarEvents.Switches, t).GroupBy(e => e.Location))
            {
                var es = g.OrderBy(e => e.At).ToList();
                for (int i = 1; i < es.Count; i++)
                    if (es[i - 1].Kind == "leave" && es[i].Kind == "enter" && es[i].At - es[i - 1].At < 1f)
                        list.Add($"{g.Key} ({es[i].At - es[i - 1].At:0.00} s)");
            }
            return list;
        }

        private static IEnumerator Churn(string[] a, Result r)
        {
            // The host watches round the client's stand-in; the client round its own player.
            Component j = Net.Session.Instance != null && Net.Session.Instance.IsHost ? (Component)Joiner() : Player.Instance;
            if (j == null) { r.Fail("no joiner"); yield break; }
            float t0 = Time.realtimeSinceStartup;
            yield return new WaitForSeconds(5f);
            var near = new HashSet<string>(WorldLocations().Where(l => StateDump.Flat(l.transform.position - j.transform.position) <= 2000f).Select(Name));
            var switched = Since(FarEvents.Switches, t0).Where(e => near.Contains(e.Location)).ToList();
            var flickers = Flickers(_since);
            var all = Since(FarEvents.Switches, _since);
            r.Info = $"since fe-reset {all.Count(e => e.Kind == "enter")} location enters, {all.Count(e => e.Kind == "leave")} leaves; " +
                     $"by the client ({string.Join(", ", near)}) in the last 5 s: [{string.Join(", ", switched.Select(e => e.Location + " " + e.Kind))}]; flickers [{string.Join(", ", flickers)}]";
            if (switched.Count > 0) r.Fail("a location by the client switched while it stood still: " + r.Info);
            else if (flickers.Count > 0) r.Fail("locations left and entered again at once: " + r.Info);
        }

        private static IEnumerator HostIn(string[] a, Result r)
        {
            if (_shared == null) { r.Fail("no location by the client (fe-jump to-client first)"); yield break; }
            float until = Time.realtimeSinceStartup + 8f;
            List<FarEvents.Entry> es = null;
            while (Time.realtimeSinceStartup < until)
            {
                es = Since(FarEvents.Log, _hostArrived, _shared);
                if (es.Any(e => e.Kind == "enter")) break;
                yield return new WaitForSeconds(0.25f);
            }
            r.Info = $"{_shared}: its events since we came [{Show(es)}]";
            if (!es.Any(e => e.Kind == "enter" && e.Part == ScriptedEvents.Part.PlayerOnly)) r.Fail("our player's part of its enter events didn't run: " + r.Info);
            else if (es.Any(e => e.Part != ScriptedEvents.Part.PlayerOnly)) r.Fail("its events ran again for the world: " + r.Info);
        }

        private static IEnumerator Stay(string[] a, Result r)
        {
            var l = WorldLocations().FirstOrDefault(x => Name(x) == _shared);
            if (l == null) { r.Fail("no shared location"); yield break; }
            yield return new WaitForSeconds(2f);
            var es = Since(FarEvents.Log, _hostArrived, _shared);
            r.Info = $"{_shared}: entered {l.entered}, its events since we came [{Show(es)}]";
            if (!l.entered) r.Fail("left while our player is in it: " + r.Info);
            else if (es.Any(e => e.Kind == "exit")) r.Fail("its exit events ran while our player is in it: " + r.Info);
        }

        private static IEnumerator Left(string[] a, Result r)
        {
            var l = WorldLocations().FirstOrDefault(x => Name(x) == _shared);
            if (l == null) { r.Fail("no shared location"); yield break; }
            float until = Time.realtimeSinceStartup + 15f;
            while (l.entered && Time.realtimeSinceStartup < until) yield return new WaitForSeconds(0.25f);
            var es = Since(FarEvents.Log, _hostArrived, _shared);
            r.Info = $"{_shared}: entered {l.entered}, its events since we came [{Show(es)}]";
            if (l.entered) r.Fail("still entered after everyone went: " + r.Info);
            else if (!es.Any(e => e.Kind == "exit" && e.Part == ScriptedEvents.Part.All)) r.Fail("its exit events didn't run in full: " + r.Info);
            else if (es.Count(e => e.Kind == "exit") != 1) r.Fail("its exit events ran more than once: " + r.Info);
        }

        private static IEnumerator GeneratorSkill(string[] a, Result r)
        {
            var j = Joiner();
            if (j == null) { r.Fail("no joiner"); yield break; }
            var gen = FuelSync.Generators.Where(g => g != null && !g.isSaw)
                .OrderBy(g => StateDump.Flat(g.transform.position - j.transform.position)).FirstOrDefault();
            if (gen == null) { r.Info = "no generator in this world"; yield break; }
            bool clientNearer = StateDump.Flat(gen.transform.position - j.transform.position) < StateDump.Flat(gen.transform.position - Player.Instance.transform.position);
            float fuel = gen.fuel;
            gen.drainFuel();
            float used = fuel - gen.fuel;
            gen.fuel = fuel;
            gen.drainFuel(); gen.fuel = fuel; // the low-power state as it was
            var skill = Characters.Instance?.Electrician(j.Owner);
            r.Info = $"{GeneratorDrainSkillPatch.Last}; used {used:0.###} (the client nearer: {clientNearer}, its electrician skill {(skill.HasValue ? skill.Value.ToString() : "not known")})";
            if (clientNearer && skill.HasValue && GeneratorDrainSkillPatch.Last != null && !GeneratorDrainSkillPatch.Last.Contains(j.OwnerName))
                r.Fail("drained by our skill: " + r.Info);
        }

        private static IEnumerator Loading(string[] a, Result r)
        {
            int refreshes = SharedCullingPatch.LoadingRefreshes - _refreshesAtReset;
            r.Info = $"{refreshes} culling refreshes round the others while our player was taken to a place, since fe-reset";
            if (refreshes == 0) r.Fail("the others' areas weren't refreshed while we loaded: " + r.Info);
            yield break;
        }
    }

    /// <summary>For the map test (fe-check-host): our player's steps that ran, and dialogues opened, while fe-reset watches.</summary>
    [HarmonyPatch]
    static class FarEventsTestCountPatch
    {
        [HarmonyPostfix, HarmonyPatch(typeof(GameEvent), nameof(GameEvent.fire))]
        static void Step(GameEvent __instance, bool __runOriginal, GameObject thisGO)
        {
            if (!MapScenarioFarEvents.WatchSteps || !__runOriginal || __instance == null || !ScriptedEvents.IsPlayerStep(__instance.type)) return;
            if (MapScenarioFarEvents.PlayerStepsRun.Count < 50) MapScenarioFarEvents.PlayerStepsRun.Add($"{__instance.type} ({(thisGO != null ? thisGO.name : "?")})");
        }

        [HarmonyPrefix, HarmonyPatch(typeof(DialogueWindow), nameof(DialogueWindow.show))]
        static void Dialogue() { if (MapScenarioFarEvents.WatchSteps) MapScenarioFarEvents.Dialogues++; }
    }
}
