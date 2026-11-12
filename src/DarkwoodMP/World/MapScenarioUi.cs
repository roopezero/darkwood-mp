using System;
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
    /// Map test steps for windows kept up to date while open (World.UiRefresh). One player has a window open and
    /// gives it no input; the other changes what it shows; the viewer's check step reads what the window has drawn
    /// (the slots' icon objects, their sprites and amount labels, the recipes' red tint, the saw's fuel label) and
    /// compares it with the object's data. The list is tools/tests/map_scenario_ui.py.
    ///
    /// Storage slots are named by index; a negative one counts from the end (-1 is the last slot), which is empty
    /// in the test save. A type "-" is an empty slot.
    /// </summary>
    public static class MapScenarioUi
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
            Add("ui-refresh", (a, r) => { UiRefresh.Enabled = a.Length == 0 || a[0] != "off"; r.Info = $"UiRefresh {(UiRefresh.Enabled ? "on" : "off")}"; return Nothing(); });
        }

        private static IEnumerator Nothing() { yield break; }

        private static Workbench Bench() => Nearest(Object.FindObjectsOfType<Workbench>().Where(w => w.normalInventory != null), Centre);

        private static int Index(Inventory inv, string s)
        {
            int i = int.Parse(s, CultureInfo.InvariantCulture);
            return i < 0 ? inv.slots.Count + i : i;
        }

        private static string Slot(InvSlot s) => InvItemClass.isNull(s.invItem) ? "-" : $"{s.invItem.type}x{s.invItem.amount}";

        private static bool Matches(InvSlot s, string type, int amount) =>
            type == "-" ? InvItemClass.isNull(s.invItem) : !InvItemClass.isNull(s.invItem) && s.invItem.type == type && s.invItem.amount == amount;

        /// <summary>Make slot i hold this as our player (a client's change goes to the host as ContainerSlots).</summary>
        private static void Put(Inventory inv, int i, string type, int amount)
        {
            var s = inv.slots[i];
            s.inventory = inv;
            AsPlayer(() =>
            {
                s.removeItem();
                if (type != "-") s.createItem(type, amount);
            });
            // Things made or removed from nothing change what the player has to craft with, as a drop or a craft
            // does; the game redraws the recipes after those (Player.refreshRecipes), so this does too.
            P.refreshRecipes();
        }

        // ---- what a window has drawn -------------------------------------------------------------------------

        /// <summary>
        /// Differences between what an open inventory's window has drawn and what its slots hold: each filled slot
        /// has a live icon (UIInvItem) with its item's sprite and amount, its frame is the filled or empty one, and
        /// there are no icons left over from things no longer there. Null when they agree.
        /// </summary>
        internal static string Drawn(Inventory inv)
        {
            if (inv == null) return "no inventory";
            if (!inv.open || inv.thisUI == null) return $"{inv.name}'s window isn't open";
            var bad = new List<string>();
            int filled = 0;
            for (int i = 0; i < inv.slots.Count; i++)
            {
                var s = inv.slots[i];
                bool full = !InvItemClass.isNull(s.invItem);
                if (s.UIInvSlot != null && s.UIInvSlot.sprite != null && s.UIInvSlot.sprite.CurrentSprite != null && !s.inHotbar)
                {
                    string frame = s.UIInvSlot.sprite.CurrentSprite.name;
                    if (frame == (full ? "emptyInv" : "fullInv")) bad.Add($"slot {i} ({Slot(s)}): frame {frame}");
                }
                if (!full) continue;
                filled++;
                var ui = s.invItem.UIInvItem;
                if (ui == null || !ui.gameObject.activeInHierarchy) { bad.Add($"slot {i} ({Slot(s)}): no icon drawn"); continue; }
                string want = s.invItem.baseClass != null ? s.invItem.baseClass.iconType : null;
                string got = ui.sprite != null && ui.sprite.CurrentSprite != null ? ui.sprite.CurrentSprite.name : null;
                if (want != null && got != want) bad.Add($"slot {i} ({Slot(s)}): icon {got}, not {want}");
                if (ui.amount != null && ui.amount.text != s.invItem.amount.ToString()) bad.Add($"slot {i} ({Slot(s)}): amount drawn {ui.amount.text}");
            }
            int icons = inv.thisUI.GetComponentsInChildren<UIInvItem>(false).Length;
            if (icons != filled) bad.Add($"{icons} icons drawn for {filled} filled slots");
            return bad.Count == 0 ? null : string.Join("; ", bad);
        }

        private static readonly AccessTools.FieldRef<UIInvItem, Color> Red = AccessTools.FieldRefAccess<UIInvItem, Color>("redColor");

        /// <summary>
        /// Differences between the workbench recipes' tint (red: can't be made) and whether they can be made now, by
        /// what the player carries and the storage holds (worked out here, not from the game's cached
        /// CraftingRequirement.met). Null when they agree. met: how many can be made.
        /// </summary>
        internal static string Recipes(Workbench wb, out int met, out int total)
        {
            met = 0; total = 0;
            var inv = wb.workbenchInventory;
            if (inv == null || !inv.open) return "the recipes aren't open";
            var bad = new List<string>();
            var have = P.Inventory;
            foreach (var s in inv.slots)
            {
                if (InvItemClass.isNull(s.invItem) || s.invItem.baseClass == null) continue;
                var cr = s.invItem.baseClass.GetComponent<CraftingRecipes>();
                if (cr == null || s.invItem.usedRecipe < 0 || s.invItem.usedRecipe >= cr.recipes.Count) continue;
                total++;
                var recipe = cr.recipes[s.invItem.usedRecipe];
                bool can = !cr.reachedMaxNumberOfTimesCrafted();
                foreach (var q in recipe.requirements)
                {
                    if (q.item == null) continue;
                    if (q.amount > 0 && have.haveItemAmountInPlayer(q.item.type, includeAdditionalInventory: true) < q.amount) can = false;
                    if (q.durabilityAmount > 0f && have.haveItemDurabilityInPlayer(q.item.type, includeAdditionalInventory: true, relativeToMaxDurability: true) < q.durabilityAmount) can = false;
                }
                if (can) met++;
                var ui = s.invItem.UIInvItem;
                if (ui == null || ui.sprite == null) { bad.Add($"{s.invItem.type}: not drawn"); continue; }
                var c = ui.sprite.color; var red = Red(ui);
                bool drawnRed = Mathf.Abs(c.r - red.r) < 0.02f && Mathf.Abs(c.g - red.g) < 0.02f && Mathf.Abs(c.b - red.b) < 0.02f;
                if (drawnRed == can) bad.Add($"{s.invItem.type} drawn {(drawnRed ? "red" : "makeable")}, is {(can ? "makeable" : "not")} ({Needs(recipe)})");
            }
            return bad.Count == 0 ? null : string.Join("; ", bad);
        }

        private static string Needs(CraftingRecipes.Recipe recipe) =>
            string.Join(",", recipe.requirements.Where(q => q.item != null).Select(q => q.amount > 0
                ? $"{q.item.type}x{q.amount} have {P.Inventory.haveItemAmountInPlayer(q.item.type, includeAdditionalInventory: true):0}"
                : $"{q.item.type} dur {q.durabilityAmount:0.##}"));

        // ---- the workbench ---------------------------------------------------------------------------------------

        /// <summary>"ui-wb-open": open the workbench as the player does (Workbench.open) and leave it open.</summary>
        private static IEnumerator WbOpen(string[] a, Result r)
        {
            var wb = Bench();
            if (wb == null) { r.Fail("no workbench"); yield break; }
            yield return Walk(wb.transform.position);
            if (P.openedItemInventory2 != wb.normalInventory) AsPlayer(() => wb.open());
            // A client asks the host for the storage as it opens it, and its window takes the answer.
            yield return new WaitForSeconds(Net.Session.Instance != null && Net.Session.Instance.IsClient ? SlotShare.AskTimeout + 0.5f : 1f);
            var st = wb.normalInventory;
            if (P.openedItemInventory2 != st || !st.open) { r.Fail("the workbench's storage didn't open"); yield break; }
            string drawn = Drawn(st), rec = Recipes(wb, out int met, out int total);
            var known = wb.workbenchInventory.slots.Where(s => !InvItemClass.isNull(s.invItem) && s.invItem.baseClass != null && s.invItem.baseClass.GetComponent<CraftingRecipes>() != null)
                .Select(s => $"{s.invItem.type}[{Needs(s.invItem.baseClass.GetComponent<CraftingRecipes>().recipes[s.invItem.usedRecipe])}]");
            r.Info = $"{Ref(st)} {st.slots.Count} slots, {st.getAllItems().Count} filled; {met}/{total} recipes makeable: {string.Join(" ", known)}";
            if (drawn != null) r.Fail($"drawn wrong as it opened: {drawn}");
            else if (rec != null) r.Fail($"recipes drawn wrong as it opened: {rec}");
        }

        private static string Ref(Component c) => ObjRef.Of(c.gameObject).ToString();

        /// <summary>"ui-wb-close": close it as the player does.</summary>
        private static IEnumerator WbClose(string[] a, Result r)
        {
            try { P.closeInventory(); } catch (Exception e) { Plugin.Log.LogWarning($"[map] ui-wb-close: {e.Message}"); }
            yield return new WaitForSeconds(0.8f);
            var wb = Bench();
            if (wb != null && P.openedItemInventory2 == wb.normalInventory) r.Fail("still open");
            r.Info = "closed";
        }

        /// <summary>"ui-wb-set IDX TYPE AMOUNT [IDX TYPE AMOUNT...] [--at EPOCH]": change storage slots as our player,
        /// in our open window (at that epoch second, for both players at once).</summary>
        private static IEnumerator WbSet(string[] a, Result r)
        {
            var args = a.ToList();
            int at = args.IndexOf("--at");
            double epoch = 0;
            if (at >= 0) { epoch = double.Parse(args[at + 1], CultureInfo.InvariantCulture); args.RemoveRange(at, 2); }
            var wb = Bench();
            var st = wb != null ? wb.normalInventory : null;
            if (st == null || P.openedItemInventory2 != st) { r.Fail("the workbench isn't open here"); yield break; }
            while (epoch > 0 && DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0 < epoch) yield return null;
            var did = new List<string>();
            for (int k = 0; k + 2 < args.Count; k += 3)
            {
                int i = Index(st, args[k]);
                Put(st, i, args[k + 1], int.Parse(args[k + 2], CultureInfo.InvariantCulture));
                did.Add($"{i}={Slot(st.slots[i])}");
            }
            yield return new WaitForSeconds(0.8f); // Containers looks at the open storage four times a second
            r.Info = string.Join(", ", did);
        }

        /// <summary>"ui-wb-race EPOCH" (host and client at once): at that epoch second the host puts bandages x2 in
        /// slot -7 and the client wire x6 in slot -8.</summary>
        private static IEnumerator WbRace(string[] a, Result r)
        {
            bool host = Net.Session.Instance == null || !Net.Session.Instance.IsClient;
            var args = host ? new[] { "-7", "bandages", "2" } : new[] { "-8", "wire", "6" };
            yield return WbSet(args.Concat(new[] { "--at", a[0] }).ToArray(), r);
        }

        /// <summary>"ui-wb-burst IDX TYPE N": N changes to one slot 0.12 s apart (TYPE x1, x2 .. xN).</summary>
        private static IEnumerator WbBurst(string[] a, Result r)
        {
            var wb = Bench();
            var st = wb != null ? wb.normalInventory : null;
            if (st == null || P.openedItemInventory2 != st) { r.Fail("the workbench isn't open here"); yield break; }
            int i = Index(st, a[0]), n = int.Parse(a[2], CultureInfo.InvariantCulture);
            for (int k = 1; k <= n; k++)
            {
                Put(st, i, a[1], k);
                yield return new WaitForSecondsRealtime(0.12f);
            }
            yield return new WaitForSeconds(0.8f);
            r.Info = $"{i}: {a[1]} x1..x{n}";
        }

        /// <summary>
        /// "ui-wb-check [IDX TYPE AMOUNT...] [--makeable N]": with no input from us, the open storage comes to hold
        /// these (within 4 s), and then what its window has drawn matches its slots and the recipes' tint matches what
        /// can be made. --makeable N: at least N recipes can be made (the storage's contents count for them).
        /// </summary>
        private static IEnumerator WbCheck(string[] a, Result r)
        {
            var args = a.ToList();
            int mk = args.IndexOf("--makeable");
            int wantMakeable = -1;
            if (mk >= 0) { wantMakeable = int.Parse(args[mk + 1], CultureInfo.InvariantCulture); args.RemoveRange(mk, 2); }
            var wb = Bench();
            var st = wb != null ? wb.normalInventory : null;
            if (st == null || P.openedItemInventory2 != st || !st.open) { r.Fail("the workbench isn't open here"); yield break; }
            var want = new List<(int i, string type, int n)>();
            for (int k = 0; k + 2 < args.Count; k += 3) want.Add((Index(st, args[k]), args[k + 1], int.Parse(args[k + 2], CultureInfo.InvariantCulture)));
            int redraws0 = UiRefresh.Redraws;
            float until = Time.realtimeSinceStartup + 4f;
            while (want.Any(w => !Matches(st.slots[w.i], w.type, w.n)) && Time.realtimeSinceStartup < until) yield return null;
            yield return null; yield return null; // icons removed this frame are destroyed at its end
            string slots = string.Join(", ", want.Select(w => $"{w.i}={Slot(st.slots[w.i])}"));
            var wrong = want.Where(w => !Matches(st.slots[w.i], w.type, w.n)).Select(w => $"slot {w.i} is {Slot(st.slots[w.i])}, not {(w.type == "-" ? "empty" : $"{w.type}x{w.n}")}").ToList();
            string drawn = Drawn(st), rec = Recipes(wb, out int met, out int total);
            r.Info = $"{slots}; drawn ok={drawn == null}; {met}/{total} makeable, tint ok={rec == null}; redraws {UiRefresh.Redraws} (total)";
            if (wrong.Count > 0) r.Fail($"{string.Join("; ", wrong)} (the other player's change didn't arrive)");
            else if (drawn != null) r.Fail($"the window shows something else: {drawn}");
            else if (rec != null) r.Fail($"the recipes are drawn stale: {rec}");
            else if (wantMakeable >= 0 && met < wantMakeable) r.Fail($"{r.Info}: only {met} makeable, wanted {wantMakeable}");
        }

        // ---- the saw ------------------------------------------------------------------------------------------

        private static Saw _saw;

        /// <summary>"ui-saw-open NAME": open the saw's window as the player does (Item.openInventory) and leave it open.</summary>
        private static IEnumerator SawOpen(string[] a, Result r)
        {
            var saw = MapScenarioWorld.Find<Saw>(a[0]);
            if (saw == null) { r.Fail($"no saw {a[0]}"); yield break; }
            var item = saw.GetComponent<Item>();
            var inv = saw.GetComponent<Inventory>();
            if (item == null || inv == null) { r.Fail($"{saw.name} has no Item or Inventory"); yield break; }
            yield return Walk(saw.transform.position);
            AsPlayer(() => item.openInventory());
            yield return new WaitForSeconds(1.5f);
            if (!inv.open) { r.Fail($"{saw.name}'s window didn't open"); yield break; }
            _saw = saw;
            r.Info = $"{saw.name} {Ref(saw)} fuel {saw.fuel:0.0}, label '{Label(inv)}'";
        }

        private static string Label(Inventory inv) => inv.thisLabel != null && inv.thisLabel.rightText != null ? inv.thisLabel.rightText.text : null;

        /// <summary>"ui-saw-check [MINFUEL]": with no input from us, the open saw's "Fuel: N" label shows its fuel
        /// (within 4 s), which is at least MINFUEL, and its Convert button is enabled exactly when it can cut a log.</summary>
        private static IEnumerator SawCheck(string[] a, Result r)
        {
            var saw = _saw;
            var inv = saw != null ? saw.GetComponent<Inventory>() : null;
            if (inv == null || !inv.open) { r.Fail("the saw's window isn't open here"); yield break; }
            float min = a.Length > 0 ? float.Parse(a[0], CultureInfo.InvariantCulture) : 0f;
            string Want() => Language.Get("Fuel", "UI") + ": " + saw.fuel;
            float until = Time.realtimeSinceStartup + 4f;
            while ((saw.fuel < min || Label(inv) != Want()) && Time.realtimeSinceStartup < until) yield return null;
            var btn = saw.convertFuelBtn != null ? saw.convertFuelBtn.GetComponent<global::Button>() : null;
            bool enabled = btn != null && btn.background != null && btn.background.activeSelf;
            bool can = saw.canConvert();
            r.Info = $"fuel {saw.fuel:0.0}, label '{Label(inv)}', convert enabled={enabled} can={can}";
            if (saw.fuel < min) r.Fail($"{r.Info}: the fuel didn't arrive (wanted {min:0})");
            else if (Label(inv) != Want()) r.Fail($"{r.Info}: the label is stale (should be '{Want()}')");
            else if (btn != null && enabled != can) r.Fail($"{r.Info}: the Convert button is drawn stale");
        }

        private static IEnumerator SawClose(string[] a, Result r)
        {
            try { P.closeInventory(); } catch (Exception e) { Plugin.Log.LogWarning($"[map] ui-saw-close: {e.Message}"); }
            yield return new WaitForSeconds(0.8f);
            var inv = _saw != null ? _saw.GetComponent<Inventory>() : null;
            if (inv != null && inv.open) r.Fail("still open");
            r.Info = "closed";
        }

        // ---- the trader ----------------------------------------------------------------------------------------

        private static NPC _trader;
        private static int _traderMark;

        /// <summary>"ui-trader-show": stand at the trader with his stock's window shown, as the trade window shows it
        /// (DialogueWindow: npc's Inventory.show, Player.inShop).</summary>
        private static IEnumerator TraderShow(string[] a, Result r)
        {
            var npc = MapScenarioWorld.Trader();
            if (npc == null) { r.Fail("no trader here"); yield break; }
            yield return Walk(npc.transform.position, 150f);
            P.inShop = true;
            P.talkedToNPC = npc;
            npc.inventory.show();
            _trader = npc;
            yield return new WaitForSeconds(1f);
            _traderMark = MapScenarioIssues.LogMark();
            string drawn = Drawn(npc.inventory);
            r.Info = $"{npc.name}: {npc.inventory.slots.Count} slots, {npc.inventory.getAllItems().Count} items shown";
            if (drawn != null) r.Fail($"drawn wrong as shown: {drawn}");
        }

        /// <summary>"ui-trader-check": the host's new stock reached the shown window (still open) and it draws it.</summary>
        private static IEnumerator TraderCheck(string[] a, Result r)
        {
            var npc = _trader;
            if (npc == null || npc.inventory == null) { r.Fail("no trader shown"); yield break; }
            float until = Time.realtimeSinceStartup + 6f;
            string line = null;
            while (line == null && Time.realtimeSinceStartup < until)
            {
                line = MapScenarioIssues.LogLines().Skip(_traderMark).FirstOrDefault(l => l.Contains("has the host's stock"));
                if (line == null) yield return new WaitForSecondsRealtime(0.5f);
            }
            yield return null; yield return null;
            string drawn = Drawn(npc.inventory);
            r.Info = $"{npc.name}: {npc.inventory.getAllItems().Count} items, open={npc.inventory.open}; {line}";
            if (line == null) r.Fail("no new stock from the host");
            else if (!npc.inventory.open) r.Fail($"{r.Info}: the new stock closed his window");
            else if (drawn != null) r.Fail($"the window shows something else: {drawn}");
        }

        private static IEnumerator TraderHide(string[] a, Result r)
        {
            if (_trader != null && _trader.inventory != null && _trader.inventory.open) _trader.inventory.hide();
            P.inShop = false;
            P.talkedToNPC = null;
            _trader = null;
            yield return new WaitForSeconds(0.5f);
            r.Info = "hidden";
        }
    }
}
