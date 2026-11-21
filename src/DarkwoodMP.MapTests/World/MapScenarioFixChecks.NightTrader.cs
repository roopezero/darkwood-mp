using System;
using System.Collections;
using System.Linq;
using DarkwoodMP.Combat;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;
using Object = UnityEngine.Object;

namespace DarkwoodMP.World
{
    /// <summary>Checks of the night trader killed.</summary>
    public static partial class MapScenarioFixChecks
    {
        // ---- 24: the night trader killed ------------------------------------

        /// <summary>
        /// "kill-trader" (client): our player kills the night trader; our own
        /// game plays the killer's dream.
        /// </summary>
        private static IEnumerator KillTrader(string[] a, Result r)
        {
            var t = Nearest(
                Object.FindObjectsOfType<Character>().Where(
                    c => c.isNightTrader && c.alive),
                Centre);
            if (t == null)
            {
                r.Fail("no living night trader here");
                yield break;
            }

            string what = $"{t.name} {ObjRef.Of(t.gameObject)}";
            int hits = 0;
            while (t != null && t.alive && hits < 30)
            {
                yield return Walk(t.transform.position, 90f);
                hits++;
                AsPlayer(
                    () => t.getHit(
                        80f, P.transform, false, true, true, true, false, false,
                        false));
                yield return new WaitForSeconds(0.4f);
            }

            yield return new WaitForSeconds(1f);
            var dreams = Singleton<Dreams>.Instance;
            bool dream = Core.EnteringDream
                || (dreams != null && dreams.currentTransition != null);
            r.Info =
                $"{what}: alive {(t != null && t.alive)} after {hits} hits; "
                    + $"our dream {dream}";
            if (t != null && t.alive)
            {
                r.Fail($"still alive: {r.Info}");
            }
            else if (!dream)
            {
                r.Fail($"the killer's own dream didn't play: {r.Info}");
            }
        }

        /// <summary>
        /// "trader-dream" (host): the night trader died here too, and our
        /// player didn't get the killer's dream.
        /// </summary>
        private static IEnumerator TraderDream(string[] a, Result r)
        {
            yield return new WaitForSeconds(2f);
            var alive = Object.FindObjectsOfType<Character>()
                .Where(
                    c => c.isNightTrader && c.alive
                        && StateDump.Flat(c.transform.position - Centre)
                            < 2000f).ToList();
            var dreams = Singleton<Dreams>.Instance;
            bool dream = Core.EnteringDream
                || (dreams != null && dreams.currentTransition != null)
                || P.endingSleep;
            bool logged = MapScenarioIssues.LogLines().Any(
                l => l.Contains("the night trader was killed in another game"));
            r.Info =
                $"living night traders here {alive.Count}; our dream {dream}; "
                    + $"skipped it {logged}";
            if (alive.Count > 0)
            {
                r.Fail($"the night trader is alive here: {r.Info}");
            }
            else if (dream)
            {
                r.Fail($"we got the killer's dream: {r.Info}");
            }
            else if (!logged)
            {
                r.Fail(
                    "the replayed death didn't go by NightTraderDreamPatch: "
                        + $"{r.Info}");
            }
        }
    }
}
