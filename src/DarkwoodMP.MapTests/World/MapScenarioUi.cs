using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;
using Object = UnityEngine.Object;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Map test steps for windows kept up to date while open (World.UiRefresh).
    /// One player has a window open and gives it no input; the other changes
    /// what it shows; the viewer's check step reads what the window has drawn
    /// (the slots' icon objects, their sprites and amount labels, the recipes'
    /// red tint, the saw's fuel label) and compares it with the object's data.
    /// The list is tools/tests/map_scenario_ui.py.
    ///
    /// Storage slots are named by index; a negative one counts from the end (-1
    /// is the last slot), which is empty in the test save. A type "-" is an
    /// empty slot.
    /// </summary>
    public static partial class MapScenarioUi
    {
        private static Player P => Player.Instance;

        public static void Register()
        {
            Add("ui-wb-open", WbOpen, timeout: 30f);
            Add("ui-wb-close", WbClose);
            Add("ui-wb-set", WbSet, timeout: 40f);
            Add("ui-wb-race", WbRace, timeout: 40f);
            Add("ui-wb-burst", WbBurst);
            Add("ui-wb-check", WbCheck);
            Add("ui-saw-open", SawOpen, timeout: 30f);
            Add("ui-saw-check", SawCheck);
            Add("ui-saw-close", SawClose);
            Add("ui-trader-show", TraderShow, timeout: 30f);
            Add("ui-trader-check", TraderCheck);
            Add("ui-trader-hide", TraderHide);
            Add("ui-pause", Pause, timeout: 20f);
            Add("ui-refresh", (a, r) =>
            {
                UiRefresh ui = Coop.Current.Ui;
                ui.Enabled = a.Length == 0 || a[0] != "off";
                string text = "off";
                if (ui.Enabled)
                {
                    text = "on";
                }

                r.Info = $"UiRefresh {text}";
                return Nothing();
            });
        }

        private static IEnumerator Nothing()
        {
            yield break;
        }

        private static Workbench Bench() => Nearest(
            Object.FindObjectsOfType<Workbench>().Where(
                w => w.normalInventory != null),
            Centre);

        private static int Index(Inventory inv, string s)
        {
            int i = int.Parse(s, CultureInfo.InvariantCulture);
            if (i < 0)
            {
                return inv.slots.Count + i;
            }

            return i;
        }

        private static string Slot(InvSlot s)
        {
            if (InvItemClass.isNull(s.invItem))
            {
                return "-";
            }

            return $"{s.invItem.type}x{s.invItem.amount}";
        }

        private static bool Matches(InvSlot s, string type, int amount)
        {
            if (type == "-")
            {
                return InvItemClass.isNull(s.invItem);
            }

            return !InvItemClass.isNull(s.invItem) && s.invItem.type == type
                && s.invItem.amount == amount;
        }

        /// <summary>
        /// Make slot i hold this as our player (a client's change goes to the
        /// host as ContainerSlots).
        /// </summary>
        private static void Put(Inventory inv, int i, string type, int amount)
        {
            var s = inv.slots[i];
            s.inventory = inv;
            AsPlayer(() =>
            {
                s.removeItem();
                if (type != "-")
                {
                    s.createItem(type, amount);
                }
            });

            // Things made or removed from nothing change what the player has to
            // craft with, as a drop or a craft does; the game redraws the
            // recipes after those (Player.refreshRecipes), so this does too.
            P.refreshRecipes();
        }

        // ---- what a window has drawn ----------------------------------------

        /// <summary>
        /// Differences between what an open inventory's window has drawn and
        /// what its slots hold: each filled slot has a live icon (UIInvItem)
        /// with its item's sprite and amount, its frame is the filled or empty
        /// one, and there are no icons left over from things no longer there.
        /// Null when they agree.
        /// </summary>
        internal static string Drawn(Inventory inv)
        {
            if (inv == null)
            {
                return "no inventory";
            }

            if (!inv.open || inv.thisUI == null)
            {
                return $"{inv.name}'s window isn't open";
            }

            var bad = new List<string>();
            int filled = 0;
            for (int i = 0; i < inv.slots.Count; i++)
            {
                var s = inv.slots[i];
                bool full = !InvItemClass.isNull(s.invItem);
                if (s.UIInvSlot != null && s.UIInvSlot.sprite != null
                    && s.UIInvSlot.sprite.CurrentSprite != null && !s.inHotbar)
                {
                    string frame = s.UIInvSlot.sprite.CurrentSprite.name;
                    string wrongFrame = "fullInv";
                    if (full)
                    {
                        wrongFrame = "emptyInv";
                    }

                    if (frame == wrongFrame)
                    {
                        bad.Add($"slot {i} ({Slot(s)}): frame {frame}");
                    }
                }

                if (!full)
                {
                    continue;
                }

                filled++;
                var ui = s.invItem.UIInvItem;
                if (ui == null || !ui.gameObject.activeInHierarchy)
                {
                    bad.Add($"slot {i} ({Slot(s)}): no icon drawn");
                    continue;
                }

                string want = null;
                if (s.invItem.baseClass != null)
                {
                    want = s.invItem.baseClass.iconType;
                }

                string got = null;
                if (ui.sprite != null && ui.sprite.CurrentSprite != null)
                {
                    got = ui.sprite.CurrentSprite.name;
                }

                if (want != null && got != want)
                {
                    bad.Add($"slot {i} ({Slot(s)}): icon {got}, not {want}");
                }

                if (ui.amount != null
                    && ui.amount.text != s.invItem.amount.ToString())
                {
                    bad.Add(
                        $"slot {i} ({Slot(s)}): amount drawn {ui.amount.text}");
                }
            }

            int icons =
                inv.thisUI.GetComponentsInChildren<UIInvItem>(false).Length;
            if (icons != filled)
            {
                bad.Add($"{icons} icons drawn for {filled} filled slots");
            }

            if (bad.Count == 0)
            {
                return null;
            }

            return string.Join("; ", bad);
        }

        private static readonly AccessTools.FieldRef<UIInvItem, Color> Red =
            AccessTools.FieldRefAccess<UIInvItem, Color>("redColor");

        /// <summary>
        /// Differences between the workbench recipes' tint (red: can't be made)
        /// and whether they can be made now, by what the player carries and the
        /// storage holds (worked out here, not from the game's cached
        /// CraftingRequirement.met). Null when they agree. met: how many can be
        /// made.
        /// </summary>
        internal static string Recipes(Workbench wb, out int met, out int total)
        {
            met = 0;
            total = 0;
            var inv = wb.workbenchInventory;
            if (inv == null || !inv.open)
            {
                return "the recipes aren't open";
            }

            var bad = new List<string>();
            var have = P.Inventory;
            foreach (var s in inv.slots)
            {
                if (InvItemClass.isNull(s.invItem)
                    || s.invItem.baseClass == null)
                {
                    continue;
                }

                var cr = s.invItem.baseClass.GetComponent<CraftingRecipes>();
                if (cr == null || s.invItem.usedRecipe < 0
                    || s.invItem.usedRecipe >= cr.recipes.Count)
                {
                    continue;
                }

                total++;
                var recipe = cr.recipes[s.invItem.usedRecipe];
                bool can = !cr.reachedMaxNumberOfTimesCrafted();
                foreach (var q in recipe.requirements)
                {
                    if (q.item == null)
                    {
                        continue;
                    }

                    if (q.amount > 0
                        && have.haveItemAmountInPlayer(
                            q.item.type,
                            includeAdditionalInventory: true) < q.amount)
                    {
                        can = false;
                    }

                    if (q.durabilityAmount > 0f
                        && have.haveItemDurabilityInPlayer(
                            q.item.type, includeAdditionalInventory: true,
                            relativeToMaxDurability: true) < q.durabilityAmount)
                    {
                        can = false;
                    }
                }

                if (can)
                {
                    met++;
                }

                var ui = s.invItem.UIInvItem;
                if (ui == null || ui.sprite == null)
                {
                    bad.Add($"{s.invItem.type}: not drawn");
                    continue;
                }

                var c = ui.sprite.color;
                var red = Red(ui);
                bool drawnRed = Mathf.Abs(c.r - red.r) < 0.02f
                    && Mathf.Abs(c.g - red.g) < 0.02f
                    && Mathf.Abs(c.b - red.b) < 0.02f;
                if (drawnRed == can)
                {
                    string text = "makeable";
                    if (drawnRed)
                    {
                        text = "red";
                    }

                    string makeable = "not";
                    if (can)
                    {
                        makeable = "makeable";
                    }

                    bad.Add(
                        $"{s.invItem.type} drawn {text}, is {makeable} "
                            + $"({Needs(recipe)})");
                }
            }

            if (bad.Count == 0)
            {
                return null;
            }

            return string.Join("; ", bad);
        }

        private static string Needs(CraftingRecipes.Recipe recipe) =>
            string.Join(",",
                recipe.requirements.Where(q => q.item != null).Select(q =>
            {
                if (q.amount > 0)
                {
                    var have = P.Inventory.haveItemAmountInPlayer(
                        q.item.type,
                        includeAdditionalInventory: true);
                    return $"{q.item.type}x{q.amount} have " + $"{have:0}";
                }

                return $"{q.item.type} dur {q.durabilityAmount:0.##}";
            }));

        // ---- the pause menu over open windows (R08, R10) --------------------

        /// <summary>"ui-pause": with the workbench open, open the pause menu:
        /// the HUD (the backpack, the hotbar, the storage's window, the bars,
        /// the clock) is hidden and so are world overlays (UiState); close it:
        /// the same HUD is back as before and the storage's window draws its
        /// slots.</summary>
        private static IEnumerator Pause(string[] a, Result r)
        {
            var wb = Bench();
            Inventory st = null;
            if (wb != null)
            {
                st = wb.normalInventory;
            }

            if (st == null || P.openedItemInventory2 != st)
            {
                r.Fail("the workbench isn't open here");
                yield break;
            }

            string before = PauseHud.Shown();
            if (!before.Contains("container2"))
            {
                r.Fail($"the storage's window isn't shown before: {before}");
                yield break;
            }

            Core.showHidePauseMenu();
            yield return new WaitForSecondsRealtime(1f);
            string under = PauseHud.Shown();
            bool hidden = UiState.WorldOverlaysHidden;
            string why = UiState.Reason;
            if (Core.mainMenu)
            {
                Core.showHidePauseMenu();
            }

            yield return new WaitForSecondsRealtime(1f);
            string after = PauseHud.Shown();
            r.Info =
                $"HUD before: {before}; under the menu: {under} (overlays "
                    + $"hidden {hidden}, {why}); after: {after}";
            if (under != "none")
            {
                r.Fail($"HUD shown under the pause menu: {under}");
            }
            else if (!hidden)
            {
                r.Fail("world overlays not hidden under the pause menu");
            }
            else if (after != before)
            {
                r.Fail($"HUD after the menu: {after}, before: {before}");
            }
            else if (UiState.WorldOverlaysHidden)
            {
                r.Fail(
                    "world overlays still hidden after the menu "
                        + $"({UiState.Reason})");
            }
            else if (Drawn(st) is string bad)
            {
                r.Fail($"the storage's window after the menu: {bad}");
            }
        }
    }
}
