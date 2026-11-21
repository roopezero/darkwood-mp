using HarmonyLib;

namespace DarkwoodMP.World.Hideout
{
    /// <summary>
    /// The porter takes a hideout's things to another
    /// (Location.transportAllItemsToCurrentHideout): once, on the host
    /// (Porter.Transport).
    /// </summary>
    [HarmonyPatch(
        typeof(Location), nameof(Location.transportAllItemsToCurrentHideout))]
    static class LocationTransportAllItems
    {
        static bool Prefix(Location __instance)
        {
            CoopSession coop = Coop.Current;
            if (coop == null)
            {
                return true;
            }

            return coop.Porter.Transport(__instance);
        }
    }

    /// <summary>
    /// The porter whistle (Location.spawnPorter): the host calls the porter, in
    /// every game (Porter.Whistled, then Porter.SendCalled).
    /// </summary>
    [HarmonyPatch(typeof(Location), nameof(Location.spawnPorter))]
    static class LocationSpawnPorter
    {
        static bool Prefix(Location __instance, out bool __state)
        {
            __state = false;
            CoopSession coop = Coop.Current;
            if (coop == null)
            {
                return true;
            }

            return coop.Porter.Whistled(__instance, out __state);
        }

        static void Postfix(Location __instance, bool __state)
        {
            CoopSession coop = Coop.Current;
            if (__state && coop != null)
            {
                coop.Porter.SendCalled(__instance);
            }
        }
    }

    /// <summary>
    /// Lighting an oven (Player.setExperienceMachine with doEnable): the host
    /// decides which goes out (Ovens.SetOven).
    /// </summary>
    [HarmonyPatch(
        typeof(Player), nameof(Player.setExperienceMachine),
        typeof(ExperienceMachine), typeof(bool))]
    static class PlayerSetExperienceMachine
    {
        static bool Prefix(
            Player __instance, ExperienceMachine machine, bool doEnable)
        {
            CoopSession coop = Coop.Current;
            if (coop == null)
            {
                return true;
            }

            return coop.Ovens.SetOven(__instance, machine, doEnable);
        }
    }
}
