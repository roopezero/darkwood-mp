using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DarkwoodMP.Combat;
using UnityEngine;
using Object = UnityEngine.Object;
using static DarkwoodMP.World.MapScenario;

namespace DarkwoodMP.World
{
    /// <summary>Steps for night attackers and several hideouts.</summary>
    public static partial class MapScenarioMulti
    {
        // ---- 5. night attackers, players in different hideouts --------------

        /// <summary>
        /// "mt-hideout-go": walk to the nearest hideout other than the one
        /// we're in (or nearest to the centre).
        /// </summary>
        private static IEnumerator HideoutGo(string[] a, Result r)
        {
            var wg = Singleton<WorldGenerator>.Instance;
            var here = NightSpawns.BigLocationAt(Centre);
            Location to = null;
            if (wg != null)
            {
                to = wg.locations.Where(
                    l => l != null && l.playerBase && !l.isSubLocation
                        && l != here)
                    .OrderBy(l => StateDump.Flat(l.transform.position
                            - Centre)).FirstOrDefault();
            }

            if (to == null)
            {
                string hideoutName = "none";
                if (here != null)
                {
                    hideoutName = here.name;
                }

                r.Fail($"no other hideout (the centre's is {hideoutName})");
                yield break;
            }

            yield return Walk(to.transform.position, 60f);
            yield return new WaitForSeconds(1.5f);
            var now = NightSpawns.BigLocationAt(P.transform.position);
            string spot = Fmt(P.transform.position);
            string location = "no location";
            if (now != null)
            {
                location = now.name;
            }

            r.Info =
                $"at {spot}: {location} (went to {to.name}, "
                    + $"{StateDump.Flat(to.transform.position - Centre):0} "
                    + "from the centre)";
            if (now != to)
            {
                r.Fail($"{r.Info}: not in it");
            }
        }

        /// <summary>"mt-night N" (host): the night's attacker spawn
        /// (CharacterSpawner.spawnNightChar) N times. Each occupied hideout
        /// should get one each time (until its budget is spent), aimed at the
        /// host's player if they are in it, else at a joiner there; players
        /// sharing a hideout get no extra ones. Then they're removed
        /// again.</summary>
        private static IEnumerator Night(string[] a, Result r)
        {
            if (!Plugin.Instance.Host.Session.IsHost)
            {
                r.Fail("night attackers are the host's");
                yield break;
            }

            int n = 3;
            if (a.Length > 0)
            {
                n = int.Parse(a[0]);
            }

            var spawner = Singleton<CharacterSpawner>.Instance;
            var call = HarmonyLib.AccessTools.Method(
                typeof(CharacterSpawner), "spawnNightChar");
            P.whereAmI.checkWhereAmI();
            // Who is where, as the host sees it.
            var where =
                new List<string> { $"host: {Name(P.whereAmI.bigLocation)}" };
            var hideouts = new HashSet<string>();
            if (P.whereAmI.bigLocation != null
                && P.whereAmI.bigLocation.playerBase)
            {
                hideouts.Add(P.whereAmI.bigLocation.name);
            }

            foreach (var px in NetPlayerProxy.All.Values.Where(
                x => x != null && x.alive))
            {
                bool home =
                    NightSpawns.AtHideout(px.transform.position, out var loc);
                string note = "";
                if (!home)
                {
                    note = " (no hideout)";
                }

                where.Add($"{px.OwnerName}: {Name(loc)}{note}");
                if (home)
                {
                    hideouts.Add(loc.name);
                }
            }

            var sc = Singleton<NightScenarios>.Instance;
            if (sc.currentScenario == null)
            {
                sc.setCurrentScenario();
            }

            string budget;
            if (sc.currentScenario == null)
            {
                budget = "no scenario";
            }
            else
            {
                budget = sc.currentScenario.name + ": " + string.Join(
                    ", ",
                    sc.currentScenario.characters.Where(x => x != null).Select(
                        x => $"{x.characterName} x{x.amount}"));
            }

            int max;
            if (sc.currentScenario == null)
            {
                max = 0;
            }
            else
            {
                max = sc.currentScenario.characters.Where(
                    x => x != null
                        && !string.IsNullOrEmpty(x.characterName)).Sum(
                            x => Math.Max(0, x.amount));
            }

            int from = NightSpawns.SentLog.Count;
            bool was = spawner.spawnNocturnalCharacters;
            // Put back in a finally: left on, night attackers would spawn in
            // the daytime of every later step.
            try
            {
                spawner.spawnNocturnalCharacters = true;
                for (int i = 0; i < n; i++)
                {
                    call.Invoke(spawner, null);
                    yield return new WaitForSeconds(0.3f);
                }
            }
            finally
            {
                spawner.spawnNocturnalCharacters = was;
            }

            var sent = NightSpawns.SentLog.Skip(from).ToList();
            var per = sent.GroupBy(s => s.Hideout).ToDictionary(
                g => g.Key, g => g.ToList());
            r.Info = $"{string.Join("; ", where)} | {budget} | " +
                     string.Join("; ", hideouts.Select(h =>
                     {
                         int count = 0;
                         if (per.TryGetValue(h, out var l))
                         {
                             count = l.Count;
                         }

                         IEnumerable<string> spots;
                         if (per.TryGetValue(h, out var l2))
                         {
                             spots = l2.Select(s => s.At);
                         }
                         else
                         {
                             spots = new string[0];
                         }

                         return $"{h}: {count} ({string.Join(",", spots)})";
                     }));

            int want = Math.Min(n, max);
            foreach (var h in hideouts)
            {
                int got = 0;
                if (per.TryGetValue(h, out var l))
                {
                    got = l.Count;
                }

                if (got != want)
                {
                    r.Fail(
                        $"{r.Info}: {h} got {got} attackers from {n} spawns, "
                            + $"wanted {want}");
                }
            }

            foreach (var h in per.Keys)
            {
                if (!hideouts.Contains(h))
                {
                    r.Fail($"{r.Info}: attackers sent to {h}, where nobody is");
                }
            }

            if (hideouts.Count < 2)
            {
                r.Fail($"{r.Info}: wanted players in two hideouts");
            }

            // They'd only fight the players: away with them (the clients'
            // copies go with the host's).
            foreach (var s in sent)
            {
                if (s.C != null)
                {
                    Object.Destroy(s.C.gameObject);
                }
            }

            yield return new WaitForSeconds(1f);
        }

        private static string Name(Location l)
        {
            if (l != null)
            {
                string hideoutNote = "";
                if (l.playerBase)
                {
                    hideoutNote = " (hideout)";
                }

                return l.name + hideoutNote;
            }

            return "none";
        }
    }
}
