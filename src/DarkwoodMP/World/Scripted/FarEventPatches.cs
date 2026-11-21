using System;
using HarmonyLib;

namespace DarkwoodMP.World.Scripted
{
    /// <summary>
    /// A location's enter events run in the part FarEvents gives them
    /// (FarEvents.EnterEvents); ScriptedEvents.Current goes back after.
    /// </summary>
    [HarmonyPatch(typeof(Location), "checkEnterEvents")]
    static class LocationCheckEnterEvents
    {
        static void Prefix(
            Location __instance, out (bool, ScriptedEvents.Part) __state)
        {
            __state = (false, ScriptedEvents.Current);
            CoopSession coop = Coop.Current;
            if (coop != null)
            {
                __state.Item1 = coop.FarEvents.EnterEvents(__instance);
            }
        }

        static Exception Finalizer(
            Exception __exception, (bool, ScriptedEvents.Part) __state)
        {
            if (__state.Item1)
            {
                ScriptedEvents.Current = __state.Item2;
            }

            return __exception;
        }
    }

    /// <summary>
    /// A location's exit events run in the part FarEvents gives them, and a
    /// joiner's may keep its characters (FarEvents.ExitEvents); both go back
    /// after.
    /// </summary>
    [HarmonyPatch(typeof(Location), "checkExitEvents")]
    static class LocationCheckExitEvents
    {
        static void Prefix(
            Location __instance, out (bool, ScriptedEvents.Part, bool) __state)
        {
            __state = (false, ScriptedEvents.Current, false);
            CoopSession coop = Coop.Current;
            if (coop == null)
            {
                return;
            }

            __state.Item3 = coop.FarEvents.KeepCharacters;
            __state.Item1 = coop.FarEvents.ExitEvents(__instance);
        }

        static Exception Finalizer(
            Exception __exception, (bool, ScriptedEvents.Part, bool) __state)
        {
            if (__state.Item1)
            {
                ScriptedEvents.Current = __state.Item2;
            }

            CoopSession coop = Coop.Current;
            if (coop != null)
            {
                coop.FarEvents.ExitEventsEnded(__state.Item3);
            }

            return __exception;
        }
    }

    /// <summary>
    /// A joiner's exit from a location doesn't take away its trader, porter or
    /// wolf while another player is by it (FarEvents.KeepCharacters).
    /// </summary>
    [HarmonyPatch(typeof(Location))]
    static class LocationDespawn
    {
        [HarmonyPrefix, HarmonyPatch(nameof(Location.despawnTrader))]
        static bool Trader() => Despawns();

        [HarmonyPrefix, HarmonyPatch(nameof(Location.despawnPorter))]
        static bool Porter() => Despawns();

        [HarmonyPrefix, HarmonyPatch(nameof(Location.despawnWolf))]
        static bool Wolf() => Despawns();

        static bool Despawns()
        {
            CoopSession coop = Coop.Current;
            return coop == null || !coop.FarEvents.KeepCharacters;
        }
    }

    /// <summary>
    /// For the map test: when a world location really switches on or off
    /// (culling entered or left it).
    /// </summary>
    [HarmonyPatch(typeof(Location))]
    static class LocationEnterLeave
    {
        [HarmonyPrefix, HarmonyPatch(nameof(Location.enter))]
        static void Enter(Location __instance, bool force)
        {
            CoopSession coop = Coop.Current;
            if (coop != null)
            {
                coop.FarEvents.Entering(__instance, force);
            }
        }

        [HarmonyPrefix, HarmonyPatch(nameof(Location.leave))]
        static void Leave(Location __instance)
        {
            CoopSession coop = Coop.Current;
            if (coop != null)
            {
                coop.FarEvents.Leaving(__instance);
            }
        }
    }
}
