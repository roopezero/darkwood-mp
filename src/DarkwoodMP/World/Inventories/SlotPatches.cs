using DarkwoodMP.World.Workbenches;
using HarmonyLib;

namespace DarkwoodMP.World.Inventories
{
    /// <summary>
    /// The world's own durability changes in a watched storage (rot, drain,
    /// recharge) aren't the player's: SlotShare.BeforeDrift and Drift.
    /// </summary>
    [HarmonyPatch(typeof(Inventory), nameof(Inventory.refreshItems))]
    static class RefreshItems
    {
        static void Prefix(Inventory __instance, out string[] __state)
        {
            __state = null;
            CoopSession coop = Coop.Current;
            if (coop == null)
            {
                return;
            }

            __state = coop.Slots.BeforeDrift(__instance);
        }

        static void Postfix(Inventory __instance, string[] __state)
        {
            CoopSession coop = Coop.Current;
            if (coop == null)
            {
                return;
            }

            coop.Slots.Drift(__instance, __state);
        }
    }

    /// <summary>
    /// An upgrade raises the workbench's level: its storage's new row (also
    /// without co-op) and the level for the others (SlotShare.Crafted).
    /// </summary>
    [HarmonyPatch(typeof(CraftingRecipes), nameof(CraftingRecipes.doCraft))]
    static class DoCraft
    {
        static void Prefix(out int __state)
        {
            __state = SlotShare.LevelNow();
        }

        static void Postfix(int __state)
        {
            SlotShare.Crafted(__state);
        }
    }

    /// <summary>
    /// A dropped item picked up with a click: SlotShare.PickingUp and
    /// PickedUp.
    /// </summary>
    [HarmonyPatch(typeof(Item), nameof(Item.getDroppedItem))]
    static class GetDroppedItem
    {
        private static readonly AccessTools.FieldRef<Item, Inventory> Inv =
            AccessTools.FieldRefAccess<Item, Inventory>("inventory");

        static void Prefix(Item __instance, out Inventory __state)
        {
            __state = null;
            CoopSession coop = Coop.Current;
            if (coop == null)
            {
                return;
            }

            __state = coop.Slots.PickingUp(Inv(__instance));
        }

        static void Postfix(Inventory __state)
        {
            CoopSession coop = Coop.Current;
            if (coop == null)
            {
                return;
            }

            coop.Slots.PickedUp(__state);
        }
    }

    /// <summary>
    /// A workbench's storage was drawn: Sort &amp; Stack under it
    /// (SlotShare.Shown), then the window drawn smaller if it would run off
    /// the screen (WorkbenchStorage.FitToScreen). One postfix, so the button
    /// is always there before the window shrinks and shrinks with it; as two
    /// postfixes the shrink could run first and leave the button below the
    /// screen. Also without co-op.
    /// </summary>
    [HarmonyPatch(typeof(Inventory), nameof(Inventory.show))]
    static class InventoryShow
    {
        static void Postfix(Inventory __instance)
        {
            SlotShare.Shown(__instance);
            WorkbenchStorage.FitToScreen(__instance);
        }
    }
}
