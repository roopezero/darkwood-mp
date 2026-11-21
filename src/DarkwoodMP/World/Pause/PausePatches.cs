using System.Collections;
using HarmonyLib;

namespace DarkwoodMP.World.Pause
{
    // The game's pause and slow motion, which CoopPause changes in co-op.
    // With no session they run as the game's.

    [HarmonyPatch(typeof(Core), nameof(Core.pause))]
    static class CorePause
    {
        static bool Prefix()
        {
            CoopSession coop = Coop.Current;
            if (coop == null)
            {
                return true;
            }

            return coop.Pause.Pause();
        }
    }

    [HarmonyPatch(typeof(Core), nameof(Core.setTimeScale), typeof(float))]
    static class CoreSetTimeScale
    {
        static void Prefix(ref float _timeScale)
        {
            CoopSession coop = Coop.Current;
            if (coop == null)
            {
                return;
            }

            _timeScale = coop.Pause.TimeScaleFor(_timeScale);
        }
    }

    [HarmonyPatch(
        typeof(Controller), nameof(Controller.alterTimeScale), typeof(float),
        typeof(float))]
    static class ControllerAlterTimeScale
    {
        static bool Prefix(ref IEnumerator __result)
        {
            CoopSession coop = Coop.Current;
            if (coop == null)
            {
                return true;
            }

            return coop.Pause.AlterTimeScale(ref __result);
        }
    }
}
