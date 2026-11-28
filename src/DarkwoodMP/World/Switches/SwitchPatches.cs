using HarmonyLib;
using Action = DarkwoodMP.World.WorldActions.Action;

namespace DarkwoodMP.World
{
    // The switch patches (SwitchSync). Each does nothing with no session:
    // playing alone is the game's own.

    /// <summary>An item switched on.</summary>
    [HarmonyPatch(typeof(Item), nameof(Item.turnOn))]
    static class TurnOn
    {
        static void Prefix()
        {
            var coop = Coop.Current;
            if (coop == null)
            {
                return;
            }

            coop.Switches.BeforeTurn();
        }

        static void Postfix(Item __instance)
        {
            var coop = Coop.Current;
            if (coop == null)
            {
                return;
            }

            coop.Switches.AfterTurn(__instance, Action.ItemOn);
        }
    }

    /// <summary>An item switched off.</summary>
    [HarmonyPatch(typeof(Item), nameof(Item.turnOff))]
    static class TurnOff
    {
        static void Prefix()
        {
            var coop = Coop.Current;
            if (coop == null)
            {
                return;
            }

            coop.Switches.BeforeTurn();
        }

        static void Postfix(Item __instance)
        {
            var coop = Coop.Current;
            if (coop == null)
            {
                return;
            }

            coop.Switches.AfterTurn(__instance, Action.ItemOff);
        }
    }

    /// <summary>
    /// An item's start and loop sounds started (from Item.turnOn): not
    /// again while they play (SwitchSync.StartsSounds).
    /// </summary>
    [HarmonyPatch(typeof(ItemSounds), nameof(ItemSounds.playStart))]
    static class ItemSoundsPlayStart
    {
        static bool Prefix(ItemSounds __instance)
        {
            var coop = Coop.Current;
            if (coop == null)
            {
                return true;
            }

            return coop.Switches.StartsSounds(__instance);
        }
    }
}
