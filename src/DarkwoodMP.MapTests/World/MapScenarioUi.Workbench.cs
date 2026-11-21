using DarkwoodMP.Menus;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DarkwoodMP.World.Inventories;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;

namespace DarkwoodMP.World
{
    /// <summary>The workbench's steps (ui-wb-*).</summary>
    public static partial class MapScenarioUi
    {
        // ---- the workbench --------------------------------------------------

        /// <summary>
        /// "ui-wb-open": open the workbench as the player does (Workbench.open)
        /// and leave it open.
        /// </summary>
        private static IEnumerator WbOpen(string[] a, Result r)
        {
            var wb = Bench();
            if (wb == null)
            {
                r.Fail("no workbench");
                yield break;
            }

            yield return Walk(wb.transform.position);
            if (P.openedItemInventory2 != wb.normalInventory)
            {
                AsPlayer(() => wb.open());
            }

            // A client asks the host for the storage as it opens it, and its
            // window takes the answer.
            float seconds = 1f;
            if (Net.Session.Instance != null && Net.Session.Instance.IsClient)
            {
                seconds = SlotRules.AskTimeout + 0.5f;
            }

            yield return new WaitForSeconds(seconds);
            var st = wb.normalInventory;
            if (P.openedItemInventory2 != st || !st.open)
            {
                r.Fail("the workbench's storage didn't open");
                yield break;
            }

            string drawn = Drawn(st),
                rec = Recipes(wb, out int met, out int total);
            var known = wb.workbenchInventory.slots.Where(
                s => !InvItemClass.isNull(s.invItem)
                    && s.invItem.baseClass != null
                    && s.invItem.baseClass.GetComponent<CraftingRecipes>()
                        != null)
                .Select(
                    s =>
                    {
                        var book = s.invItem.baseClass
                            .GetComponent<CraftingRecipes>();
                        var recipe = book.recipes[s.invItem.usedRecipe];
                        return $"{s.invItem.type}[{Needs(recipe)}]";
                    });

            r.Info =
                $"{MapScenarioWorld.Ref(st)} {st.slots.Count} slots, "
                    + $"{st.getAllItems().Count} filled; {met}/{total} recipes "
                    + $"makeable: {string.Join(" ", known)}";
            if (drawn != null)
            {
                r.Fail($"drawn wrong as it opened: {drawn}");
            }
            else if (rec != null)
            {
                r.Fail($"recipes drawn wrong as it opened: {rec}");
            }
        }

        /// <summary>"ui-wb-close": close it as the player does.</summary>
        private static IEnumerator WbClose(string[] a, Result r)
        {
            try
            {
                P.closeInventory();
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[map] ui-wb-close: {e.Message}");
            }

            yield return new WaitForSeconds(0.8f);
            var wb = Bench();
            if (wb != null && P.openedItemInventory2 == wb.normalInventory)
            {
                r.Fail("still open");
            }

            r.Info = "closed";
        }

        /// <summary>"ui-wb-set IDX TYPE AMOUNT [IDX TYPE AMOUNT...]
        /// [--at EPOCH]": change storage slots as our player, in our open
        /// window (at that epoch second, for both players at once).</summary>
        private static IEnumerator WbSet(string[] a, Result r)
        {
            var args = a.ToList();
            int at = args.IndexOf("--at");
            double epoch = 0;
            if (at >= 0)
            {
                epoch =
                    double.Parse(args[at + 1], CultureInfo.InvariantCulture);
                args.RemoveRange(at, 2);
            }

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

            while (epoch > 0 && MapScenarioSavage.Now < epoch)
            {
                yield return null;
            }

            var did = new List<string>();
            for (int k = 0; k + 2 < args.Count; k += 3)
            {
                int i = Index(st, args[k]);
                Put(
                    st, i, args[k + 1],
                    int.Parse(args[k + 2], CultureInfo.InvariantCulture));
                did.Add($"{i}={Slot(st.slots[i])}");
            }

            // Containers looks at the open storage four times a second
            yield return new WaitForSeconds(0.8f);
            r.Info = string.Join(", ", did);
        }

        /// <summary>"ui-wb-race EPOCH" (host and client at once): at that epoch
        /// second the host puts bandages x2 in slot -7 and the client wire x6
        /// in slot -8.</summary>
        private static IEnumerator WbRace(string[] a, Result r)
        {
            bool host =
                Net.Session.Instance == null || !Net.Session.Instance.IsClient;
            string[] args;
            if (host)
            {
                args = new[] { "-7", "bandages", "2" };
            }
            else
            {
                args = new[] { "-8", "wire", "6" };
            }

            yield return WbSet(
                args.Concat(new[] { "--at", a[0] }).ToArray(), r);
        }

        /// <summary>
        /// "ui-wb-burst IDX TYPE N": N changes to one slot 0.12 s apart (TYPE
        /// x1, x2 .. xN).
        /// </summary>
        private static IEnumerator WbBurst(string[] a, Result r)
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

            int i = Index(st, a[0]),
                n = int.Parse(a[2], CultureInfo.InvariantCulture);
            for (int k = 1; k <= n; k++)
            {
                Put(st, i, a[1], k);
                yield return new WaitForSecondsRealtime(0.12f);
            }

            yield return new WaitForSeconds(0.8f);
            r.Info = $"{i}: {a[1]} x1..x{n}";
        }

        /// <summary>
        /// "ui-wb-check [IDX TYPE AMOUNT...] [--makeable N]": with no input
        /// from us, the open storage comes to hold these (within 4 s), and then
        /// what its window has drawn matches its slots and the recipes' tint
        /// matches what can be made. --makeable N: at least N recipes can be
        /// made (the storage's contents count for them).
        /// </summary>
        private static IEnumerator WbCheck(string[] a, Result r)
        {
            var args = a.ToList();
            int mk = args.IndexOf("--makeable");
            int wantMakeable = -1;
            if (mk >= 0)
            {
                wantMakeable =
                    int.Parse(args[mk + 1], CultureInfo.InvariantCulture);
                args.RemoveRange(mk, 2);
            }

            var wb = Bench();
            Inventory st = null;
            if (wb != null)
            {
                st = wb.normalInventory;
            }

            if (st == null || P.openedItemInventory2 != st || !st.open)
            {
                r.Fail("the workbench isn't open here");
                yield break;
            }

            var want = new List<(int i, string type, int n)>();
            for (int k = 0; k + 2 < args.Count; k += 3)
            {
                want.Add(
                    (Index(st, args[k]), args[k + 1], int.Parse(
                        args[k + 2], CultureInfo.InvariantCulture)));
            }

            int redraws0 = Coop.Current.Ui.Redraws;
            float until = Time.realtimeSinceStartup + 4f;
            while (want.Any(w => !Matches(st.slots[w.i], w.type, w.n))
                && Time.realtimeSinceStartup < until)
            {
                yield return null;
            }

            // icons removed this frame are destroyed at its end
            yield return null;
            yield return null;
            string slots = string.Join(
                ", ", want.Select(w => $"{w.i}={Slot(st.slots[w.i])}"));
            var wrong = want.Where(w =>
                !Matches(st.slots[w.i], w.type, w.n)).Select(w =>
            {
                string expected;
                if (w.type == "-")
                {
                    expected = "empty";
                }
                else
                {
                    expected = $"{w.type}x{w.n}";
                }

                return $"slot {w.i} is {Slot(st.slots[w.i])}, not {expected}";
            }).ToList();

            string drawn = Drawn(st),
                rec = Recipes(wb, out int met, out int total);
            r.Info =
                $"{slots}; drawn ok={drawn == null}; {met}/{total} makeable, "
                    + $"tint ok={rec == null}; redraws "
                    + $"{Coop.Current.Ui.Redraws} (total)";
            if (wrong.Count > 0)
            {
                r.Fail(
                    $"{string.Join("; ", wrong)} (the other player's change "
                        + "didn't arrive)");
            }
            else if (drawn != null)
            {
                r.Fail($"the window shows something else: {drawn}");
            }
            else if (rec != null)
            {
                r.Fail($"the recipes are drawn stale: {rec}");
            }
            else if (wantMakeable >= 0 && met < wantMakeable)
            {
                r.Fail($"{r.Info}: only {met} makeable, wanted {wantMakeable}");
            }
        }
    }
}
