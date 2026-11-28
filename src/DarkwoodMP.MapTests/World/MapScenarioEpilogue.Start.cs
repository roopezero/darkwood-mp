using System.Collections;
using System.IO;
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
            Add("epilogue-offered", Offered, timeout: 30f);
            Add("epilogue-tree", Tree, timeout: 30f);
        }

        /// <summary>
        /// "epilogue-offered [DIR]" (host): the game was started at the
        /// epilogue, and the Multiplayer window shows "Start the epilogue",
        /// which may be pressed now; with DIR, a screenshot of the window
        /// DIR/epilogue-start-panel.png.
        /// </summary>
        private static IEnumerator Offered(string[] a, Result r)
        {
            var start = Plugin.Instance.Host.EpilogueStart;
            Verdict may = start.MayStart();
            r.Info =
                $"shown {start.Shown}, may start: "
                    + $"{Refusals.Text(may)} (slot "
                    + $"{Core.currentProfile.id}, the Tree "
                    + $"{SaveBook.TreeAt(Core.currentProfile.id)})";
            if (!start.Shown)
            {
                r.Fail("the button isn't shown");
            }

            if (may.Refused)
            {
                r.Fail("it may not start: " + Refusals.Text(may));
            }

            if (a.Length == 0)
            {
                yield break;
            }

            var panel = Plugin.Instance.Host.Panel;
            panel.SetPanel(true);
            yield return new WaitForSecondsRealtime(1.5f);
            Directory.CreateDirectory(a[0]);
            string path =
                System.IO.Path.Combine(a[0], "epilogue-start-panel.png");
            ScreenCapture.CaptureScreenshot(path);
            yield return new WaitForSecondsRealtime(1f);
            Plugin.Log.LogInfo($"[shots] {path}");
            panel.SetPanel(false);
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
