using UnityEngine;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Redraws the open window that shows a world object after another player's change to it was applied here, so
    /// the window is up to date without the viewer closing it, hovering or clicking.
    ///
    /// A slot's icon already follows a remote change: InvSlot.createItem and removeItem draw and remove it while
    /// its inventory is open. What the game draws once and keeps until the player does something is the rest:
    /// - the workbench's recipes, red or not by what the player carries plus the storage (CraftingRequirement.met,
    ///   set by CraftingRecipes.refresh), and its upgrade button: Player.refreshRecipes, which the game calls after
    ///   each of the player's own moves, redraws those, the backpack and the hotbar;
    /// - the saw's "Fuel: N" label and whether its Convert button can be pressed: Saw.refresh;
    /// - the fuel bar of the generator or saw under the cursor: EnemyHealthBar.show with onlyRefresh.
    ///
    /// These only redraw. They run with WorldActions.Replaying set, and none of them changes a slot, so nothing goes
    /// back out from them. Nothing is done when the object's window isn't open.
    /// </summary>
    internal static class UiRefresh
    {
        /// <summary>Counted for the map test (ui-* steps): how many times an open window was redrawn.</summary>
        internal static int Redraws;

        /// <summary>False only in the map test's "before" run (ui-refresh off): windows behave as they did without this.</summary>
        internal static bool Enabled = true;

        /// <summary>Whether the local player has this inventory's window open.</summary>
        internal static bool Shown(Inventory inv)
        {
            var p = Player.Instance;
            if (inv == null || p == null) return false;
            return inv.open || p.openedItemInventory == inv || p.openedItemInventory2 == inv;
        }

        /// <summary>A remote change to these slots was applied: redraw what of its window the slots don't draw.</summary>
        internal static void Changed(Inventory inv)
        {
            if (!Enabled || !Shown(inv)) return;
            Run(() =>
            {
                Player.Instance.refreshRecipes();
                var saw = inv.GetComponent<Saw>();
                if (saw != null && inv.open) saw.refresh();
            }, inv.name);
        }

        /// <summary>A remote change to a generator's or the saw's fuel was applied.</summary>
        internal static void FuelChanged(Component c)
        {
            if (c == null || !Enabled) return;
            var saw = c as Saw;
            var inv = saw != null ? c.GetComponent<Inventory>() : null;
            bool sawOpen = inv != null && inv.open;
            var ui = Singleton<UI>.Instance;
            var bar = ui != null ? ui.enemyHealthBar : null;
            bool barShown = bar != null && bar.currentObj == c.gameObject && bar.gameObject.activeInHierarchy;
            if (!sawOpen && !barShown) return;
            Run(() =>
            {
                if (sawOpen) saw.refresh();
                if (barShown) bar.show(c.gameObject, onlyRefresh: true);
            }, c.name);
        }

        private static void Run(System.Action redraw, string what)
        {
            bool was = WorldActions.Replaying;
            WorldActions.Replaying = true;
            try { redraw(); Redraws++; }
            catch (System.Exception e) { Plugin.Log.LogWarning($"[ui] couldn't redraw {what}: {e.Message}"); }
            finally { WorldActions.Replaying = was; }
        }
    }
}
