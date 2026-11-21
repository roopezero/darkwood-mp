using System.Collections.Generic;
using DarkwoodMP.Combat;
using DarkwoodMP.Net;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;

namespace DarkwoodMP.World.Inventories
{
    /// <summary>
    /// Containers: the windows of containers that go while open.
    /// </summary>
    internal sealed partial class Containers
    {
        // ---- windows of containers that go while open -----------------------

        /// <summary>
        /// Closes the window of every container in this object (or under it)
        /// that is open here. Called before the mod destroys a container or its
        /// Inventory: the window (Inventory.thisUI) lives under the UI, not
        /// under the container, and Inventory has no OnDestroy, so a container
        /// destroyed while open leaves its window on screen, and
        /// Player.closeInventory skips it from then on (openedItemInventory is
        /// a destroyed object).
        /// </summary>
        internal static void CloseIfOpen(GameObject go)
        {
            if (go == null)
            {
                return;
            }

            foreach (var inv in go.GetComponentsInChildren<Inventory>(true))
            {
                if (inv == null || !inv.open)
                {
                    continue;
                }

                try
                {
                    inv.hide();
                }
                catch (System.Exception e)
                {
                    Plugin.Log.LogWarning(
                        $"Couldn't close {inv.name}'s window before removing "
                            + $"it: {e.Message}");
                }
            }
        }

        private static readonly System.Reflection.FieldInfo SawField =
            AccessTools.Field(typeof(Inventory), "sawComponent");

        /// <summary>
        /// The safety net for any other way a container goes while open (the
        /// game's own code, or a path of ours that CloseIfOpen doesn't cover):
        /// when the player's open container is a destroyed object, its window
        /// is removed and the inventory closed as Inventory.hide would have
        /// done. The destroyed Inventory's fields can still be read; only its
        /// Unity side is gone, so hide() itself can't run.
        /// </summary>
        internal static void DropOrphanedWindows()
        {
            var p = Player.Instance;
            if (p == null)
            {
                return;
            }

            if (Orphaned(p.openedItemInventory))
            {
                var inv = p.openedItemInventory;
                p.openedItemInventory = null;
                DropWindow(p, inv);
            }

            if (Orphaned(p.openedItemInventory2))
            {
                var inv = p.openedItemInventory2;
                p.openedItemInventory2 = null;
                DropWindow(p, inv);
            }
        }

        private static bool Orphaned(Inventory inv) =>
            !ReferenceEquals(inv, null) && inv == null;

        private static void DropWindow(Player p, Inventory inv)
        {
            string text = " (none left)";
            if (inv.thisUI != null)
            {
                text = "";
            }

            Plugin.Log.LogInfo(
                "[containers] an open container was destroyed with its window "
                    + $"still up: removed the window{text}");
            try
            {
                if (inv.slots != null)
                {
                    foreach (var slot in inv.slots)
                    {
                        try
                        {
                            if (slot != null)
                            {
                                slot.despawn();
                            }
                        }
                        catch (System.Exception)
                        {
                        }
                    }
                }

                if (inv.thisUI != null)
                {
                    Object.Destroy(inv.thisUI);
                }

                if (inv.traderMessage != null)
                {
                    Object.Destroy(inv.traderMessage.gameObject);
                }

                inv.open = false;
                UI instance = Singleton<UI>.Instance;
                if (instance != null)
                {
                    InventorySelectionPrompt inventorySelectionPrompt =
                        instance.InventorySelectionPrompt;
                    if (inventorySelectionPrompt != null)
                    {
                        inventorySelectionPrompt.Hide();
                    }
                }

                if (inv.invType == Inventory.InvType.shop)
                {
                    p.talkedToNPC = null;
                }

                var ic = Singleton<InventoryController>.Instance;
                if (ic != null)
                {
                    if (inv.isWorkbench)
                    {
                        ic.RepairMode = false;
                        ic.UpgradeMode = false;
                        ic.inWorkbench = false;
                    }

                    System.Reflection.FieldInfo sawField = SawField;
                    object saw = null;
                    if (sawField != null)
                    {
                        saw = sawField.GetValue(inv);
                    }

                    if (!ReferenceEquals(saw, null))
                    {
                        ic.inSaw = false;
                    }
                }
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning(
                    "Couldn't remove a destroyed container's window: "
                        + $"{e.Message}");
            }
        }
    }
}
