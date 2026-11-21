using System.Collections;
using HarmonyLib;

namespace DarkwoodMP.World.Spectating
{
    /// <summary>
    /// The permadeath screen, unless there are others to watch (see Spectator).
    /// </summary>
    [HarmonyPatch(typeof(UI), nameof(UI.PreparePermadeathVideo))]
    static class UIPreparePermadeathVideo
    {
        static bool Prefix(ref IEnumerator __result)
        {
            CoopSession coop = Coop.Current;
            if (coop == null || !coop.Spectator.TryBegin())
            {
                return true;
            }

            __result = World.ScriptedEvents.Nothing();
            return false;
        }
    }
}
