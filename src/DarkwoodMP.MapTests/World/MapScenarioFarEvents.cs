using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DarkwoodMP.Combat;
using HarmonyLib;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;
using Object = UnityEngine.Object;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Steps of the two-game map test for players far apart (the order they run
    /// in is tools/tests/map_scenario_farevents.py): the client jumps to a far
    /// world location, and the host checks how it ran that location's enter and
    /// exit events (World.FarEvents) and that its culling didn't make locations
    /// flicker (Combat.SharedCullingPatch).
    /// </summary>
    public static partial class MapScenarioFarEvents
    {
        // fe-reset
        private static float _since;
        // host: fe-jump to-client
        private static float _hostArrived;
        // host: the location both players were at
        private static string _shared;
        // host: record the player steps run here
        internal static bool WatchSteps;
        internal static readonly List<string> PlayerStepsRun =
            new List<string>();
        internal static int Dialogues;

        public static void Register()
        {
            // "fe-reset": start counting (enter and exit events, location
            // switches, player steps run, dialogues).
            MapScenario.Add("fe-reset", Reset);
            // "fe-jump loc:auto|loc:NAME|off:DX,DZ|gen:far|to-client": our
            // player is put there at once (a jump, as a respawn or a trip to a
            // place does).
            MapScenario.Add("fe-jump", Jump, timeout: 10f);
            // "fe-check-host" (host): the location by the client was entered
            // here for them, its enter events run without our player's part;
            // none of our player's steps ran, and no dialogue opened.
            MapScenario.Add("fe-check-host", CheckHost, timeout: 15f);
            // "fe-check-client" (client): the location by our player ran its
            // enter events as our player's and the shared part.
            MapScenario.Add("fe-check-client", CheckClient, timeout: 15f);
            // "fe-churn" (host): over 5 s, the locations by the client don't
            // switch, and since fe-reset no location was left and entered again
            // within a second (an older culling pass leaving what a newer one
            // entered).
            MapScenario.Add("fe-churn", Churn, timeout: 15f);
            // "fe-hostin" (host): we came to the client's location: our
            // player's part of its enter events ran.
            MapScenario.Add("fe-hostin", HostIn, timeout: 15f);
            // "fe-stay" (host): the client went: the location stays entered
            // (we're there), no exit events ran.
            MapScenario.Add("fe-stay", Stay, timeout: 15f);
            // "fe-left" (host): we went too: the location is left and its exit
            // events ran in full.
            MapScenario.Add("fe-left", Left, timeout: 20f);
            // "fe-generator" (host): the generator nearest the client drains by
            // the client's skill, not ours.
            MapScenario.Add("fe-generator", GeneratorSkill, timeout: 10f);
            // "fe-loading" (host): while our player was taken to a place, the
            // culling round the client was refreshed.
            MapScenario.Add("fe-loading", Loading, timeout: 10f);
        }

        private static string Name(Location l) =>
            Core.getTrueLocationName(l.name);

        private static int _refreshesAtReset;

        private static IEnumerator Reset(string[] a, Result r)
        {
            _since = Time.realtimeSinceStartup;
            PlayerStepsRun.Clear();
            Dialogues = 0;
            WatchSteps = Sync.PlayerSync.Instance != null
                && Net.Session.Instance != null && Net.Session.Instance.IsHost;
            _refreshesAtReset = SharedCullingPatch.LoadingRefreshes;
            var locs = WorldLocations()
                .OrderBy(l => StateDump.Flat(l.transform.position - Centre))
                .ToList();
            Plugin.Log
                .LogInfo($"[map] fe-reset: {locs.Count} world locations: " +
                string.Join(", ", locs.Select(l =>
                {
                    string name = Name(l);
                    string at = Fmt(l.transform.position);
                    int triggers = EventCount(l);
                    string note = "";
                    if (!Safe(l))
                    {
                        note = ", not safe";
                    }

                    return $"{name} at {at} ({triggers} enter/exit "
                        + $"triggers{note})";
                })));

            yield return Info(r, $"{locs.Count} world locations");
        }

        private static IEnumerable<Location> WorldLocations() =>
            Object.FindObjectsOfType<Location>(true).Where(
                FarEvents.WorldLocation);

        private static IEnumerable<global::EventTrigger> LocationTriggers(
            Location l) =>
            l.events.Where(e => e != null).SelectMany(e => e.eventTriggers)
                .Where(t => t != null &&
                (t.type == global::EventTrigger.Type.onEnterLocation
                    || t.type == global::EventTrigger.Type.onExitLocation));

        private static int EventCount(Location l) =>
            LocationTriggers(l).Count();

        private static readonly HashSet<GameEvent.Type> Unsafe =
            new HashSet<GameEvent.Type>
        {
            GameEvent.Type.openDialogue, GameEvent.Type.startDream,
            GameEvent.Type.endDream, GameEvent.Type.returnToWorld,
            GameEvent.Type.transportToOutsideLocation,
            GameEvent.Type.transportPlayerToObject, GameEvent.Type.forbidInputs,
            GameEvent.Type.timeScale, GameEvent.Type.endGame,
            GameEvent.Type.moveInventory, GameEvent.Type.tweenTime,
        };

        /// <summary>
        /// No enter or exit step that would take the player somewhere, stop the
        /// test or end the game.
        /// </summary>
        private static bool Safe(Location l)
        {
            if (l.name.IndexOf("hideout", StringComparison.OrdinalIgnoreCase)
                >= 0)
            {
                return false;
            }

            foreach (var t in LocationTriggers(l))
            {
                foreach (var g in new[]
                {
                    t.gameEvents,
                    t.gameEventsExit,
                    t.gameEventsElse
                })
                {
                    if (g != null && g.events != null
                        && g.events.Any(
                            e => e != null
                                && (Unsafe.Contains(e.type)
                                    || Chapters.IsStoryStep(e))))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// The far location the client goes to: the nearest safe one with enter
        /// or exit triggers 5000 or more from the centre.
        /// </summary>
        private static Location Auto()
        {
            var far = WorldLocations().Where(
                l => StateDump.Flat(l.transform.position - Centre) >= 5000f
                    && Safe(l))
                .OrderBy(l => StateDump.Flat(l.transform.position - Centre))
                .ToList();
            Location location = far.FirstOrDefault(l => EventCount(l) > 0);
            if (location == null)
            {
                location = far.FirstOrDefault();
            }

            return location;
        }

        private static NetPlayerProxy Joiner() =>
            NetPlayerProxy.All.Values.FirstOrDefault(
                p => p != null && !p.Away
                    && p.Owner != Sync.PlayerSync.DummyId);

        private static Location NearestTo(Vector3 at, float within) =>
            WorldLocations().Where(
                l => StateDump.Flat(l.transform.position - at) <= within)
                .OrderBy(l => StateDump.Flat(l.transform.position - at))
                .FirstOrDefault();

        private static IEnumerator Jump(string[] a, Result r)
        {
            var p = Player.Instance;
            if (p == null || a.Length == 0)
            {
                r.Fail("no player or no target");
                yield break;
            }

            string t = a[0];
            Vector3 to;
            string what;
            if (t.StartsWith("loc:"))
            {
                Location l;
                if (t == "loc:auto")
                {
                    l = Auto();
                }
                else
                {
                    l = WorldLocations().FirstOrDefault(
                        x => Name(x) == t.Substring(4));
                }

                if (l == null)
                {
                    r.Fail($"no location for {t}");
                    yield break;
                }

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
                var g = FuelSync.Generators.Where(x => x != null && !x.isSaw)
                    .OrderByDescending(
                        x => StateDump.Flat(x.transform.position
                                - Centre)).FirstOrDefault();
                if (g == null)
                {
                    r.Fail("no generator");
                    yield break;
                }

                to = g.transform.position + new Vector3(100f, 0f, 0f);
                what =
                    $"by {g.name}, "
                        + $"{StateDump.Flat(g.transform.position - Centre):0} "
                        + "from the centre";
            }
            else if (t == "to-client")
            {
                var j = Joiner();
                if (j == null)
                {
                    r.Fail("no joiner");
                    yield break;
                }

                to = j.transform.position + new Vector3(150f, 0f, 0f);
                what = $"beside {j.OwnerName}";
                _hostArrived = Time.realtimeSinceStartup;
                var l = NearestTo(j.transform.position, 2500f);
                if (l != null)
                {
                    _shared = Name(l);
                }
                else
                {
                    _shared = null;
                }
            }
            else
            {
                r.Fail($"unknown target {t}");
                yield break;
            }

            // our own player moves: what it sets off from here on is its own
            WatchSteps = false;
            float jumped = StateDump.Flat(to - p.transform.position);
            Place(to);
            yield return null;
            r.Info = $"at {Fmt(to)}, {what}, jumped {jumped:0}";
        }

        private static List<FarEvents.Entry> Since(
            List<FarEvents.Entry> list, float t, string loc = null) =>
            list.Where(e => e.At >= t
                    && (loc == null || e.Location == loc)).ToList();

        private static string Show(IEnumerable<FarEvents.Entry> es) =>
            string.Join(", ", es.Select(e => $"{e.Kind} {e.Part}"));

        private static IEnumerator CheckHost(string[] a, Result r)
        {
            var j = Joiner();
            if (j == null)
            {
                r.Fail("no joiner");
                yield break;
            }

            Location l = null;
            float until = Time.realtimeSinceStartup + 10f;
            // The location by the client is entered, and its enter events have
            // run.
            while (Time.realtimeSinceStartup < until)
            {
                l = NearestTo(j.transform.position, 2500f);
                if (l != null && l.entered && l.finishedLoading
                    && Since(FarEvents.Log, _since, Name(l)).Any(
                        e => e.Kind == "enter"))
                {
                    break;
                }

                yield return new WaitForSeconds(0.25f);
            }

            if (l == null)
            {
                r.Fail(
                    "no world location within 2500 of the client at "
                        + $"{Fmt(j.transform.position)}");
                yield break;
            }

            var es = Since(FarEvents.Log, _since, Name(l));
            string info =
                $"{Name(l)}: entered {l.entered}, its events here "
                    + $"[{Show(es)}], our player's steps run "
                    + $"[{string.Join(", ", PlayerStepsRun)}], dialogues "
                    + $"{Dialogues}";
            if (!l.entered)
            {
                r.Fail("not entered here for the client: " + info);
            }
            else if (es.Any(
                e => e.Part == ScriptedEvents.Part.All
                    || e.Part == ScriptedEvents.Part.PlayerOnly))
            {
                r.Fail("its events ran for our player: " + info);
            }
            else if (!es.Any(
                e => e.Kind == "enter"
                    && e.Part == ScriptedEvents.Part.HostAndShared))
            {
                r.Fail(
                    "its enter events didn't run (as the host's and shared "
                        + "part): " + info);
            }
            else if (PlayerStepsRun.Count > 0 || Dialogues > 0)
            {
                r.Fail(
                    "our player got steps of an event it didn't set off: "
                        + info);
            }

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
                if (l != null && l.entered
                    && Since(FarEvents.Log, _since, Name(l)).Any(
                        e => e.Kind == "enter"))
                {
                    break;
                }

                yield return new WaitForSeconds(0.25f);
            }

            if (l == null)
            {
                r.Fail("no world location by our player");
                yield break;
            }

            var es = Since(FarEvents.Log, _since, Name(l));
            r.Info =
                $"{Name(l)}: entered {l.entered}, its events here [{Show(es)}]";
            if (!es.Any(
                e => e.Kind == "enter"
                    && e.Part == ScriptedEvents.Part.PlayerAndShared))
            {
                r.Fail(
                    "its enter events didn't run as our player's and the "
                        + "shared part: " + r.Info);
            }
            else if (es.Any(e => e.Part != ScriptedEvents.Part.PlayerAndShared))
            {
                r.Fail("its events ran in another part: " + r.Info);
            }
        }
    }

    /// <summary>
    /// For the map test (fe-check-host): our player's steps that ran, and
    /// dialogues opened, while fe-reset watches.
    /// </summary>
    [HarmonyPatch]
    static class FarEventsTestCountPatch
    {
        [HarmonyPostfix, HarmonyPatch(
            typeof(GameEvent), nameof(GameEvent.fire))]
        static void Step(
            GameEvent __instance, bool __runOriginal, GameObject thisGO)
        {
            if (!MapScenarioFarEvents.WatchSteps || !__runOriginal
                || __instance == null
                || !ScriptedEvents.IsPlayerStep(__instance.type))
            {
                return;
            }

            if (MapScenarioFarEvents.PlayerStepsRun.Count < 50)
            {
                string objectName = "?";
                if (thisGO != null)
                {
                    objectName = thisGO.name;
                }

                MapScenarioFarEvents.PlayerStepsRun.Add(
                    $"{__instance.type} ({objectName})");
            }
        }

        [HarmonyPrefix, HarmonyPatch(
            typeof(DialogueWindow), nameof(DialogueWindow.show))]
        static void Dialogue()
        {
            if (MapScenarioFarEvents.WatchSteps)
            {
                MapScenarioFarEvents.Dialogues++;
            }
        }
    }
}
