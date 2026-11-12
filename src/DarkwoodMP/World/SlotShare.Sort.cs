using System.Collections.Generic;
using DarkwoodMP.Net;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Sort &amp; Stack for the workbench's storage (GitHub issue 10). A button under the storage's slots merges
    /// partial stacks of the same item and then puts the items in order: by the game's item category (melee
    /// weapons, firearms, ammo, ..., as InvItem.Category numbers them), then by item type, then fullest first.
    ///
    /// Stacks merge the way the game merges them when it moves a stack onto another (InvSlot.transferItemAllToPlayer):
    /// only stackable items of the same type, up to the item's maxAmount, and the merged stack's durability is the
    /// average of the two weighted by their amounts (InvItemClass.getStackedDurability), which is how food of
    /// different freshness stacks in the game too. Items that aren't stackable, items that hold ammo, recipes for
    /// different things, and items with upgrades are only moved, never merged, so a weapon keeps its durability,
    /// its loaded rounds and its upgrades.
    ///
    /// Item type (not the item's name in the player's language) is the order within a category, so every game
    /// sorts the same contents into the same slots whatever language it runs in.
    ///
    /// In co-op the host decides. The host (and a game without co-op) sorts at once, and the host sends the
    /// changed slots to everyone as ContainerSlots. A client sorts its own copy at once as a prediction, without
    /// sending those slot changes, and sends StorageSort in a Request. The host refuses it if the player isn't at
    /// the workbench, and otherwise sorts its own copy, which ends the same as the client's when both held the
    /// same items. Either way the host sends the requester every slot of its storage (ContainerSlots), which the
    /// client takes as it is: a wrong prediction (someone else changed a slot meanwhile) is replaced by the host's
    /// slots, and nothing is lost or doubled because only the host's copy counts.
    /// </summary>
    public sealed partial class SlotShare
    {
        /// <summary>The local player pressed Sort &amp; Stack at this storage.</summary>
        public void SortPressed(Inventory inv)
        {
            if (inv == null) return;
            var ctl = Singleton<Controller>.Instance;
            if (ctl != null && !InvItemClass.isNull(ctl.pickedUpItem)) return; // an item on the cursor still belongs to a slot
            var s = _session;
            if (s.IsClient && WorldShare.InWorld)
            {
                var r = ObjRef.Of(inv.gameObject);
                if (!r.Valid) { Plugin.Log.LogWarning($"[slots] can't sort {inv.name}: no name for it in the host's world"); return; }
                // The host's contents must be our starting point, and what we changed before pressing goes first.
                if (_awaiting.ContainsKey(inv)) { Plugin.Log.LogInfo($"[slots] sort of {inv.name}: still waiting for the host's contents"); return; }
                if (_watched.ContainsKey(inv)) Check(inv);
                var changed = Sort(inv);
                var w = s.Begin(MsgType.StorageSort);
                r.Write(w);
                Authority.Request(w, $"sort {inv.name}", inventory: false);
                Plugin.Log.LogInfo($"[slots] sorted {inv.name} here ({changed.Count} slot(s) changed); asked the host to sort it");
                return;
            }
            var done = Sort(inv);
            Plugin.Log.LogInfo($"[slots] sorted {inv.name}: {done.Count} slot(s) changed");
            if (s.IsHost && done.Count > 0)
            {
                var r = ObjRef.Of(inv.gameObject);
                if (r.Valid) SendChanges(r, done, 0, toOnly: false);
            }
        }

        /// <summary>Host: a client's player pressed Sort &amp; Stack (inside a Request).</summary>
        private void OnSort(ulong from, PacketReader r)
        {
            var s = _session;
            if (!s.IsHost || !WorldShare.InWorld) return;
            var obj = ObjRef.Read(r);
            var inv = ObjRef.Resolve<Inventory>(obj);
            if (inv == null || !Is(inv)) { Authority.Refuse("no such workbench storage here"); return; }
            var before = Keys(inv);
            string why = Containers.Refusal(from, inv);
            var ctl = Singleton<Controller>.Instance;
            if (why == null && ctl != null && !InvItemClass.isNull(ctl.pickedUpItem) && ctl.pickedUpItem.slot != null && ctl.pickedUpItem.slot.inventory == inv)
                why = "the host is moving an item in it";
            List<Change> done = why == null ? Sort(inv) : new List<Change>();
            // The requester gets every slot: what to keep if its prediction was right, what to take if not.
            var all = new List<Change>(before.Length);
            for (int i = 0; i < before.Length && i < inv.slots.Count; i++) all.Add(new Change { Slot = i, Old = before[i], New = Json(inv, i) });
            SendChanges(obj, all, from, toOnly: true);
            if (why != null)
            {
                Authority.Refuse(why);
                Plugin.Log.LogInfo($"[authority] {s.NameOf(from)} sorted {inv.name}: refused ({why})");
                return;
            }
            if (done.Count > 0) SendChanges(obj, done, from, toOnly: false);
            Plugin.Log.LogInfo($"[slots] {s.NameOf(from)} sorted {inv.name}: {done.Count} slot(s) changed here");
        }

        /// <summary>ContainerSlots with these changes, in parts of at most 200 (its count is a byte): to one peer (toOnly), or to everyone but that peer.</summary>
        private void SendChanges(ObjRef obj, List<Change> changes, ulong peer, bool toOnly)
        {
            var s = _session;
            for (int at = 0; at < changes.Count; at += 200)
            {
                int n = Mathf.Min(200, changes.Count - at);
                var w = s.Begin(MsgType.ContainerSlots);
                obj.Write(w);
                w.W.Write((byte)n);
                for (int k = at; k < at + n; k++) { w.W.Write((ushort)changes[k].Slot); w.WriteStr(changes[k].Old); w.WriteStr(changes[k].New); }
                if (toOnly) s.SendTo(peer, w, Channel.Control, reliable: true);
                else s.Broadcast(w, Channel.Control, reliable: true, except: peer);
            }
        }

        private sealed class Stack
        {
            public InvItemClass.SaveState State;
            public InvItem Base;
            public bool Mergeable =>
                Base != null && Base.stackable && !Base.hasAmmo && Base.maxAmount > 1
                && (State.upgrades == null || State.upgrades.Count == 0) && (State.modifiers == null || State.modifiers.Count == 0);
            public bool SameItem(Stack o) => State.type == o.State.type && State.isRecipe == o.State.isRecipe && State.recipeFor == o.State.recipeFor;
        }

        /// <summary>
        /// Merge and sort this storage's slots here (each changed slot set as Set does, so it's the slot's
        /// starting point and isn't sent by Check). Returns the slots that changed: what each held (key) and holds (JSON).
        /// </summary>
        private List<Change> Sort(Inventory inv)
        {
            var stacks = new List<Stack>();
            for (int i = 0; i < inv.slots.Count; i++)
            {
                var it = inv.slots[i].invItem;
                if (InvItemClass.isNull(it)) continue;
                stacks.Add(new Stack { State = new InvItemClass.SaveState(it, i), Base = it.baseClass });
            }

            // Merge: each mergeable stack tops up the earlier stacks of the same item, in slot order.
            for (int b = 0; b < stacks.Count; b++)
            {
                var src = stacks[b];
                if (!src.Mergeable) continue;
                for (int a = 0; a < b && src.State.amount > 0; a++)
                {
                    var dst = stacks[a];
                    if (!dst.Mergeable || !dst.SameItem(src) || dst.State.amount <= 0 || dst.State.amount >= dst.Base.maxAmount) continue;
                    int n = Mathf.Min(src.State.amount, dst.Base.maxAmount - dst.State.amount);
                    dst.State.durability = (dst.State.amount * dst.State.durability + src.State.durability * n) / (dst.State.amount + n);
                    dst.State.amount += n;
                    src.State.amount -= n;
                }
            }
            stacks.RemoveAll(st => st.State.amount <= 0 && st.Mergeable);

            stacks.Sort((x, y) =>
            {
                int c = Category(x).CompareTo(Category(y));
                if (c == 0) c = string.CompareOrdinal(x.State.isRecipe ? "~" + x.State.recipeFor : x.State.type, y.State.isRecipe ? "~" + y.State.recipeFor : y.State.type);
                if (c == 0) c = y.State.amount.CompareTo(x.State.amount);
                if (c == 0) c = y.State.durability.CompareTo(x.State.durability);
                if (c == 0) c = x.State.slotId.CompareTo(y.State.slotId); // stable: equal items keep their order
                return c;
            });

            var before = Keys(inv);
            var changes = new List<Change>();
            for (int i = 0; i < inv.slots.Count; i++)
            {
                string json = "";
                if (i < stacks.Count) { stacks[i].State.slotId = i; json = JsonConvert.SerializeObject(stacks[i].State); }
                if (json == Json(inv, i)) continue;
                Set(inv, i, json);
                changes.Add(new Change { Slot = i, Old = before[i], New = json });
            }
            return changes;
        }

        private static int Category(Stack s) => s.Base != null && s.Base.categories != null && s.Base.categories.Count > 0 ? (int)s.Base.categories[0] : 10000;

        internal const string ButtonName = "DarkwoodMP_SortStack";

        /// <summary>
        /// The button, under the storage's slots. It's a copy of the workbench's Repair Item button (UI/Inventory/RepairBtn),
        /// the game's own text button with its frame and rollover, drawn, placed and clicked by the game's Button.
        /// The game has no words for it, so the text isn't localized.
        /// </summary>
        internal static void AddButton(Inventory inv)
        {
            var ui = inv.thisUI;
            if (ui == null || ui.transform.Find(ButtonName) != null) return;
            try
            {
                int cols = Mathf.Max(1, inv.maxColumns);
                int rows = (inv.slots.Count + cols - 1) / cols;
                const float slot = 60f; // Inventory.slotSize
                var at = ui.transform.position + new Vector3((cols - 1) * slot * 0.5f, 5f, -rows * slot + 8f); // as far under the slots as Repair Item is under the workbench's
                var go = Core.AddPrefab("UI/Inventory/RepairBtn", at, Quaternion.Euler(90f, 0f, 0f), ui, worldSpace: true);
                if (go == null) return;
                go.name = ButtonName;
                foreach (var loc in go.GetComponentsInChildren<LocalizedText>(true)) Object.DestroyImmediate(loc);
                var button = go.GetComponent<global::Button>();
                var text = button != null && button.textMesh != null ? button.textMesh : go.GetComponentInChildren<tk2dTextMesh>(true);
                if (text != null) { text.text = "SORT & STACK"; text.Commit(); }
                if (button != null)
                {
                    button.function = "";
                    button.localized = false;
                    if (button.background != null) button.background.SetActive(true);
                    if (button.disabledBackground != null) button.disabledBackground.SetActive(false);
                    button.OnFire = () =>
                    {
                        AudioController.Play("UI_placeItem");
                        if (Instance != null) Instance.SortPressed(inv);
                    };
                }
                Plugin.Log.LogInfo($"[slots] Sort & Stack button added to {inv.name} at {at}");
            }
            catch (System.Exception e) { Plugin.Log.LogWarning($"[slots] couldn't add the Sort & Stack button: {e.Message}"); }
        }
    }

    /// <summary>The workbench's storage was drawn (Workbench.open shows it): add Sort &amp; Stack under it.</summary>
    [HarmonyPatch(typeof(Inventory), nameof(Inventory.show))]
    static class StorageSortButtonPatch
    {
        static void Postfix(Inventory __instance)
        {
            if (__instance == null || !__instance.open) return;
            var wb = __instance.GetComponent<Workbench>();
            if (wb != null && wb.normalInventory == __instance) SlotShare.AddButton(__instance);
        }
    }
}
