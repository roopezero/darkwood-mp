using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DarkwoodMP.World.Workbenches;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;
using Object = UnityEngine.Object;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Map test steps for the workbench storage's rows
    /// (World.WorkbenchStorage): an upgrade adds a row of six slots in both
    /// games, the open window draws it (every slot on the screen, none on top
    /// of another, Sort &amp; Stack under the last row), a client puts
    /// something in the new row, and the host's save and reload keep it. The
    /// list is tools/tests/map_scenario_wbrows.py; the window steps are
    /// MapScenarioUi's (ui-wb-open, ui-wb-set).
    /// </summary>
    public static class MapScenarioWbRows
    {
        private static Player P => Player.Instance;
        private static int _countBefore = -1;
        private static int _sceneBefore = -1;
        private static int Scene =>
            UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle;

        public static void Register()
        {
            Add("wb-rows-check", Check, timeout: 240f);
            Add("wb-upgrade", Upgrade, timeout: 30f);
            Add("wb-rows-reload", Reload, timeout: 240f);
        }

        private static Workbench Bench() => Nearest(
            Object.FindObjectsOfType<Workbench>().Where(
                w => w.normalInventory != null),
            Centre);

        private static int Level =>
            Singleton<Controller>.Instance.workbenchLevel;

        private static WorkbenchStorage Rows =>
            Plugin.Instance.Host.Workbenches;

        private static string Slot(InvSlot s)
        {
            if (InvItemClass.isNull(s.invItem))
            {
                return "-";
            }

            return $"{s.invItem.type}x{s.invItem.amount}";
        }

        /// <summary>
        /// "wb-rows-check [--level L] [--grown N] [--slot IDX TYPE AMOUNT]
        /// [--shot DIR]": the storage has the slot count of the level (waiting
        /// up to 10 s for it), the level is L, it has N more slots than at the
        /// last check without --grown (or wb-upgrade's start), slot IDX
        /// (negative from the end) holds that; open, its window is drawn right.
        /// With --after-reload, first waits for our world to have loaded again
        /// since wb-upgrade (a client follows the host's reload) and for the
        /// host's contents.
        /// </summary>
        private static IEnumerator Check(string[] a, Result r)
        {
            var args = a.ToList();
            string Opt(string name, int k = 1)
            {
                int i = args.IndexOf(name);
                if (i >= 0 && i + k < args.Count)
                {
                    return args[i + k];
                }

                return null;
            }

            int wantLevel = -1;
            if (Opt("--level") != null)
            {
                wantLevel = int.Parse(Opt("--level"));
            }

            int grown = -1;
            if (Opt("--grown") != null)
            {
                grown = int.Parse(Opt("--grown"));
            }

            if (args.Contains("--after-reload"))
            {
                float by = Time.realtimeSinceStartup + 220f;
                while (Time.realtimeSinceStartup < by
                    && (Scene == _sceneBefore || !WorldShare.WorldReady))
                {
                    yield return null;
                }

                if (Scene == _sceneBefore || !WorldShare.WorldReady)
                {
                    r.Fail("our world wasn't loaded again");
                    yield break;
                }

                // the host's contents of the storage (SlotShare.Update asks for
                // them)
                yield return new WaitForSeconds(5f);
            }

            var wb = Bench();
            if (wb == null)
            {
                r.Fail("no workbench");
                yield break;
            }

            var st = wb.normalInventory;
            float until = Time.realtimeSinceStartup + 10f;
            while (Time.realtimeSinceStartup < until
                && (st.slots.Count != Rows.Wanted(wb, Level)
                    || (wantLevel > 0 && Level != wantLevel)))
            {
                yield return null;
            }

            int want = Rows.Wanted(wb, Level);
            r.Info =
                $"{MapScenarioWorld.Ref(st)} level {Level}: {st.slots.Count} "
                    + $"slots (base {Rows.BaseSlots(wb)}, wanted "
                    + $"{want}), {st.maxColumns} columns, "
                    + $"{st.getAllItems().Count} filled";
            if (wantLevel > 0 && Level != wantLevel)
            {
                r.Fail($"level {Level}, not {wantLevel}: {r.Info}");
                yield break;
            }

            if (st.slots.Count != want)
            {
                r.Fail($"slot count isn't the level's: {r.Info}");
                yield break;
            }

            if (grown >= 0 && st.slots.Count - _countBefore != grown)
            {
                r.Fail(
                    $"grew by {st.slots.Count - _countBefore}, not {grown}: "
                        + $"{r.Info}");
                yield break;
            }

            if (grown < 0)
            {
                // what a later --grown counts from (in this game)
                _countBefore = st.slots.Count;
            }

            if (!args.Contains("--after-reload"))
            {
                _sceneBefore = Scene;
            }

            if (Opt("--slot") != null)
            {
                int i = int.Parse(Opt("--slot"));
                if (i < 0)
                {
                    i += st.slots.Count;
                }

                string type = Opt("--slot", 2);
                int amount = int.Parse(Opt("--slot", 3));
                var s = st.slots[i];
                bool ok;
                if (type == "-")
                {
                    ok = InvItemClass.isNull(s.invItem);
                }
                else
                {
                    ok = !InvItemClass.isNull(s.invItem)
                        && s.invItem.type == type && s.invItem.amount == amount;
                }

                r.Info += $"; slot {i}: {Slot(s)}";
                if (!ok)
                {
                    r.Fail(
                        $"slot {i} holds {Slot(s)}, not {type}x{amount}: "
                            + $"{r.Info}");
                    yield break;
                }
            }

            if (st.open)
            {
                string drawn = MapScenarioUi.Drawn(st);
                if (drawn == null)
                {
                    drawn = Layout(st);
                }

                if (drawn != null)
                {
                    r.Fail($"drawn wrong: {drawn}; {r.Info}");
                    yield break;
                }

                r.Info += "; drawn right";
            }

            string dir = Opt("--shot");
            if (dir != null)
            {
                Directory.CreateDirectory(dir);
                string text = "host";
                if (Net.Session.Instance != null
                    && Net.Session.Instance.IsClient)
                {
                    text = "client";
                }

                string path =
                    Path.Combine(dir, $"wb-rows-{text}-{st.slots.Count}.png");
                ScreenCapture.CaptureScreenshot(path);
                yield return new WaitForSecondsRealtime(1f);
                Plugin.Log.LogInfo($"[shots] {path}");
            }
        }

        /// <summary>
        /// The open storage's slots: each one drawn, in rows of maxColumns, no
        /// two on one spot, all of them and the Sort &amp; Stack button under
        /// the last row on the screen (Core.CamUI). Null if right.
        /// </summary>
        private static string Layout(Inventory st)
        {
            Camera cam = null;
            if (Core.CamUI != null)
            {
                cam = Core.CamUI.GetComponent<Camera>();
            }

            var at = new List<Vector3>();
            for (int i = 0; i < st.slots.Count; i++)
            {
                var ui = st.slots[i].UIInvSlot;
                if (ui == null || !ui.gameObject.activeInHierarchy)
                {
                    return $"slot {i} isn't drawn";
                }

                at.Add(ui.transform.position);
            }

            float distance = 60f;
            if (at.Count > 1)
            {
                distance = Vector3.Distance(at[0], at[1]);
            }

            float size = distance * 0.9f;
            for (int i = 0; i < at.Count; i++)
            {
                for (int j = i + 1; j < at.Count; j++)
                {
                    if (Vector3.Distance(at[i], at[j]) < size)
                    {
                        return $"slots {i} and {j} overlap ({at[i]}, {at[j]})";
                    }
                }
            }

            int cols = Mathf.Max(1, st.maxColumns);
            for (int i = cols; i < at.Count; i++)
            {
                if (Mathf.Abs(at[i].x - at[i - cols].x) > 1f
                    || at[i].z >= at[i - cols].z)
                {
                    return $"slot {i} isn't under slot {i - cols}";
                }
            }

            var button = st.thisUI.transform.Find(SlotShare.ButtonName);
            if (button != null && button.position.z >= at[at.Count - 1].z)
            {
                return $"Sort & Stack ({button.position}) isn't under the last "
                    + $"row ({at[at.Count - 1]})";
            }

            if (cam != null)
            {
                Vector3[] second;
                if (button != null)
                {
                    second = new[] { button.position };
                }
                else
                {
                    second = new Vector3[0];
                }

                foreach (var p in new[] { at[0], at[at.Count - 1] }.Concat(
                    second))
                {
                    var v = cam.WorldToViewportPoint(p);
                    if (v.x < 0f || v.x > 1f || v.y < 0f || v.y > 1f)
                    {
                        return $"off the screen at {p} (viewport "
                            + $"{v.x:0.00},{v.y:0.00})";
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// "wb-upgrade [max]" (at the open workbench): upgrade it as the game
        /// does (with max, again until there's no next level). The upgrade
        /// recipe's ingredients are given to our player first, then
        /// CraftingRecipes.doCraft runs (what the progress bar ends in), which
        /// raises Controller.workbenchLevel; the storage must grow by one row
        /// here at once.
        /// </summary>
        private static IEnumerator Upgrade(string[] a, Result r)
        {
            var wb = Bench();
            if (wb == null)
            {
                r.Fail("no workbench");
                yield break;
            }

            var st = wb.normalInventory;
            if (P.openedItemInventory2 != st || P.openedItemInventory == null
                || !P.openedItemInventory.isWorkbench)
            {
                r.Fail("the workbench isn't open here");
                yield break;
            }

            bool max = a.Length > 0 && a[0] == "max";
            int first = Level, firstCount = st.slots.Count;
            var done = new List<string>();
            int limit = 1;
            if (max)
            {
                limit = 50;
            }

            for (int n = 0; n < limit; n++)
            {
                int level = Level;
                CraftingRecipes up = null;
                if (level - 1 < wb.levels.Count)
                {
                    up = wb.levels[level - 1].upgradeRecipe;
                }

                if (up == null || up.recipes.Count == 0)
                {
                    if (n == 0)
                    {
                        r.Fail(
                            $"no upgrade from level {level} ({wb.levels.Count} "
                                + "levels)");
                        yield break;
                    }

                    break;
                }

                var recipe = up.recipes[0];
                foreach (var req in recipe.requirements)
                {
                    if (req != null && req.item != null
                        && req.durabilityAmount == 0f)
                    {
                        AsPlayer(
                            () => P.Inventory.addItemTypeToPlayer(
                                req.item.type, req.amount, dropIfNoRoom: true));
                    }
                }

                yield return new WaitForSeconds(0.8f);
                int before = st.slots.Count;
                if (n == 0)
                {
                    _countBefore = before;
                    _sceneBefore = Scene;
                }

                AsPlayer(() => up.doCraft(recipe));
                yield return new WaitForSeconds(1f);
                string needs = string.Join(
                    ", ",
                    recipe.requirements
                        .Where(q => q != null && q.item != null)
                        .Select(q => $"{q.item.type}x{q.amount}"));
                done.Add($"{level} -> {Level} " + $"({needs})");
                if (Level != level + 1)
                {
                    r.Fail(
                        $"the level didn't go up from {level}: "
                            + $"{string.Join("; ", done)}");
                    yield break;
                }

                if (st.slots.Count != before + WorkbenchRules.RowSize)
                {
                    r.Fail(
                        $"the storage didn't grow by a row at level {Level}: "
                            + $"{before} -> {st.slots.Count}");
                    yield break;
                }
            }

            r.Info =
                $"level {first} -> {Level} of {wb.levels.Count} levels; "
                    + $"storage {firstCount} -> {st.slots.Count} slots; "
                    + $"upgrades {string.Join("; ", done)}";
        }

        /// <summary>
        /// "wb-rows-reload IDX TYPE AMOUNT" (host, in a test copy of the
        /// saves): save, load the world again (UI.quickLoadGame, players
        /// follow), and the storage still has its slot count and slot IDX that
        /// item.
        /// </summary>
        private static IEnumerator Reload(string[] a, Result r)
        {
            if (!SaveRedirect.Active)
            {
                r.Fail(
                    "the saves aren't a test copy (--dwmp-sandbox or the test "
                        + "world)");
                yield break;
            }

            var wb = Bench();
            if (wb == null)
            {
                r.Fail("no workbench");
                yield break;
            }

            var st = wb.normalInventory;
            int count = st.slots.Count, level = Level;
            int i = int.Parse(a[0]);
            if (i < 0)
            {
                i += count;
            }

            string before = Slot(st.slots[i]);
            try
            {
                P.closeInventory();
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning(
                    $"[map] wb-rows-reload: closeInventory: {e.Message}");
            }

            yield return new WaitForSeconds(0.8f);
            try
            {
                Singleton<SaveManager>.Instance.Save(doJson: true, force: true);
            }
            catch (Exception e)
            {
                r.Fail($"Save threw: {e.Message}");
                yield break;
            }

            yield return new WaitForSeconds(1.5f);
            Plugin.Log.LogInfo(
                $"[map] wb-rows-reload: saved ({count} slots, slot {i} "
                    + $"{before}, level {level}); loading the world again");
            Singleton<UI>.Instance.quickLoadGame();
            yield return new WaitForSecondsRealtime(3f);
            float until = Time.realtimeSinceStartup + 200f;
            while (Time.realtimeSinceStartup < until && !WorldShare.WorldReady)
            {
                yield return null;
            }

            if (!WorldShare.WorldReady)
            {
                r.Fail("the world didn't load again");
                yield break;
            }

            yield return new WaitForSeconds(1f);
            wb = Bench();
            if (wb == null)
            {
                r.Fail("no workbench after the reload");
                yield break;
            }

            st = wb.normalInventory;
            string after = "(no such slot)";
            if (i < st.slots.Count)
            {
                after = Slot(st.slots[i]);
            }

            r.Info =
                $"before: level {level}, {count} slots, slot {i} {before}; "
                    + $"after: level {Level}, {st.slots.Count} slots, slot {i} "
                    + $"{after}";
            bool ok = false;
            if (a.Length >= 3)
            {
                string expected;
                if (a[1] == "-")
                {
                    expected = "-";
                }
                else
                {
                    expected = $"{a[1]}x{a[2]}";
                }

                ok = after == expected;
            }

            if (Level != level || st.slots.Count != count || after != before
                || !ok)
            {
                r.Fail(r.Info);
            }
        }
    }
}
