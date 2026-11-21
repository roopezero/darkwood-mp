using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using HarmonyLib;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Steps of the two-game map test for the bugs reported on GitHub, one or
    /// more steps each (see MapScenario; the order they run in is
    /// tools/tests/map_scenario_issues.py).
    ///
    /// Things these steps add to the test area are found again by where they
    /// are, in both games: the station's egg and infection patch are the ones
    /// nearest the centre, the extra ones the nearest to a fixed offset from it
    /// (<see cref="Spot"/>), far enough from the stations not to be mixed up
    /// with them.
    /// </summary>
    public static partial class MapScenarioIssues
    {
        // Where the extra things go, from the centre (the stations are within
        // ~500 of it; the dump reaches 2000).
        private static readonly Dictionary<string, Vector3> Spots =
            new Dictionary<string, Vector3>
            {
                ["egg:near"] = new Vector3(0f, 0f, -900f),
                // outside what the client has loaded around it: culled there
                ["egg:far"] = new Vector3(2800f, 0f, 0f),
                ["infection:client"] = new Vector3(-900f, 0f, 0f),
                ["infection:keep"] = new Vector3(900f, 0f, 0f),
            };

        private static Vector3 Spot(string which)
        {
            if (which == "station" || !Spots.TryGetValue(which, out var off))
            {
                return Centre;
            }

            return Centre + off;
        }

        public static void Register()
        {
            // #1 loading screen while joining
            MapScenario.Add("join-log", JoinLog, timeout: 15f);
            // #13 a joiner placed off the others, not on top of them
            MapScenario.Add("join-apart", JoinApart, timeout: 20f);
            // #2 dead enemy's sounds, #3 re-equip after join (and an attack
            // with it)
            MapScenario.Add("hold", Hold);
            MapScenario.Add("check-equip", CheckEquip);
            MapScenario.Add("spawn-dog", SpawnDog);
            MapScenario.Add("attack-dog", AttackDog, timeout: 25f);
            MapScenario.Add("dog-hurt", DogHurt);
            MapScenario.Add("kill-dog", KillDog);
            MapScenario.Add("dead-dog", DeadDog, timeout: 20f);
            MapScenario.Add("search-corpse", SearchCorpse, timeout: 20f);
            MapScenario.Add("hidden-silent", HiddenSilent, timeout: 15f);
            // #4 workbench storage
            MapScenario.Add("wb-fill", WbFill);
            MapScenario.Add("wb-use", WbUse, timeout: 30f);
            MapScenario.Add("wb-host", WbHost);
            // #5 larva eggs
            MapScenario.Add("issue-setup", Setup);
            MapScenario.Add("egg-break", EggBreak, timeout: 30f);
            MapScenario.Add("egg-check", EggCheck, timeout: 15f);
            // "egg-cull WHICH on|off": that egg made inactive here (on) or
            // active again, as the game culls what's far off.
            MapScenario.Add("egg-cull", (a, r) =>
            {
                var e = Egg(a[0]);
                if (e == null)
                {
                    r.Fail("no egg");
                }
                else
                {
                    e.gameObject.SetActive(a.Length > 1 && a[1] == "off");
                    r.Info =
                        $"{a[0]} egg "
                            + $"{ObjRef.Of(e.gameObject)} "
                            + $"active {e.gameObject.activeSelf}";
                }

                return Nothing();
            });

            // "flags": the story flags here (to tell which one differs between
            // the games).
            MapScenario.Add("flags", (a, r) =>
            {
                var dict =
                    AccessTools
                        .FieldRefAccess<Flags, Dictionary<string, Flags.Flag>>(
                            "flagsDict")(Singleton<Flags>.Instance);
                r.Info = string.Join(",",
                    dict.Values.Where(f => f != null).Select(f =>
                {
                    string bit = "=0";
                    if (f.isTrue)
                    {
                        bit = "=1";
                    }

                    return f.name + bit;
                }).OrderBy(x => x, StringComparer.Ordinal));

                return Nothing();
            });

            // #6 infection
            MapScenario.Add("burn-infection", BurnInfection, timeout: 15f);
            MapScenario.Add("swing-infection", SwingInfection, timeout: 25f);
            MapScenario.Add("infection-check", InfectionCheck, timeout: 15f);
            // Where the patches are, noted in each game before any is cleared
            // (a cleared one is removed).
            MapScenario.Add("infection-note", (a, r) =>
            {
                foreach (var w in new[]
                {
                    "station",
                    "infection:client",
                    "infection:keep"
                })
                {
                    var i = Patch(w);
                    if (i != null)
                    {
                        _noted[w] = i.transform.position;
                    }
                }

                r.Info = string.Join(
                    "; ", _noted.Select(kv => $"{kv.Key} {Fmt(kv.Value)}"));
                return Nothing();
            });

            StateDump.AddSection("infection", ctx =>
            {
                foreach (var inf in StateDump.All<Infection>())
                {
                    if (inf == null || inf.disappearing
                        || !ctx.Near(inf.transform.position))
                    {
                        continue; // a cleared one is gone
                    }

                    ctx.Add(inf.gameObject);
                }
            });
        }

        // ---- helpers --------------------------------------------------------

        // Where this test's part of the log starts: the game keeps running
        // between test items (tools/run-test.sh), and an earlier item's lines
        // (the scripted peers' refused slot changes in host-authority) aren't
        // this test's.
        private static int _logStart;

        /// <summary>
        /// Test control's reset, before each test item: later LogLines start
        /// here.
        /// </summary>
        internal static void MarkLogStart()
        {
            _logStart = 0;
            try
            {
                _logStart = Math.Max(0, AllLogLines().Count - 1);
            }
            catch
            {
            }
        }

        /// <summary>
        /// This game's BepInEx log since the last test reset, as written so far
        /// (the disk log is flushed every second or two).
        /// </summary>
        internal static List<string> LogLines()
        {
            var all = AllLogLines();
            // A log shorter than the mark is a new one (the game was started
            // again): all of it is this test's.
            if (_logStart > 0 && _logStart < all.Count)
            {
                return all.GetRange(_logStart, all.Count - _logStart);
            }

            return all;
        }

        private static List<string> AllLogLines()
        {
            // BepInEx writes the file from a buffer it flushes every 2 s:
            // without this, a line logged just before a step was often missing
            // (join-waited found no "World ready" after a quick join).
            foreach (var l in BepInEx.Logging.Logger.Listeners)
            {
                if (l is BepInEx.Logging.DiskLogListener disk)
                {
                    try
                    {
                        lock (disk.LogWriter)
                        {
                            disk.LogWriter.Flush();
                        }
                    }
                    catch
                    {
                    }
                }
            }

            string path =
                Path.Combine(BepInEx.Paths.BepInExRootPath, "LogOutput.log");
            using (var fs = new FileStream(
                path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete))
            {
                using (var sr = new StreamReader(fs))
                {
                    return sr.ReadToEnd().Split('\n')
                        .Select(l => l.TrimEnd('\r')).ToList();
                }
            }
        }

        /// <summary>
        /// Wait (up to secs) for a line matching each pattern in this game's
        /// log after line `from`; the ones not found.
        /// </summary>
        internal static IEnumerator WaitLog(
            int from, string[] patterns, float secs, List<string> missing)
        {
            float until = Time.realtimeSinceStartup + secs;
            while (true)
            {
                var lines = LogLines();
                missing.Clear();
                foreach (var pat in patterns)
                {
                    if (!lines.Skip(from).Any(l => Regex.IsMatch(l, pat)))
                    {
                        missing.Add(pat);
                    }
                }

                if (missing.Count == 0 || Time.realtimeSinceStartup > until)
                {
                    yield break;
                }

                yield return new WaitForSecondsRealtime(0.5f);
            }
        }

        internal static int LogMark()
        {
            try
            {
                return LogLines().Count - 1;
            }
            catch
            {
                return 0;
            }
        }

        private static IEnumerable<InvItemClass> Carried(Player p) =>
            p.Inventory.slots.Concat(p.Hotbar.slots)
                .Where(s => s != null && !InvItemClass.isNull(s.invItem))
                .Select(s => s.invItem);

        private static bool InBags(Player p, InvItemClass item) =>
            p.Inventory.slots.Concat(p.Hotbar.slots).Any(
                s => s != null && s.invItem == item);

        private static GameObject Place(string prefab, Vector3 at)
        {
            var go = Core.AddPrefab(
                prefab, MapScenarioEnemies.FreeSpot(at),
                Quaternion.Euler(90f, 0f, 0f), Core.ItemContainer,
                worldSpace: true);
            if (go != null)
            {
                Core.addToSaveable(go, isDynamic: true, assignID: true);
            }

            return go;
        }

        /// <summary>
        /// Things of a kind in the scene (culled ones too), nearest a spot.
        /// </summary>
        internal static T NearestAll<T>(Vector3 to, Func<T, bool> ok)
            where T : Component =>
            StateDump.All<T>().Where(ok)
                .OrderBy(t => StateDump.Flat(t.transform.position - to))
                .FirstOrDefault();

        private static Item Egg(string which)
        {
            string spot;
            if (which == "station")
            {
                spot = which;
            }
            else
            {
                spot = "egg:" + which;
            }

            return NearestAll<Item>(
                Spot(spot),
                i => i.gameObject.name.StartsWith(
                    "larva_egg", StringComparison.Ordinal));
        }

        private static Infection Patch(string which, bool any = false) =>
            NearestAll<Infection>(Spot(which), i => any || !i.disappearing);

        /// <summary>
        /// Swing what our player holds, as the game's attack animation does
        /// (Player.attack), facing `at`.
        /// </summary>
        private static readonly System.Reflection.MethodInfo AttackMethod =
            AccessTools.Method(
                typeof(Player), "attack", new[] { typeof(float) });
        private static void Swing(Player p, Vector3 at)
        {
            var d = at - p.transform.position;
            d.y = 0f;
            if (d.sqrMagnitude > 1f)
            {
                p.transform.rotation = Quaternion.Euler(
                    90f, Quaternion.LookRotation(d).eulerAngles.y, 0f);
            }

            AsPlayer(() => AttackMethod.Invoke(p, new object[] { 1f }));
        }

        private static string ItemName(InvItemClass i)
        {
            if (InvItemClass.isNull(i))
            {
                return "nothing";
            }

            return i.type;
        }
    }
}
