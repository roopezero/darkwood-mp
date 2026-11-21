using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DarkwoodMP.Combat;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;

namespace DarkwoodMP.World
{
    /// <summary>The host's checks as both players come and go.</summary>
    public static partial class MapScenarioFarEvents
    {
        /// <summary>
        /// Locations left and entered again within a second, since t.
        /// </summary>
        private static List<string> Flickers(float t)
        {
            var list = new List<string>();
            foreach (var g in Since(FarEvents.Switches, t).GroupBy(
                e => e.Location))
            {
                var es = g.OrderBy(e => e.At).ToList();
                for (int i = 1; i < es.Count; i++)
                {
                    if (es[i - 1].Kind == "leave" && es[i].Kind == "enter"
                        && es[i].At - es[i - 1].At < 1f)
                    {
                        list.Add($"{g.Key} ({es[i].At - es[i - 1].At:0.00} s)");
                    }
                }
            }

            return list;
        }

        private static IEnumerator Churn(string[] a, Result r)
        {
            // The host watches round the client's stand-in; the client round
            // its own player.
            Component j;
            if (Net.Session.Instance != null && Net.Session.Instance.IsHost)
            {
                j = (Component)Joiner();
            }
            else
            {
                j = Player.Instance;
            }

            if (j == null)
            {
                r.Fail("no joiner");
                yield break;
            }

            float t0 = Time.realtimeSinceStartup;
            yield return new WaitForSeconds(5f);
            var near = new HashSet<string>(WorldLocations()
                    .Where(
                        l => StateDump.Flat(
                            l.transform.position - j.transform.position)
                            <= 2000f).Select(Name));
            var switched = Since(FarEvents.Switches, t0)
                .Where(e => near.Contains(e.Location)).ToList();
            var flickers = Flickers(_since);
            var all = Since(FarEvents.Switches, _since);
            string kinds = string.Join(
                ", ",
                switched.Select(e => e.Location + " " + e.Kind));
            r.Info =
                $"since fe-reset {all.Count(e => e.Kind == "enter")} location "
                    + $"enters, {all.Count(e => e.Kind == "leave")} leaves; " +
                     $"by the client ({string.Join(", ", near)}) in the last 5 "
                    + "s: "
                    + $"[{kinds}]; flickers [{string.Join(", ", flickers)}]";
            if (switched.Count > 0)
            {
                r.Fail(
                    "a location by the client switched while it stood still: "
                        + r.Info);
            }
            else if (flickers.Count > 0)
            {
                r.Fail("locations left and entered again at once: " + r.Info);
            }
        }

        private static IEnumerator HostIn(string[] a, Result r)
        {
            if (_shared == null)
            {
                r.Fail("no location by the client (fe-jump to-client first)");
                yield break;
            }

            float until = Time.realtimeSinceStartup + 8f;
            List<FarEvents.Entry> es = null;
            while (Time.realtimeSinceStartup < until)
            {
                es = Since(FarEvents.Log, _hostArrived, _shared);
                if (es.Any(e => e.Kind == "enter"))
                {
                    break;
                }

                yield return new WaitForSeconds(0.25f);
            }

            r.Info = $"{_shared}: its events since we came [{Show(es)}]";
            if (!es.Any(
                e => e.Kind == "enter"
                    && e.Part == ScriptedEvents.Part.PlayerOnly))
            {
                r.Fail(
                    "our player's part of its enter events didn't run: "
                        + r.Info);
            }
            else if (es.Any(e => e.Part != ScriptedEvents.Part.PlayerOnly))
            {
                r.Fail("its events ran again for the world: " + r.Info);
            }
        }

        private static IEnumerator Stay(string[] a, Result r)
        {
            var l = WorldLocations().FirstOrDefault(x => Name(x) == _shared);
            if (l == null)
            {
                r.Fail("no shared location");
                yield break;
            }

            yield return new WaitForSeconds(2f);
            var es = Since(FarEvents.Log, _hostArrived, _shared);
            r.Info =
                $"{_shared}: entered {l.entered}, its events since we came "
                    + $"[{Show(es)}]";
            if (!l.entered)
            {
                r.Fail("left while our player is in it: " + r.Info);
            }
            else if (es.Any(e => e.Kind == "exit"))
            {
                r.Fail(
                    "its exit events ran while our player is in it: " + r.Info);
            }
        }

        private static IEnumerator Left(string[] a, Result r)
        {
            var l = WorldLocations().FirstOrDefault(x => Name(x) == _shared);
            if (l == null)
            {
                r.Fail("no shared location");
                yield break;
            }

            float until = Time.realtimeSinceStartup + 15f;
            while (l.entered && Time.realtimeSinceStartup < until)
            {
                yield return new WaitForSeconds(0.25f);
            }

            var es = Since(FarEvents.Log, _hostArrived, _shared);
            r.Info =
                $"{_shared}: entered {l.entered}, its events since we came "
                    + $"[{Show(es)}]";
            if (l.entered)
            {
                r.Fail("still entered after everyone went: " + r.Info);
            }
            else if (!es.Any(
                e => e.Kind == "exit" && e.Part == ScriptedEvents.Part.All))
            {
                r.Fail("its exit events didn't run in full: " + r.Info);
            }
            else if (es.Count(e => e.Kind == "exit") != 1)
            {
                r.Fail("its exit events ran more than once: " + r.Info);
            }
        }

        private static IEnumerator GeneratorSkill(string[] a, Result r)
        {
            var j = Joiner();
            if (j == null)
            {
                r.Fail("no joiner");
                yield break;
            }

            var gen = FuelSync.Generators.Where(g => g != null && !g.isSaw)
                .OrderBy(
                    g => StateDump.Flat(
                        g.transform.position
                            - j.transform.position)).FirstOrDefault();
            if (gen == null)
            {
                r.Info = "no generator in this world";
                yield break;
            }

            bool clientNearer = StateDump.Flat(
                gen.transform.position - j.transform.position)
                < StateDump.Flat(
                    gen.transform.position
                        - Player.Instance.transform.position);
            float fuel = gen.fuel;
            gen.drainFuel();
            float used = fuel - gen.fuel;
            gen.fuel = fuel;
            // the low-power state as it was
            gen.drainFuel();
            gen.fuel = fuel;
            Characters instance = Characters.Instance;
            bool? skill = null;
            if (instance != null)
            {
                skill = instance.Electrician(j.Owner);
            }

            string skillText = "not known";
            if (skill.HasValue)
            {
                skillText = skill.Value.ToString();
            }

            r.Info =
                $"{GeneratorDrainSkillPatch.Last}; used {used:0.###} (the "
                    + $"client nearer: {clientNearer}, its electrician skill "
                    + $"{skillText})";
            if (clientNearer && skill.HasValue
                && GeneratorDrainSkillPatch.Last != null
                && !GeneratorDrainSkillPatch.Last.Contains(j.OwnerName))
            {
                r.Fail("drained by our skill: " + r.Info);
            }
        }

        private static IEnumerator Loading(string[] a, Result r)
        {
            int refreshes =
                SharedCullingPatch.LoadingRefreshes - _refreshesAtReset;
            r.Info =
                $"{refreshes} culling refreshes round the others while our "
                    + "player was taken to a place, since fe-reset";
            if (refreshes == 0)
            {
                r.Fail(
                    "the others' areas weren't refreshed while we loaded: "
                        + r.Info);
            }

            yield break;
        }
    }
}
