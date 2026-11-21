using DarkwoodMP.World.Inventories;
using System.Collections.Generic;
using DarkwoodMP.Net;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Sort &amp; Stack for the workbench's storage (GitHub issue 10). A button
    /// under the storage's slots merges partial stacks of the same item and
    /// then puts the items in order: by the game's item category (melee
    /// weapons, firearms, ammo, ..., as InvItem.Category numbers them), then by
    /// item type, then fullest first.
    ///
    /// Stacks merge the way the game merges them when it moves a stack onto
    /// another (InvSlot.transferItemAllToPlayer): only stackable items of the
    /// same type, up to the item's maxAmount, and the merged stack's durability
    /// is the average of the two weighted by their amounts
    /// (InvItemClass.getStackedDurability), which is how food of different
    /// freshness stacks in the game too. Items that aren't stackable, items
    /// that hold ammo, recipes for different things, and items with upgrades
    /// are only moved, never merged, so a weapon keeps its durability, its
    /// loaded rounds and its upgrades.
    ///
    /// Items whose fuel is their durability (InvItem.stacksDurability:
    /// gasoline, the lantern) never stack, but the game pours one into another
    /// when a player drops one on the other (InvSlot.swapItems): up to the
    /// item's maxDurability, and what doesn't fit stays in the one poured from.
    /// Sort &amp; Stack pours them the same way, each into the earlier ones of
    /// the same type in slot order, so the cans end up full but one. A can
    /// poured empty is gone, as one is when its fuel runs out
    /// (InvItemClass.drainDurability). One the game keeps when it's empty (a
    /// reloadable, repairable or regenerating item, or one that rots: the
    /// lantern) stays, empty, so a sort never takes a player's lantern.
    ///
    /// Item type (not the item's name in the player's language) is the order
    /// within a category, so every game sorts the same contents into the same
    /// slots whatever language it runs in.
    ///
    /// In co-op the host decides. The host (and a game without co-op) sorts at
    /// once, and the host sends the changed slots to everyone as
    /// ContainerSlots. A client sorts its own copy at once as a prediction,
    /// without sending those slot changes, and sends StorageSort in a Request.
    /// The host refuses it if the player isn't at the workbench, and otherwise
    /// sorts its own copy, which ends the same as the client's when both held
    /// the same items. Either way the host sends the requester every slot of
    /// its storage (ContainerSlots), which the client takes as it is: a wrong
    /// prediction (someone else changed a slot meanwhile) is replaced by the
    /// host's slots, and nothing is lost or doubled because only the host's
    /// copy counts.
    /// </summary>
    internal sealed partial class SlotShare
    {
        /// <summary>
        /// The local player pressed Sort &amp; Stack at this storage.
        /// </summary>
        public void SortPressed(Inventory inv)
        {
            if (inv == null)
            {
                return;
            }

            var s = _session;
            var r = ObjRef.Of(inv.gameObject);
            switch (SortRules.Pressed(FactsOf(inv, r)))
            {
                case SortPress.Nothing:
                    return;
                case SortPress.NoName:
                    Plugin.Log.LogWarning(
                        $"[slots] can't sort {inv.name}: no name for it in "
                            + "the host's world");
                    return;
                case SortPress.StillWaiting:
                    Plugin.Log.LogInfo(
                        $"[slots] sort of {inv.name}: still waiting for the "
                            + "host's contents");
                    return;
                case SortPress.PredictAndAsk:
                    PredictSort(inv, r);
                    return;
            }

            var done = Sort(inv);
            Plugin.Log.LogInfo(
                $"[slots] sorted {inv.name}: {done.Count} slot(s) changed");
            if (SortRules.SendsSorted(s.IsHost, done.Count, r.Valid))
            {
                SendChanges(r, done);
            }
        }

        /// <summary>
        /// What pressing Sort &amp; Stack looks at: the only code that reads
        /// the cursor and the storage's state for SortRules.Pressed.
        /// </summary>
        private SortPressFacts FactsOf(Inventory inv, ObjRef r)
        {
            var ctl = Singleton<Controller>.Instance;
            return new SortPressFacts
            {
                Holding = ctl != null && !InvItemClass.isNull(ctl.pickedUpItem),
                IsClient = _session.IsClient,
                InWorld = WorldShare.InWorld,
                Named = r.Valid,
                Waiting = _awaiting.ContainsKey(inv),
            };
        }

        /// <summary>
        /// Client: sort our copy as a prediction and ask the host to sort.
        /// The host's contents are our starting point, and what we changed
        /// before pressing goes first.
        /// </summary>
        private void PredictSort(Inventory inv, ObjRef r)
        {
            if (_watched.ContainsKey(inv))
            {
                Check(inv);
            }

            var changed = Sort(inv);
            var w = _session.Begin(MsgType.StorageSort);
            StorageSortMessage.Write(w, ObjRef.NameOf(r));
            Coop.Current.Authority.Request(
                w, $"sort {inv.name}", inventory: false);
            Plugin.Log.LogInfo(
                $"[slots] sorted {inv.name} here ({changed.Count} slot(s) "
                    + "changed); asked the host to sort it");
        }

        /// <summary>
        /// Host: a client's player pressed Sort &amp; Stack (inside a Request).
        /// </summary>
        private void OnSort(ulong from, PacketReader r)
        {
            var s = _session;
            if (!s.IsHost || !WorldShare.InWorld)
            {
                return;
            }

            var obj = ObjRef.RefOf(StorageSortMessage.Read(r));
            var inv = ObjRef.Resolve<Inventory>(obj);
            var verdict = SortRules.MaySort(FactsOf(from, inv));
            if (verdict.Reason == Refusal.NoSuchStorage)
            {
                Coop.Current.Authority.Refuse(Refusals.Text(verdict));
                return;
            }

            var before = Keys(inv);
            List<SlotChange> done;
            if (!verdict.Refused)
            {
                done = Sort(inv);
            }
            else
            {
                done = new List<SlotChange>();
            }

            // The requester gets every slot: what to keep if its prediction was
            // right, what to take if not.
            var all = new List<SlotChange>(before.Length);
            for (int i = 0; i < before.Length && i < inv.slots.Count; i++)
            {
                all.Add(new SlotChange
                {
                    Slot = i,
                    Old = before[i],
                    New = Json(inv, i)
                });
            }

            SendChanges(obj, all, from, toOnly: true);
            if (verdict.Refused)
            {
                string why = Refusals.Text(verdict);
                Coop.Current.Authority.Refuse(why);
                Plugin.Log.LogInfo(
                    $"[authority] {s.NameOf(from)} sorted {inv.name}: refused "
                        + $"({why})");
                return;
            }

            if (done.Count > 0)
            {
                SendChanges(obj, done, from);
            }

            Plugin.Log.LogInfo(
                $"[slots] {s.NameOf(from)} sorted {inv.name}: {done.Count} "
                    + "slot(s) changed here");
        }

        /// <summary>
        /// What the host knows of a client's Sort &amp; Stack: the only code
        /// that reads the storage and the host's cursor for SortRules.MaySort.
        /// </summary>
        private SortFacts FactsOf(ulong from, Inventory inv)
        {
            if (inv == null || !Is(inv))
            {
                return new SortFacts { StorageHere = false };
            }

            var ctl = Singleton<Controller>.Instance;
            bool moving = ctl != null
                && !InvItemClass.isNull(ctl.pickedUpItem)
                && ctl.pickedUpItem.slot != null
                && ctl.pickedUpItem.slot.inventory == inv;
            return new SortFacts
            {
                StorageHere = true,
                Container = Containers.MayChange(from, inv),
                HostMoving = moving,
            };
        }

        /// <summary>
        /// ContainerSlots with these changes, in parts of at most 200 (its
        /// count is a byte): to one peer (toOnly); otherwise from the host to
        /// everyone but that peer, and from a client to the host.
        /// </summary>
        private void SendChanges(
            ObjRef obj, List<SlotChange> changes, ulong peer = 0,
            bool toOnly = false)
        {
            var s = _session;
            for (int at = 0; at < changes.Count;
                at += ContainerSlotsMessage.MaxChanges)
            {
                int n = Mathf.Min(
                    ContainerSlotsMessage.MaxChanges, changes.Count - at);
                var w = s.Begin(MsgType.ContainerSlots);
                ContainerSlotsMessage.Write(
                    w, ObjRef.NameOf(obj), changes.GetRange(at, n));

                if (toOnly)
                {
                    s.SendTo(peer, w, Channel.Control, reliable: true);
                }
                else if (s.IsHost)
                {
                    s.Broadcast(
                        w, Channel.Control, reliable: true, except: peer);
                }
                else
                {
                    s.SendToHost(w, Channel.Control, reliable: true);
                }
            }
        }

        /// <summary>
        /// Merge and sort this storage's slots here as SortRules.Sort says
        /// (each changed slot set as Set does, so it's the slot's starting
        /// point and isn't sent by Check). Returns the slots that changed:
        /// what each held (key) and holds (JSON).
        /// </summary>
        private List<SlotChange> Sort(Inventory inv)
        {
            var changes = SortSlots(inv);
            if (_watched.TryGetValue(inv, out var keys))
            {
                foreach (var c in changes)
                {
                    if (c.Slot < keys.Length)
                    {
                        keys[c.Slot] = Key(inv.slots[c.Slot]);
                    }
                }
            }

            return changes;
        }

        /// <summary>
        /// Sort: the slots' part, which needs no session.
        /// </summary>
        private static List<SlotChange> SortSlots(Inventory inv)
        {
            var states = new Dictionary<int, InvItemClass.SaveState>();
            var stacks = new List<SortStack>();
            for (int i = 0; i < inv.slots.Count; i++)
            {
                var it = inv.slots[i].invItem;
                if (InvItemClass.isNull(it))
                {
                    continue;
                }

                var state = new InvItemClass.SaveState(it, i);
                states[i] = state;
                stacks.Add(FactsOf(state, it.baseClass));
            }

            var sorted = SortRules.Sort(stacks);
            var before = Keys(inv);
            var changes = new List<SlotChange>();
            for (int i = 0; i < inv.slots.Count; i++)
            {
                string json = "";
                if (i < sorted.Count)
                {
                    var state = states[sorted[i].From];
                    state.amount = sorted[i].Amount;
                    state.durability = sorted[i].Durability;
                    state.slotId = i;
                    json = JsonConvert.SerializeObject(state);
                }

                if (json == Json(inv, i))
                {
                    continue;
                }

                Fill(inv, i, json);
                changes.Add(new SlotChange
                {
                    Slot = i,
                    Old = before[i],
                    New = json
                });
            }

            return changes;
        }

        /// <summary>
        /// What Sort &amp; Stack looks at in one filled slot: the only code
        /// that reads an item's saved state and its baseClass for
        /// SortRules.Sort.
        /// </summary>
        private static SortStack FactsOf(
            InvItemClass.SaveState state, InvItem item)
        {
            int upgrades = 0;
            if (state.upgrades != null)
            {
                upgrades = state.upgrades.Count;
            }

            int modifiers = 0;
            if (state.modifiers != null)
            {
                modifiers = state.modifiers.Count;
            }

            var stack = new SortStack
            {
                Slot = state.slotId,
                Type = state.type,
                Amount = state.amount,
                Durability = state.durability,
                Recipe = state.isRecipe,
                RecipeFor = state.recipeFor,
                Upgrades = upgrades,
                Modifiers = modifiers,
                Category = SortRules.NoCategory,
            };

            if (item == null)
            {
                return stack;
            }

            int category = SortRules.NoCategory;
            if (item.categories != null && item.categories.Count > 0)
            {
                category = (int)item.categories[0];
            }

            return stack with
            {
                HasBase = true,
                Stackable = item.stackable,
                HasAmmo = item.hasAmmo,
                MaxAmount = item.maxAmount,
                StacksDurability = item.stacksDurability,
                MaxDurability = item.maxDurability,
                Reloadable = item.canBeReloaded,
                Repairable = item.repairable,
                Regenerates = item.regeneratesWhenInactive,
                Rots = item.rottenItem != null,
                Category = category,
            };
        }

        internal const string ButtonName = "DarkwoodMP_SortStack";

        /// <summary>
        /// The button was pressed: the session's SortPressed, or in a game
        /// played alone a sort of this storage here.
        /// </summary>
        private static void Pressed(Inventory inv)
        {
            CoopSession coop = Coop.Current;
            if (coop != null)
            {
                coop.Slots.SortPressed(inv);
                return;
            }

            SortAlone(inv);
        }

        /// <summary>
        /// Sort &amp; Stack with no session: what SortPressed does when it
        /// isn't a client, without sending anything.
        /// </summary>
        private static void SortAlone(Inventory inv)
        {
            if (inv == null)
            {
                return;
            }

            var ctl = Singleton<Controller>.Instance;
            var press = new SortPressFacts
            {
                Holding = ctl != null && !InvItemClass.isNull(ctl.pickedUpItem),
                InWorld = WorldShare.InWorld,
            };

            if (SortRules.Pressed(press) == SortPress.Nothing)
            {
                return;
            }

            var done = SortSlots(inv);
            Plugin.Log.LogInfo(
                $"[slots] sorted {inv.name}: {done.Count} slot(s) changed");
        }

        /// <summary>
        /// The button, under the storage's slots. It's a copy of the
        /// workbench's Repair Item button (UI/Inventory/RepairBtn), the game's
        /// own text button with its frame and rollover, drawn, placed and
        /// clicked by the game's Button.
        /// The game has no words for it, so the text isn't localized.
        /// </summary>
        internal static void AddButton(Inventory inv)
        {
            var ui = inv.thisUI;
            if (ui == null || ui.transform.Find(ButtonName) != null)
            {
                return;
            }

            try
            {
                int cols = Mathf.Max(1, inv.maxColumns);
                int rows = (inv.slots.Count + cols - 1) / cols;
                const float slot = 60f; // Inventory.slotSize
                // as far under the slots as Repair Item is under the
                // workbench's
                var at = ui.transform.position + new Vector3(
                    (cols - 1) * slot * 0.5f, 5f, -rows * slot + 8f);
                var go = Core.AddPrefab(
                    "UI/Inventory/RepairBtn", at, Quaternion.Euler(90f, 0f, 0f),
                    ui, worldSpace: true);
                if (go == null)
                {
                    return;
                }

                go.name = ButtonName;
                foreach (var loc in go.GetComponentsInChildren<LocalizedText>(
                    true))
                {
                    Object.DestroyImmediate(loc);
                }

                var button = go.GetComponent<global::Button>();
                tk2dTextMesh text;
                if (button != null && button.textMesh != null)
                {
                    text = button.textMesh;
                }
                else
                {
                    text = go.GetComponentInChildren<tk2dTextMesh>(true);
                }

                if (text != null)
                {
                    text.text = "SORT & STACK";
                    text.Commit();
                }

                if (button != null)
                {
                    button.function = "";
                    button.localized = false;
                    if (button.background != null)
                    {
                        button.background.SetActive(true);
                    }

                    if (button.disabledBackground != null)
                    {
                        button.disabledBackground.SetActive(false);
                    }

                    button.OnFire = () =>
                    {
                        AudioController.Play("UI_placeItem");
                        Pressed(inv);
                    };
                }

                Plugin.Log.LogInfo(
                    $"[slots] Sort & Stack button added to {inv.name} at {at}");
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning(
                    "[slots] couldn't add the Sort & Stack button: "
                        + $"{e.Message}");
            }
        }
    }

    /// <summary>
    /// The workbench's storage was drawn (Workbench.open shows it): add Sort
    /// &amp; Stack under it.
    /// </summary>
    [HarmonyPatch(typeof(Inventory), nameof(Inventory.show))]
    static class StorageSortButtonPatch
    {
        static void Postfix(Inventory __instance)
        {
            if (__instance == null || !__instance.open)
            {
                return;
            }

            var wb = __instance.GetComponent<Workbench>();
            if (wb != null && wb.normalInventory == __instance)
            {
                SlotShare.AddButton(__instance);
            }
        }
    }
}
