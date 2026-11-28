using System.Collections;
using DarkwoodMP.World.Story;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Map test steps for a new game started at the epilogue
    /// (World.Story.EpilogueStart, tools/tests/map_scenario_epilogue_start.py).
    /// </summary>
    public static partial class MapScenarioEpilogue
    {
        private static void RegisterStart()
        {
            Add("epilogue-started", Started, timeout: 120f);
            Add("epilogue-tree", Tree, timeout: 30f);
        }

        /// <summary>
        /// "epilogue-started" (host): the game started at the epilogue went
        /// into its first part by itself once its world was ready (no step
        /// of ours ran), and it is the shared epilogue, which others join.
        /// </summary>
        private static IEnumerator Started(string[] a, Result r)
        {
            float until = Time.realtimeSinceStartup + 110f;
            while (Time.realtimeSinceStartup < until
                && !InPart(Chapters.EpilogueDream))
            {
                yield return new WaitForSeconds(0.5f);
            }

            yield return new WaitForSeconds(2f);
            Verdict may = Plugin.Instance.Host.EpilogueStart.MayStart();
            r.Info =
                $"{Where()}; may start again: {Refusals.Text(may)} (slot "
                    + $"{Core.currentProfile.id}, the Tree "
                    + $"{SaveBook.TreeAt(Core.currentProfile.id)})";
            if (!InPart(Chapters.EpilogueDream))
            {
                r.Fail($"not in the epilogue: {r.Info}");
            }
            else if (!Playing() || Coop.Current.Dreaming.Shared == null)
            {
                r.Fail($"the epilogue isn't shared: {r.Info}");
            }
        }

        /// <summary>
        /// "epilogue-tree yes|no" (any): whether the story flag of the
        /// Talking Tree burned is set here (the host's, shared).
        /// </summary>
        private static IEnumerator Tree(string[] a, Result r)
        {
            bool burned = Singleton<Flags>.Instance.isFlagTrue(
                EpilogueStartRules.TreeBurnedFlag);
            r.Info = $"{EpilogueStartRules.TreeBurnedFlag}: {burned}";
            bool wanted = a.Length > 0 && a[0] == "yes";
            if (burned != wanted)
            {
                r.Fail($"the flag is {burned}, expected {wanted}");
            }

            yield break;
        }
    }
}
