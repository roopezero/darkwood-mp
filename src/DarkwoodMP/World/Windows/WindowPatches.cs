using HarmonyLib;
using UnityEngine;

namespace DarkwoodMP.World.Windows
{
    // Each prefix counts one patched world call deeper and keeps the window
    // as it was (Windows.Before); each postfix hands the call to the
    // session's Windows, which sends it if it changed the window.

    [HarmonyPatch(typeof(Window), nameof(Window.barricade))]
    static class WindowBarricade
    {
        static void Prefix(Window __instance, out WindowState __state)
        {
            __state = Windows.Before(__instance);
        }

        static void Postfix(
            Window __instance,
            int destHealth,
            bool byPlayer,
            WindowState __state)
        {
            var coop = Coop.Current;
            if (coop != null)
            {
                var call = new ActionValues { X = destHealth, F1 = byPlayer };
                coop.Windows.Barricaded(__instance, __state, call);
            }
        }
    }

    [HarmonyPatch(typeof(Window), nameof(Window.destroyBarricade))]
    static class WindowDestroyBarricade
    {
        static void Prefix(Window __instance, out WindowState __state)
        {
            __state = Windows.Before(__instance);
        }

        static void Postfix(Window __instance, bool silent, WindowState __state)
        {
            var coop = Coop.Current;
            if (coop != null)
            {
                coop.Windows.BarricadeBroken(__instance, __state, silent);
            }
        }
    }

    [HarmonyPatch(
        typeof(Window), nameof(Window.getHit),
        new[] { typeof(int), typeof(Transform), typeof(bool) })]
    static class WindowGetHit
    {
        /// <summary>
        /// On a client, a hit the host makes too (an explosion, fire) isn't
        /// made here: the host's WindowHit is.
        /// </summary>
        static bool Prefix(Window __instance, out WindowState __state)
        {
            __state = Windows.Before(__instance);
            return !BreakSync.HostsHit();
        }

        static void Postfix(
            Window __instance,
            int damage,
            bool normalHit,
            WindowState __state)
        {
            var coop = Coop.Current;
            if (coop != null)
            {
                var hit = new ActionValues { X = damage, F1 = normalHit };
                coop.Windows.Hit(__instance, __state, hit);
            }
        }
    }
}
