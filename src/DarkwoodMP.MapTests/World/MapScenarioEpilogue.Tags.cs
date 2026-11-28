using System.Collections;
using DarkwoodMP.Menus;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;

namespace DarkwoodMP.World
{
    public static partial class MapScenarioEpilogue
    {
        /// <summary>
        /// "epilogue-tags" (any): in the epilogue the other players' name
        /// tags are drawn (the game hides its HUD there, which once hid the
        /// tags all the way through). Waits up to 10 s for a cutscene or a
        /// black screen to pass.
        /// </summary>
        private static IEnumerator Tags(string[] a, Result r)
        {
            float until = Time.realtimeSinceStartup + 10f;
            while (UiState.WorldOverlaysHidden
                && Time.realtimeSinceStartup < until)
            {
                yield return new WaitForSecondsRealtime(0.5f);
            }

            bool inEpilogue = P != null && P.inEpilogue;
            string why = UiState.Reason;
            if (why == null)
            {
                why = "none";
            }

            r.Info = $"in the epilogue {inEpilogue}, tags hidden by {why}";
            if (!inEpilogue)
            {
                r.Fail($"not in the epilogue: {r.Info}");
            }
            else if (UiState.WorldOverlaysHidden)
            {
                r.Fail($"name tags hidden: {r.Info}");
            }
        }
    }
}
