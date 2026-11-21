using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Each workbench level adds a row of RowSize slots to the workbench's
    /// storage (Workbench.normalInventory):
    /// its slot count is its prefab's count plus RowSize for each level above
    /// the first (Controller.workbenchLevel).
    ///
    /// The game saves a container's slot count with its contents
    /// (Inventory.SaveState.slotCount) and makes that many slots again when it
    /// loads, so the extra slots and what's in them are kept by the save
    /// itself. A save made without the mod has the prefab's count; it gets its
    /// rows when the world has loaded (Update), and whenever a workbench is
    /// opened. The storage only ever grows: a save with more slots than the
    /// level gives keeps them, so nothing in them is lost.
    ///
    /// In co-op the level is the host's (SlotShare.OnLevel): the WorkbenchLevel
    /// message reaches a client before any change to the new slots (both go
    /// reliably on one channel), and the client grows its storage as it takes
    /// it. A client's world is the host's save, which has the host's slot count
    /// already; the host's contents (ContainerSlotsAll) carry it too, and
    /// SlotShare grows the storage to it before taking them.
    /// </summary>
    public static class WorkbenchStorage
    {
        /// <summary>
        /// Slots added for each level above the first: one row of the storage's
        /// six columns.
        /// </summary>
        public const int RowSize = 6;

        /// <summary>
        /// The level a new game starts at (Controller.workbenchLevel's
        /// default).
        /// </summary>
        public const int BaseLevel = 1;

        /// <summary>
        /// The storage's slot count at the base level when its prefab can't be
        /// found.
        /// </summary>
        public const int FallbackBase = 30;

        private const string DefaultPrefab = "Furnitures/workbench_01";

        private static readonly Dictionary<string, int> _base =
            new Dictionary<string, int>();
        private static int _appliedScene = -1;
        private static int _appliedLevel = -1;

        /// <summary>
        /// The storage's slot count at the base level: its prefab's.
        /// </summary>
        public static int BaseSlots(Workbench wb)
        {
            var so = wb.GetComponent<SaveableObject>();
            string name;
            if (so != null && !string.IsNullOrEmpty(so.prefabName))
            {
                name = so.prefabName;
            }
            else
            {
                name = DefaultPrefab;
            }

            if (_base.TryGetValue(name, out int n))
            {
                return n;
            }

            n = 0;
            foreach (string path in new[]
            {
                "Prefabs/" + name,
                name,
                "Prefabs/" + DefaultPrefab
            })
            {
                var go = Resources.Load(path) as GameObject;
                Inventory inv = null;
                if (go != null)
                {
                    Workbench bench = go.GetComponent<Workbench>();
                    inv = null;
                    if (bench != null)
                    {
                        inv = bench.normalInventory;
                    }

                    if (inv == null)
                    {
                        inv = go.GetComponent<Inventory>();
                    }
                }

                if (inv != null && inv.slots != null && inv.slots.Count > 0)
                {
                    n = inv.slots.Count;
                    break;
                }
            }

            if (n == 0)
            {
                n = FallbackBase;
                Plugin.Log.LogWarning(
                    $"[workbench] no prefab for {wb.name} ('{name}'): its "
                        + $"storage counts {n} slots at level {BaseLevel}");
            }

            _base[name] = n;
            return n;
        }

        /// <summary>
        /// The slot count the storage should have at this level.
        /// </summary>
        public static int Wanted(Workbench wb, int level) =>
            BaseSlots(wb) + RowSize * Mathf.Max(0, level - BaseLevel);

        private static int Level
        {
            get
            {
                if (Singleton<Controller>.Instance != null)
                {
                    return Singleton<Controller>.Instance.workbenchLevel;
                }

                return BaseLevel;
            }
        }

        /// <summary>
        /// Every frame: once a world has loaded (again), or the level changed,
        /// give every workbench its rows.
        /// </summary>
        public static void Update()
        {
            if (!WorldShare.WorldReady)
            {
                _appliedScene = -1;
                return;
            }

            int scene =
                UnityEngine.SceneManagement.SceneManager
                    .GetActiveScene().handle;
            int level = Level;
            if (scene == _appliedScene && level == _appliedLevel)
            {
                return;
            }

            bool loaded = scene != _appliedScene;
            _appliedScene = scene;
            _appliedLevel = level;
            string why = "level changed";
            if (loaded)
            {
                why = "world loaded";
            }

            ApplyAll(why);
        }

        /// <summary>
        /// Give every workbench's storage the slot count of the current level.
        /// </summary>
        public static void ApplyAll(string why)
        {
            int level = Level;
            foreach (var wb in Object.FindObjectsOfType<Workbench>(true))
            {
                if (wb == null || wb.normalInventory == null)
                {
                    continue;
                }

                int before = wb.normalInventory.slots.Count;
                if (Fit(wb.normalInventory, Wanted(wb, level)))
                {
                    Plugin.Log.LogInfo(
                        $"[workbench] {wb.name} "
                            + $"{ObjRef.Of(wb.normalInventory.gameObject)} "
                            + $"storage {before} -> "
                            + $"{wb.normalInventory.slots.Count} slots "
                            + $"(level {level}, {why})");
                }
                else if (why == "world loaded")
                {
                    Plugin.Log.LogInfo(
                        $"[workbench] {wb.name} "
                            + $"{ObjRef.Of(wb.normalInventory.gameObject)} "
                            + $"storage {before} slots (level {level}, "
                            + $"base {BaseSlots(wb)}, "
                            + $"{wb.normalInventory.maxColumns} columns)");
                }
            }
        }

        /// <summary>
        /// Grow this storage to at least count slots (never shrinks it). If
        /// it's open, it's drawn again with the new rows. The new slots are
        /// empty, and SlotShare watches them from there (Grew): growing isn't a
        /// change of the player's, and one the player made before still goes.
        /// True if it grew.
        /// </summary>
        public static bool Fit(Inventory inv, int count)
        {
            if (inv == null || inv.slots.Count >= count)
            {
                return false;
            }

            bool open = inv.open;
            var player = Player.Instance;
            bool second =
                open && player != null && player.openedItemInventory2 == inv;
            if (open)
            {
                inv.hide();
            }

            while (inv.slots.Count < count)
            {
                var slot = new InvSlot
                {
                    inventory = inv,
                    name = "Slot" + (inv.slots.Count + 1)
                };

                inv.slots.Add(slot);
            }

            if (inv.slots.Count >= 30)
            {
                // the game's rule for big containers (Inventory.init)
                inv.maxColumns = RowSize;
            }

            if (open)
            {
                inv.show("Storage"); // as Workbench.open shows it
                if (second && player != null)
                {
                    player.openedItemInventory2 = inv;
                }
            }

            SlotShare instance = SlotShare.Instance;
            if (instance != null)
            {
                instance.Grew(inv);
            }

            CoopSession coop = Coop.Current;
            if (coop != null)
            {
                coop.Ui.Changed(inv);
            }

            return true;
        }
    }

    /// <summary>
    /// The workbench's storage was drawn (Inventory.show): if its rows and the
    /// Sort &amp; Stack button under them would run off the bottom of the
    /// screen, the whole window is drawn smaller to fit. Runs after the button
    /// is added (StorageSortButtonPatch), so the button shrinks with it. The
    /// game's ScaleToResolution takes the window's scale as its base on the
    /// next frame, so a change of resolution keeps the fit.
    /// </summary>
    [HarmonyPatch(typeof(Inventory), nameof(Inventory.show))]
    [HarmonyPriority(Priority.Low)]
    static class StorageFitPatch
    {
        /// <summary>
        /// Room under the last row's centre for its lower half and the Sort
        /// &amp; Stack button (UI units at 1080p).
        /// </summary>
        private const float Below = 30f + 70f;

        static void Postfix(Inventory __instance)
        {
            var inv = __instance;
            if (inv == null || !inv.open || inv.thisUI == null)
            {
                return;
            }

            var wb = inv.GetComponent<Workbench>();
            if (wb == null || wb.normalInventory != inv)
            {
                return;
            }

            Camera cam = null;
            if (Core.CamUI != null)
            {
                cam = Core.CamUI.GetComponent<Camera>();
            }

            if (cam == null)
            {
                return;
            }

            const float slot = 60f; // Inventory.slotSize
            int cols = Mathf.Max(1, inv.maxColumns),
                rows = (inv.slots.Count + cols - 1) / cols;
            // Measured unscaled now; ScaleToResolution scales the window by
            // this factor next frame, about the same spot.
            float mod = Mathf.Min(
                Core.ResolutionHeightModifier, Core.ResolutionWidthModifier);
            var at = inv.thisUI.transform.position;
            float top = cam.WorldToViewportPoint(
                at + new Vector3(0f, 0f, slot * 0.5f)).y;
            float bottom = cam.WorldToViewportPoint(
                at - new Vector3(0f, 0f, (rows - 1) * slot + Below)).y;
            float need = (top - bottom) * mod, room = top - 0.01f;
            if (need <= 0f || need <= room)
            {
                return;
            }

            float scale = room / need;
            inv.thisUI.transform.localScale = Vector3.one * scale;
            Plugin.Log.LogInfo(
                $"[workbench] storage of {inv.slots.Count} slots ({rows} rows) "
                    + $"drawn at {scale:0.00} of its size to fit the screen");
        }
    }

    /// <summary>
    /// A workbench opened: its storage has the rows of the current level before
    /// it's drawn.
    /// </summary>
    [HarmonyPatch(typeof(Workbench), nameof(Workbench.open))]
    static class WorkbenchOpenRowsPatch
    {
        static void Prefix(Workbench __instance)
        {
            if (__instance == null || __instance.normalInventory == null
                || Singleton<Controller>.Instance == null)
            {
                return;
            }

            WorkbenchStorage.Fit(
                __instance.normalInventory,
                WorkbenchStorage.Wanted(
                    __instance, Singleton<Controller>.Instance.workbenchLevel));
        }
    }
}
