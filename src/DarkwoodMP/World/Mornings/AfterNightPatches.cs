using System.Collections.Generic;
using HarmonyLib;

namespace DarkwoodMP.World.Mornings
{
    /// <summary>
    /// A new dawn (startAfterNight): the host's morning isn't over yet
    /// (AfterNight.HostDawn). The after-night reward (the trader's
    /// reputation += 100 to 250) is given in each game whose player is in a
    /// hideout at dawn. It goes to the others as the reward for that day,
    /// which counts once (FlagsSync). The reward's part runs with no session
    /// too, as FlagsSync is there from the start.
    /// </summary>
    [HarmonyPatch(typeof(Controller), nameof(Controller.startAfterNight))]
    static class ControllerStartAfterNight
    {
        static void Prefix(out Dictionary<string, int> __state)
        {
            CoopSession coop = Coop.Current;
            if (coop != null)
            {
                coop.Mornings.HostDawn();
            }

            __state = null;
            var f = Singleton<Flags>.Instance;
            if (f == null || FlagsSync.Instance == null)
            {
                return;
            }

            __state = new Dictionary<string, int>();
            foreach (var n in f.npcStates)
            {
                if (n != null && n.name != null)
                {
                    __state[n.name] = n.reputation;
                }
            }
        }

        static void Postfix(
            Controller __instance, Dictionary<string, int> __state)
        {
            var f = Singleton<Flags>.Instance;
            if (__state == null || f == null)
            {
                return;
            }

            foreach (var n in f.npcStates)
            {
                if (n == null || n.name == null
                    || !__state.TryGetValue(n.name, out int was)
                    || n.reputation == was)
                {
                    continue;
                }

                FlagsSync.Instance.Absorb(n.name, n.reputation - was);
                FlagsSync.Instance.LocalReward(
                    n.name, __instance.day, n.reputation - was);
            }
        }
    }

    /// <summary>A joiner walking out of its hideout in the morning
    /// (Location.OnTriggerExit -> endAfterNight) tells the host
    /// (AfterNight.TriggerExited).</summary>
    [HarmonyPatch(
        typeof(Location), "OnTriggerExit",
        new[] { typeof(UnityEngine.Collider) })]
    static class LocationOnTriggerExit
    {
        static void Prefix(out bool __state)
        {
            var c = Singleton<Controller>.Instance;
            __state = c != null && c.isAfterNight;
        }

        static void Postfix(bool __state)
        {
            CoopSession coop = Coop.Current;
            if (coop == null)
            {
                return;
            }

            coop.Mornings.TriggerExited(__state);
        }
    }

    /// <summary>Our morning ended (AfterNight.Ended).</summary>
    [HarmonyPatch(typeof(Controller), nameof(Controller.endAfterNight))]
    static class ControllerEndAfterNight
    {
        static void Prefix(Controller __instance, out bool __state)
        {
            __state = __instance.isAfterNight;
        }

        static void Postfix(
            Controller __instance, bool __state, bool byKillingTrader)
        {
            CoopSession coop = Coop.Current;
            if (coop == null)
            {
                return;
            }

            coop.Mornings.Ended(__instance, __state, byKillingTrader);
        }
    }
}
