using DarkwoodMP.World.Inventories;
using System.Collections.Generic;
using DarkwoodMP.Combat;
using DarkwoodMP.Net;
using DarkwoodMP.World.Workbenches;
using Newtonsoft.Json;
using UnityEngine;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Containers changed slot by slot, with the host deciding: the workbench's
    /// storage, used by several players at once, and every container a client
    /// changes (the host's own changes go out whole: Containers). The host
    /// settles who got there first, and refuses a change from a player who
    /// isn't at the container, to one that is still locked in the host's world
    /// (a crate not picked open there: Containers.LockedReason), or while
    /// someone else has it open.
    ///
    /// While a player has the storage open, its slots are compared a few times
    /// a second with how they were.
    /// Each slot that changed goes out as ContainerSlots:
    /// "slot i was A, is now B". The host makes the change in its world only if
    /// slot i is still A there, and passes it on; the host's own changes go
    /// straight out. If slot i had already changed (two players took the same
    /// stack at the same moment), the host refuses (ContainerSlotRefused, with
    /// what slot i holds): that player's slot is set to the host's, and what
    /// their change moved is undone for them. Items they put in go back into
    /// their bags or hotbar (at their feet only if there's no room); items they
    /// took are taken back from what they carry. Nothing is lost or doubled.
    ///
    /// A client's "slot i was A" must be what the host has, or every change is
    /// refused: its copy of the world is the host's save from when it joined,
    /// and changes the host made while it downloaded and loaded it never
    /// reached it. So a client asks the host what a container holds as it opens
    /// it (ContainerSlotsAsk), and sends no change before the answer
    /// (ContainerSlotsAll) has become its starting point; and asks for every
    /// workbench's storage once its world has loaded.
    ///
    /// A slot is compared by its item's type, amount, durability, recipe and
    /// upgrades; its contents travel as the game saves them
    /// (InvItemClass.SaveState, as JSON; "" for an empty slot).
    ///
    /// Food rots, and some items drain or recharge, by themselves while their
    /// container is on the game's refreshing list (Inventory.refreshItems),
    /// which each game runs on its own. Those durability changes are the
    /// world's, not the player's: they become the slot's starting point without
    /// being sent (Drift). The host accepts a change to a slot whose only
    /// difference from what the client saw is such an item's durability
    /// (RotOnly), and starts the rot in its own copy when a client opens the
    /// container (OnAsk), as drawing the slots would. A client takes the host's
    /// durability each time it opens the container.
    ///
    /// The workbench's level is shared too (WorkbenchLevel): an upgrade made at
    /// anyone's workbench raises it for everyone. The highest level wins.
    /// </summary>
    internal sealed partial class SlotShare
    {
        private readonly Session _session;

        // what each watched storage's slots held when last compared (their
        // keys)
        private readonly Dictionary<Inventory, string[]> _watched =
            new Dictionary<Inventory, string[]>();
        private readonly Dictionary<Inventory, ObjRef> _refs =
            new Dictionary<Inventory, ObjRef>();
        private readonly Dictionary<Inventory, bool> _isStorage =
            new Dictionary<Inventory, bool>();

        public SlotShare(CoopSession coop)
        {
            _session = coop.Net;
            coop.Handle(MsgType.ContainerSlots, OnSlots);
            coop.Handle(MsgType.ContainerSlotRefused, OnRefused);
            coop.Handle(MsgType.WorkbenchLevel, OnLevel);
            coop.Handle(MsgType.ContainerSlotsAsk, OnAsk);
            coop.Handle(MsgType.ContainerSlotsAll, OnContents);
            coop.Handle(MsgType.StorageSort, OnSort);
        }

        /// <summary>
        /// Whether this container is a workbench's storage (shared slot by
        /// slot).
        /// </summary>
        public bool Is(Inventory inv)
        {
            if (inv == null)
            {
                return false;
            }

            if (_isStorage.TryGetValue(inv, out bool known))
            {
                return known;
            }

            bool yes = false;
            foreach (var wb in Object.FindObjectsOfType<Workbench>())
            {
                if (wb != null && wb.normalInventory == inv)
                {
                    yes = true;
                    break;
                }
            }

            _isStorage[inv] = yes;
            return yes;
        }

        // ---- watching -------------------------------------------------------

        /// <summary>
        /// Called a few times a second with each storage the local player has
        /// open. The first call records the slots as they are here; on a client
        /// it also asks the host what they hold there (ContainerSlotsAsk), and
        /// no change is sent until that answer has become the starting point
        /// (OnContents), or AskTimeout went by. A click pickup (askHost false)
        /// is over within one frame, so it doesn't wait.
        /// </summary>
        public void Check(Inventory inv, bool askHost = true)
        {
            if (inv == null)
            {
                return;
            }

            var keys = Keys(inv);
            _watched.TryGetValue(inv, out var before);
            if (SlotRules.IsFirstLook(before, keys.Length))
            {
                _watched[inv] = keys; // as we found it
                var found = ObjRef.Of(inv.gameObject);
                _refs[inv] = found;
                if (SlotRules.Asks(askHost, _session.IsClient, found.Valid))
                {
                    Ask(inv, found);
                }

                return;
            }

            if (_awaiting.TryGetValue(inv, out float asked))
            {
                var now = new Seconds(Time.unscaledTime);
                if (SlotRules.StillWaiting(new Seconds(asked), now))
                {
                    // what we change meanwhile goes once the host's answer is
                    // in
                    return;
                }

                _awaiting.Remove(inv);
                Plugin.Log.LogWarning(
                    $"[slots] {_refs[inv]} no contents from host in "
                        + $"{SlotRules.AskTimeout:0} s; sending changes "
                        + "against what we saw");
            }

            var changed = new List<SlotChange>();
            foreach (int i in SlotRules.Changed(before, keys))
            {
                changed.Add(new SlotChange
                {
                    Slot = i,
                    Old = before[i],
                    New = Json(inv, i)
                });
            }

            if (changed.Count == 0)
            {
                return;
            }

            var r = _refs[inv];
            if (!r.Valid)
            {
                return;
            }

            SendChanges(r, changed);
            if (_session.IsClient)
            {
                RememberSent(r, changed);
            }

            if (WorldActions.Debug)
            {
                string changes = string.Join(
                    ", ",
                    changed.ConvertAll(
                        c => $"{c.Slot}: '{c.Old}' -> '{keys[c.Slot]}'"));
                Plugin.Log.LogInfo(
                    $"[world] sent {changed.Count} slot(s) of {inv.name} "
                        + $"{r}: {changes}");
            }

            _watched[inv] = keys;
        }

        /// <summary>
        /// The local player closed it: one last look, then stop watching.
        /// </summary>
        public void Closed(Inventory inv)
        {
            // closed before the host answered: what we changed goes against
            // what we saw
            _awaiting.Remove(inv);
            if (inv != null)
            {
                Check(inv);
            }
            // destroyed (a Unity object that is == null but still a key here):
            // its slots went empty
            else if (_watched.TryGetValue(inv, out var before)
                && _refs.TryGetValue(inv, out var r) && r.Valid)
            {
                SendEmptied(r, before);
            }

            _watched.Remove(inv);
            _refs.Remove(inv);
        }

        // ---- the host's contents as the starting point ----------------------

        // client: what we have open and asked the host about, and when
        private readonly Dictionary<Inventory, float> _awaiting =
            new Dictionary<Inventory, float>();
        private int _askedScene = -1;

        private void Ask(Inventory inv, ObjRef r)
        {
            if (inv != null)
            {
                _awaiting[inv] = Time.unscaledTime;
            }

            var w = _session.Begin(MsgType.ContainerSlotsAsk);
            ContainerSlotsAskMessage.Write(w, ObjRef.NameOf(r));
            _session.SendToHost(w, Channel.Control, reliable: true);
            if (WorldActions.Debug)
            {
                Plugin.Log.LogInfo(
                    $"[slots] {r} asked the host for its contents");
            }
        }

        /// <summary>
        /// Client, every frame: once our world has loaded (again), ask the host
        /// what every workbench's storage holds. Our world is the host's save
        /// as it was when we were offered it; what the host put in or took out
        /// while we downloaded and loaded it came to us before we were in the
        /// world, and was dropped.
        /// </summary>
        public void Update()
        {
            if (!_session.IsClient || !WorldShare.WorldReady)
            {
                return;
            }

            int scene =
                UnityEngine.SceneManagement.SceneManager
                    .GetActiveScene().handle;
            if (scene == _askedScene)
            {
                return;
            }

            _askedScene = scene;
            _isStorage.Clear(); // a new world's objects
            foreach (var wb in Object.FindObjectsOfType<Workbench>())
            {
                if (wb == null || wb.normalInventory == null)
                {
                    continue;
                }

                var r = ObjRef.Of(wb.normalInventory.gameObject);
                if (r.Valid)
                {
                    Inventory inv = null;
                    if (_watched.ContainsKey(wb.normalInventory))
                    {
                        inv = wb.normalInventory;
                    }

                    Ask(inv, r);
                }
            }
        }

        /// <summary>
        /// Host: a client wants what a container holds here (ContainerSlotsAll;
        /// "" when we haven't got it).
        /// </summary>
        private void OnAsk(ulong from, PacketReader r)
        {
            var s = _session;
            if (!s.IsHost || !WorldShare.InWorld)
            {
                return;
            }

            var obj = ObjRef.RefOf(ContainerSlotsAskMessage.Read(r));
            var inv = ObjRef.Resolve<Inventory>(obj);
            if (inv != null)
            {
                StartRot(inv);
            }

            string doc = "";
            if (inv != null)
            {
                doc = JsonConvert.SerializeObject(new Inventory.SaveState(inv));
            }

            var w = s.Begin(MsgType.ContainerSlotsAll);
            ContainerSlotsAllMessage.Write(w, ObjRef.NameOf(obj), doc);
            s.SendTo(from, w, Channel.Control, reliable: true);
            if (WorldActions.Debug)
            {
                string name = "not found here";
                if (inv != null)
                {
                    name = inv.name;
                }

                Plugin.Log.LogInfo(
                    $"[slots] {obj} contents sent to {s.NameOf(from)}: "
                        + name);
            }
        }

        /// <summary>
        /// Client: what a container holds on the host. Closed, it's loaded as
        /// it is. Open, each slot we haven't changed since we opened it takes
        /// the host's contents; one we have changed stays ours if the host's
        /// slot holds what we saw there (the change goes at the next Check),
        /// and is undone here otherwise, as the host would refuse it. From then
        /// on our changes are compared with the host's slots.
        /// </summary>
        private void OnContents(ulong from, PacketReader r)
        {
            var s = _session;
            if (!s.IsClient || from != s.HostId || !WorldShare.InWorld)
            {
                return;
            }

            var obj = ObjRef.RefOf(
                ContainerSlotsAllMessage.Read(r, out string contents));
            var inv = ObjRef.Resolve<Inventory>(obj);
            bool waited = inv != null && _awaiting.Remove(inv);
            var answer = SlotRules.Answer(FactsOf(inv, contents, waited));
            if (answer == HostContents.NotFound
                || answer == HostContents.HostHasNone)
            {
                string why = "the host hasn't got it";
                if (answer == HostContents.NotFound)
                {
                    why = "not found here";
                }

                Plugin.Log.LogInfo($"[slots] {obj} contents from host: " + why);
                return;
            }

            // Open and already sending changes (we stopped waiting, or this
            // answers an earlier ask): the host's answer is older than what we
            // sent since, which the host settles slot by slot.
            if (answer == HostContents.NotUsed)
            {
                Plugin.Log.LogInfo(
                    $"[slots] {obj} contents from host: not used, our changes "
                        + "to it already went");
                return;
            }

            Inventory.SaveState state;
            try
            {
                state = JsonConvert.DeserializeObject<Inventory.SaveState>(
                    contents);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning(
                    $"[slots] {obj} bad contents from host: {e.Message}");
                return;
            }

            if (state == null)
            {
                return;
            }

            if (SlotRules.GrowsRows(state.slotCount, inv.slots.Count, Is(inv)))
            {
                // the host's rows (an upgrade)
                WorkbenchStorage.Fit(inv, state.slotCount);
            }

            if (state.slotCount != inv.slots.Count)
            {
                Plugin.Log.LogWarning(
                    "[slots] "
                        + $"{obj} has {state.slotCount} slots on the host, "
                        + $"{inv.slots.Count} here");
            }

            if (answer == HostContents.LoadWhole)
            {
                Coop.Guards.Replaying = true;
                try
                {
                    state.loadValues(inv);
                }
                finally
                {
                    Coop.Guards.Replaying = false;
                }

                var items = state.invItems;
                int filled = 0;
                if (items != null)
                {
                    filled = items.Count;
                }

                Plugin.Log.LogInfo(
                    $"[slots] {obj} contents from host ({inv.slots.Count} "
                        + $"slots): {filled} filled, loaded whole");
                RemoveIfEmptied(inv);
                return;
            }

            Merge(inv, state, Watched(inv), $"{obj} contents from host");
        }

        /// <summary>
        /// What OnContents looks at in the host's answer for a container (inv
        /// null when it isn't here).
        /// </summary>
        private AnswerFacts FactsOf(
            Inventory inv, string contents, bool waited)
        {
            if (inv == null)
            {
                return default;
            }

            return new AnswerFacts
            {
                Found = true,
                Empty = contents.Length == 0,
                Watched = _watched.ContainsKey(inv),
                Waited = waited,
                Open = inv.open,
            };
        }

        /// <summary>
        /// Client: the host's whole contents of a container we have open
        /// (ContainerState: a trader's new stock, a corpse's loot, a
        /// roll-back), taken slot by slot as OnContents takes them, so the
        /// window stays open and shows them, and a change of ours not yet sent
        /// is kept or undone as the host would settle it.
        /// </summary>
        internal void MergeOpen(
            Inventory inv, Inventory.SaveState state, string what)
        {
            _watched.TryGetValue(inv, out var seen);
            Merge(inv, state, seen, what);
        }

        /// <summary>
        /// Host: the contents of a storage we have open were just loaded whole;
        /// they are its starting point, so the next Check doesn't send them
        /// back as our player's change. Sends what our player changed before,
        /// first.
        /// </summary>
        internal void FlushBeforeLoad(Inventory inv)
        {
            if (Watching(inv))
            {
                Check(inv);
            }
        }

        internal void Loaded(Inventory inv)
        {
            if (Watching(inv))
            {
                _watched[inv] = Keys(inv);
            }
        }

        /// <summary>
        /// The storage got more slots (WorkbenchStorage.Fit): they start empty,
        /// as they are; our changes to the others still go.
        /// </summary>
        internal void Grew(Inventory inv)
        {
            if (!_watched.TryGetValue(inv, out var keys))
            {
                return;
            }

            _watched[inv] = SlotRules.Grown(keys, Keys(inv));
        }

        private void Merge(
            Inventory inv, Inventory.SaveState state, string[] seen,
            string what)
        {
            int filled = 0;
            if (state.invItems != null)
            {
                filled = state.invItems.Count;
            }

            int slotCount = inv.slots.Count;
            var host = new string[slotCount];
            for (int i = 0; i < slotCount; i++)
            {
                host[i] = "";
            }

            if (state.invItems != null)
            {
                foreach (var st in state.invItems)
                {
                    if (st != null && st.slotId >= 0 && st.slotId < slotCount)
                    {
                        host[st.slotId] = JsonConvert.SerializeObject(st);
                    }
                }
            }

            int differed = 0, undone = 0;
            for (int i = 0; i < slotCount; i++)
            {
                var slot = FactsOf(inv, i, seen) with
                {
                    HostKey = Key(Item(host[i])),
                };

                var step = SlotRules.Merge(slot);
                if (step == SlotStep.Take)
                {
                    // Set makes it the slot's starting point
                    Set(inv, i, host[i]);
                    differed++;
                    continue;
                }

                if (step != SlotStep.TakeAndUndo)
                {
                    // unchanged and as the host's, or we changed it from what
                    // the host has too: it goes at the next Check
                    continue;
                }

                // We changed a slot that had already changed on the host: the
                // host would refuse it, so undo it now.
                string oldKey = seen[i];
                var tried = Item(Json(inv, i));
                Set(inv, i, host[i]);
                Plugin.Log.LogInfo(
                    $"[authority] our change to slot {i} of {inv.name}: "
                        + "refused here (it had changed on the host before we "
                        + "opened it)");
                Undo(oldKey, tried);
                differed++;
                undone++;
            }

            Plugin.Log.LogInfo(
                $"[slots] {what} ({slotCount} slots): {filled} filled, "
                    + $"{differed} differed here, {undone} of our changes "
                    + "undone");
            if (differed > 0)
            {
                UiRefresh.Changed(inv);
            }

            RemoveIfEmptied(inv);
        }

        /// <summary>
        /// A container that removes itself once emptied went before we last
        /// looked: its slots are empty now.
        /// </summary>
        private void SendEmptied(ObjRef r, string[] before)
        {
            var changed = new List<SlotChange>();
            foreach (int i in SlotRules.Emptied(before))
            {
                changed.Add(new SlotChange
                {
                    Slot = i,
                    Old = before[i],
                    New = ""
                });
            }

            if (changed.Count == 0)
            {
                return;
            }

            SendChanges(r, changed);
            if (_session.IsClient)
            {
                RememberSent(r, changed);
            }

            if (WorldActions.Debug)
            {
                Plugin.Log.LogInfo(
                    $"[world] sent {r} emptied ({changed.Count} slot(s))");
            }
        }

        /// <summary>
        /// One that goes when emptied goes once it's empty: a death bag
        /// (Inventory.removeWhenEmpty), and a dropped item, which isn't marked
        /// so (the game destroys it itself in Item.getDroppedItem).
        /// </summary>
        private static void RemoveIfEmptied(Inventory inv)
        {
            if (inv == null)
            {
                return;
            }

            var item = inv.GetComponent<Item>();
            bool dropped = item != null && item.isDroppedItem;
            int items = inv.getAllItems().Count;
            if (!SlotRules.Removes(items, inv.removeWhenEmpty, dropped))
            {
                return;
            }

            if (inv.open)
            {
                inv.hide();
            }

            if (inv == null)
            {
                return;
            }

            if (WorldActions.Debug)
            {
                Plugin.Log.LogInfo($"[world] {inv.name} emptied: removed");
            }

            Object.Destroy(inv.gameObject);
        }

        public bool Watching(Inventory inv) => _watched.ContainsKey(inv);

        // ---- slots ----------------------------------------------------------

        private static string Key(InvSlot s) => Key(s.invItem);

        private static string Key(InvItemClass it) =>
            SlotKeyRules.Of(FactsOf(it));

        /// <summary>
        /// What the slot rules look at in an item (SlotItem; the zero value
        /// when the slot is empty).
        /// </summary>
        private static SlotItem FactsOf(InvItemClass it)
        {
            if (InvItemClass.isNull(it))
            {
                return default;
            }

            int upgrades = 0;
            if (it.upgrades != null)
            {
                upgrades = it.upgrades.Count;
            }

            var item = new SlotItem
            {
                Filled = true,
                Type = it.type,
                Amount = it.amount,
                Durability = it.durability,
                Recipe = it.isRecipe,
                Upgrades = upgrades,
            };

            var basic = it.baseClass;
            if (basic == null)
            {
                return item;
            }

            return item with
            {
                HasBase = true,
                Rots = basic.rottenItem != null,
                Regenerates = basic.regeneratesWhenInactive,
                Drain = basic.durabilityDrain,
                MaxDurability = basic.maxDurability,
            };
        }

        /// <summary>
        /// What the slot rules look at in slot i here against seen, the keys
        /// of our last look (null when not watched). Seen is false for a slot
        /// past either; HostKey is left for the caller.
        /// </summary>
        private static SlotFacts FactsOf(Inventory inv, int i, string[] seen)
        {
            if (i >= inv.slots.Count)
            {
                return default;
            }

            var here = inv.slots[i].invItem;
            var slot = new SlotFacts
            {
                NowKey = Key(here),
                Here = FactsOf(here),
            };

            if (seen == null || i >= seen.Length)
            {
                return slot;
            }

            return slot with
            {
                Seen = true,
                SeenKey = seen[i],
            };
        }

        /// The keys of our last look at a container, or null.
        private string[] Watched(Inventory inv)
        {
            _watched.TryGetValue(inv, out var keys);
            return keys;
        }

        /// <summary>
        /// Host: start the rot of perishables in a container a client opened,
        /// as drawing its slots here would.
        /// </summary>
        private static void StartRot(Inventory inv)
        {
            var ctl = Singleton<Controller>.Instance;
            if (ctl == null)
            {
                return;
            }

            bool any = false;
            foreach (var slot in inv.slots)
            {
                InvItemClass it = null;
                if (slot != null)
                {
                    it = slot.invItem;
                }

                if (InvItemClass.isNull(it) || it.baseClass == null
                    || it.baseClass.rottenItem == null)
                {
                    continue;
                }

                any = true;
                if (it.timeSeen != 0)
                {
                    continue;
                }

                it.timeSeen = ctl.totalTime;
                if (!it.activated)
                {
                    // what checkForActiveSwitches does for a seen perishable in
                    // a container
                    it.switchActive(true);
                }
            }

            if (any)
            {
                inv.RefreshingItems = true;
            }
        }

        /// <summary>
        /// Keys of a watched storage just before the world's own refresh
        /// (Inventory.refreshItems), or null.
        /// </summary>
        internal string[] BeforeDrift(Inventory inv)
        {
            if (inv != null && _watched.ContainsKey(inv))
            {
                return Keys(inv);
            }

            return null;
        }

        /// <summary>
        /// After the world's own refresh: a slot that changed in it (rot,
        /// drain, recharge) and that the player hadn't changed since the last
        /// Check takes its new key as the starting point, so it isn't sent.
        /// </summary>
        internal void Drift(Inventory inv, string[] before)
        {
            if (before == null || inv == null
                || !_watched.TryGetValue(inv, out var keys))
            {
                return;
            }

            // in place: a Merge under way may hold this array as what it saw
            var kept = SlotRules.Drift(keys, before, Keys(inv));
            System.Array.Copy(kept, keys, keys.Length);
        }

        private static string[] Keys(Inventory inv)
        {
            var k = new string[inv.slots.Count];
            for (int i = 0; i < k.Length; i++)
            {
                k[i] = Key(inv.slots[i]);
            }

            return k;
        }

        private static string Json(Inventory inv, int i)
        {
            var it = inv.slots[i].invItem;
            if (InvItemClass.isNull(it))
            {
                return "";
            }

            return JsonConvert.SerializeObject(
                new InvItemClass.SaveState(it, i));
        }

        private static InvItemClass Item(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return null;
            }

            var st =
                JsonConvert.DeserializeObject<InvItemClass.SaveState>(json);
            if (st == null)
            {
                return null;
            }

            var it = new InvItemClass();
            st.loadValues(it);
            return it;
        }

        /// <summary>
        /// Make slot i hold this (JSON, "" for empty), and remember it as the
        /// slot's state.
        /// </summary>
        private void Set(Inventory inv, int i, string json)
        {
            if (i < 0 || i >= inv.slots.Count)
            {
                return;
            }

            Fill(inv, i, json);
            if (_watched.TryGetValue(inv, out var keys) && i < keys.Length)
            {
                keys[i] = Key(inv.slots[i]);
            }
        }

        /// <summary>
        /// Make slot i (in range) hold this (JSON, "" for empty). Needs no
        /// session: Sort &amp; Stack played alone uses it too.
        /// </summary>
        private static void Fill(Inventory inv, int i, string json)
        {
            var slot = inv.slots[i];
            slot.inventory = inv;
            // Our player holds an item on the cursor that came from this slot,
            // which someone else has filled now: the game would put the held
            // item back there when it's let go (Controller.placeItem), over
            // theirs. It goes into our bags instead (it's ours: the slot gave
            // it up when we picked it up).
            var ctl = Singleton<Controller>.Instance;
            InvItemClass held = null;
            if (ctl != null)
            {
                held = ctl.pickedUpItem;
            }

            var player = Player.Instance;
            bool holding = !InvItemClass.isNull(held);
            var facts = new HeldFacts
            {
                Filling = !string.IsNullOrEmpty(json),
                Holding = holding,
                FromThisSlot = holding && held.slot == slot,
                PlayerHere = player != null,
            };

            if (SlotRules.HeldGoesToBags(facts))
            {
                var keep = new InvItemClass(held);
                UIInvItem uiInvItem = held.UIInvItem;
                if (uiInvItem != null)
                {
                    uiInvItem.despawn();
                }

                ctl.pickedUpItem = null;
                int dropped = GiveBack(player, keep, keep.amount);
                Plugin.Log.LogInfo(
                    $"[slots] the {keep.type} we held came from slot {i} of "
                        + $"{inv.name}, filled by someone else: into our bags "
                        + $"({dropped} at our feet)");
            }

            Coop.Guards.Replaying = true;
            try
            {
                slot.removeItem();
                var it = Item(json);
                // A gun's saved amount is its loaded rounds (it.ammo); its
                // amount stays the 1 the copy gives it, not 0.
                if (it != null)
                {
                    bool ammo = it.baseClass != null && it.baseClass.hasAmmo;
                    slot.createItem(
                        it, SlotRules.CreateAmount(ammo, it.amount));
                }
            }
            finally
            {
                Coop.Guards.Replaying = false;
            }
        }

        // ---- messages -------------------------------------------------------

        // Client: the batches of slot changes we sent lately, to undo one whole
        // when the host refuses it.
        private readonly SentBatches _sent = new SentBatches();

        private void RememberSent(ObjRef obj, List<SlotChange> changes)
        {
            _sent.Remember(
                obj.ToString(), changes, new Seconds(Time.unscaledTime));
        }

        private void OnSlots(ulong from, PacketReader r)
        {
            var s = _session;
            if (!WorldShare.InWorld)
            {
                return;
            }

            if (s.IsClient && from != s.HostId)
            {
                return;
            }

            var obj = ObjRef.RefOf(
                ContainerSlotsMessage.Read(r, out var changes));

            var inv = ObjRef.Resolve<Inventory>(obj);
            if (WorldActions.Debug)
            {
                string name = "not found here";
                if (inv != null)
                {
                    name = inv.name;
                }

                Plugin.Log.LogInfo(
                    $"[world] got {changes.Count} slot(s) of {obj}: " + name);
            }

            if (inv == null)
            {
                return;
            }

            if (s.IsClient) // the host's word
            {
                foreach (var c in changes)
                {
                    TakeHost(inv, c);
                }

                UiRefresh.Changed(inv);
                RemoveIfEmptied(inv);
                return;
            }

            // Host: the player must be at the container, it must be open in the
            // host's world (a locked crate not picked open here isn't), and
            // nobody else may have it open (but its storage). The changes
            // stand if every slot still holds what the player saw there; if
            // one doesn't, none does. A batch is what one look at the
            // container found changed: an item moved from one slot to another
            // (a cursor drag inside it) is two changes, and taking only one of
            // them would double or lose that item. A stack bigger than the
            // host's stack size (StackSizes) isn't one the client could have
            // made.
            changes.RemoveAll(c => !SlotRules.InRange(c.Slot, inv.slots.Count));
            var batch = FactsOf(from, inv, obj) with
            {
                Changes = FactsOf(inv, changes),
            };

            var verdict = SlotRules.MayChange(batch);
            string refusedAll = null;
            if (verdict.Refused)
            {
                refusedAll = Refusals.Text(verdict);
            }

            var accepted = new List<SlotChange>();
            foreach (var c in changes)
            {
                if (refusedAll == null)
                {
                    Set(inv, c.Slot, c.New);
                    accepted.Add(c);
                    continue;
                }

                Plugin.Log.LogInfo(
                    $"[authority] {s.NameOf(from)} changed slot {c.Slot} of "
                        + $"{inv.name}: refused ({refusedAll})");
                var w = s.Begin(MsgType.ContainerSlotRefused);
                ContainerSlotRefusedMessage.Write(
                    w,
                    ObjRef.NameOf(obj),
                    new RefusedSlot
                    {
                        Slot = c.Slot,
                        Held = Json(inv, c.Slot),
                        Old = c.Old,
                        Tried = c.New,
                    });

                s.SendTo(from, w, Channel.Control, reliable: true);
            }

            if (accepted.Count == 0)
            {
                return;
            }

            UiRefresh.Changed(inv);
            SendChanges(obj, accepted, from);
            RemoveIfEmptied(inv);
        }

        /// <summary>
        /// What the host looks at in a container a client changed; Changes is
        /// left for the caller.
        /// </summary>
        private SlotBatchFacts FactsOf(ulong from, Inventory inv, ObjRef obj)
        {
            string holder = null;
            CoopSession coop = Coop.Current;
            bool held = coop != null
                && coop.Containers.LockedByOther(obj, from, out holder);
            return new SlotBatchFacts
            {
                Container = Containers.MayChange(from, inv),
                Storage = Is(inv),
                HeldByOther = held,
                Holder = holder,
                SlotCount = inv.slots.Count,
            };
        }

        /// <summary>
        /// What the host looks at in each change of a client's batch (all
        /// within the container's slots).
        /// </summary>
        private static List<SlotChangeFacts> FactsOf(
            Inventory inv, List<SlotChange> changes)
        {
            var facts = new List<SlotChangeFacts>(changes.Count);
            foreach (var c in changes)
            {
                var here = inv.slots[c.Slot].invItem;
                facts.Add(new SlotChangeFacts
                {
                    Slot = c.Slot,
                    Old = c.Old,
                    HostKey = Key(here),
                    Host = FactsOf(here),
                    Fits = StackSizes.Fits(Item(c.New)),
                });
            }

            return facts;
        }

        /// <summary>
        /// Client: another player's change to a slot, as the host took it. A
        /// change of ours to that slot not yet sent stands if the slot now
        /// holds what we changed it from (it goes at the next Check); otherwise
        /// the host would refuse it, so it's undone now, as Merge does, and Set
        /// doesn't double or lose what it moved.
        /// </summary>
        private void TakeHost(Inventory inv, SlotChange c)
        {
            var slot = FactsOf(inv, c.Slot, Watched(inv)) with
            {
                HostKey = Key(Item(c.New)),
            };

            var step = SlotRules.TakeHost(slot);
            if (step == SlotStep.Take)
            {
                Set(inv, c.Slot, c.New);
                return;
            }

            if (step != SlotStep.TakeAndUndo)
            {
                return;
            }

            string oldKey = slot.SeenKey;
            var tried = Item(Json(inv, c.Slot));
            Set(inv, c.Slot, c.New);
            Plugin.Log.LogInfo(
                $"[authority] our change to slot {c.Slot} of {inv.name}: "
                    + "refused here (another player changed it first)");
            Undo(oldKey, tried);
        }

        /// <summary>
        /// Client: the host refused a slot change; take the host's slot, and
        /// undo what the change moved.
        /// </summary>
        private void OnRefused(ulong from, PacketReader r)
        {
            var s = _session;
            if (!s.IsClient || from != s.HostId || !WorldShare.InWorld)
            {
                return;
            }

            var obj = ObjRef.RefOf(
                ContainerSlotRefusedMessage.Read(r, out var refused));
            int slot = refused.Slot;
            string hostJson = refused.Held,
                oldKey = refused.Old,
                triedJson = refused.Tried;
            // may be gone here: a dropped item we picked up with a click
            var inv = ObjRef.Resolve<Inventory>(obj);
            Plugin.Log.LogInfo(
                "[slots] "
                    + $"{obj} slot {slot}: the host refused our change "
                    + $"'{oldKey}' -> '{Key(Item(triedJson))}' "
                    + $"(it holds '{Key(Item(hostJson))}' there)");
            // Something we changed in that slot after this change and haven't
            // sent yet: Set replaces it too, so it's undone as well. (A later
            // change already sent is refused, and undone, by its own answer.)
            InvItemClass unsent = null;
            string sentKey = null;
            if (inv != null)
            {
                var here = FactsOf(inv, slot, Watched(inv));
                if (SlotRules.HasUnsent(here))
                {
                    sentKey = here.SeenKey;
                    unsent = Item(Json(inv, slot));
                }
            }

            if (inv != null)
            {
                Set(inv, slot, hostJson);
            }

            // What this change moved: from what we saw (its key) to what we
            // made it. The host refuses a batch whole, so the first refused
            // slot of a batch we sent undoes the whole batch, netted per item
            // type (an item we moved within the container comes to nothing);
            // its other slots' refusals only set the host's contents.
            var batch = _sent.Find(
                obj.ToString(),
                new SlotChange
                {
                    Slot = slot,
                    Old = oldKey,
                    New = triedJson,
                });

            bool undone = false;
            if (batch != null)
            {
                undone = batch.Undone;
            }

            var undo = SlotRules.UndoOnRefusal(batch != null, undone);
            if (undo == RefusalUndo.Alone)
            {
                Undo(oldKey, Item(triedJson));
            }
            else if (undo == RefusalUndo.Batch)
            {
                batch.Undone = true;
                var moves = new Moves();
                foreach (var c in batch.Changes)
                {
                    moves.Add(c.Old, Item(c.New));
                }

                Undo(moves, quiet: false);
            }

            if (sentKey != null)
            {
                Undo(sentKey, unsent, quiet: true);
            }

            UiRefresh.Changed(inv);
        }

        /// <summary>
        /// Our change made a slot go from oldKey (its key, "" empty) to tried.
        /// The slot now holds the host's contents (Set); undo the rest for our
        /// player: what we put in comes back into our bags or hotbar (at our
        /// feet only when there's no room), what we took goes again (from the
        /// cursor, the bags, the hotbar; never from the open storage, which is
        /// the host's now).
        /// </summary>
        private static void Undo(
            string oldKey, InvItemClass tried, bool quiet = false)
        {
            var moves = new Moves();
            moves.Add(oldKey, tried);
            Undo(moves, quiet);
        }

        /// <summary>
        /// What slot changes moved, per item type (SlotNet), with the first
        /// item tried of each type, to give back.
        /// </summary>
        private sealed class Moves
        {
            public readonly SlotNet Net = new SlotNet();

            public readonly Dictionary<string, InvItemClass> Items =
                new Dictionary<string, InvItemClass>();

            /// A slot went from oldKey (its key, "" empty) to tried.
            public void Add(string oldKey, InvItemClass tried)
            {
                if (InvItemClass.isNull(tried))
                {
                    Net.Add(oldKey, null, 0);
                    return;
                }

                Net.Add(oldKey, tried.type, tried.amount);
                if (!Items.ContainsKey(tried.type))
                {
                    Items[tried.type] = tried;
                }
            }
        }

        /// <summary>
        /// Undo what net says our changes moved: what we put in comes back to
        /// us, what we took goes again.
        /// </summary>
        private static void Undo(Moves moves, bool quiet)
        {
            var player = Player.Instance;
            if (player == null)
            {
                return;
            }

            bool any = false;
            foreach (var kv in moves.Net.Counts)
            {
                int put = SlotNet.Put(kv.Value);
                int took = SlotNet.Took(kv.Value);

                if (put == 0 && took == 0)
                {
                    continue;
                }

                any = true;
                int dropped = 0;
                if (put > 0
                    && moves.Items.TryGetValue(kv.Key, out var item))
                {
                    dropped = GiveBack(player, item, put);
                }

                int left = took;
                if (left > 0)
                {
                    var ctl = Singleton<Controller>.Instance;
                    InvItemClass held = null;
                    if (ctl != null)
                    {
                        held = ctl.pickedUpItem;
                    }

                    if (!InvItemClass.isNull(held) && held.type == kv.Key)
                    {
                        int n = Mathf.Min(held.amount, left);
                        left -= n;
                        if (n >= held.amount)
                        {
                            UIInvItem uiInvItem = held.UIInvItem;
                            if (uiInvItem != null)
                            {
                                uiInvItem.despawn();
                            }

                            ctl.pickedUpItem = null;
                        }
                        else
                        {
                            held.amount -= n;
                            held.refresh();
                        }
                    }

                    if (left > 0)
                    {
                        left -= player.Inventory.removeItemAmountFromPlayer(
                            kv.Key, left, includeAdditionalInventory: false);
                    }
                }

                string data;
                if (put > 0)
                {
                    data =
                        $"[slots] undone: gave back {put} {kv.Key} ({dropped} "
                            + "of them at our feet, no room)";
                }
                else
                {
                    data =
                        $"[slots] undone: took back {took - left} of {took} "
                            + $"{kv.Key}";
                }

                Plugin.Log.LogInfo(data);
            }

            if (any && !quiet)
            {
                player.displayMessage("Someone took that first");
            }
        }

        /// <summary>
        /// Put amount of this item back where it came from: the player's bags,
        /// then the hotbar (onto a stack of it first, then into a free slot).
        /// Not Inventory.addItemTypeToPlayer: with the workbench open that
        /// counts its storage as the player's, and would put it straight back
        /// there. Returns how many had to be dropped.
        /// </summary>
        private static int GiveBack(
            Player player, InvItemClass item, int amount)
        {
            var bags = new[] { player.Inventory, player.Hotbar };
            int left = amount;
            var baseClass = item.baseClass;
            if (baseClass != null && baseClass.stackable)
            {
                foreach (var bag in bags)
                {
                    foreach (var slot in bag.slots)
                    {
                        if (left <= 0)
                        {
                            break;
                        }

                        var it = slot.invItem;
                        if (InvItemClass.isNull(it) || it.type != item.type
                            || it.amount >= it.baseClass.maxAmount)
                        {
                            continue;
                        }

                        int n =
                            Mathf.Min(left, it.baseClass.maxAmount - it.amount);
                        var moved = new InvItemClass(item) { amount = n };
                        it.durability =
                            InvItemClass.getStackedDurability(it, moved);
                        it.amount += n;
                        it.refresh();
                        left -= n;
                    }
                }
            }

            foreach (var bag in bags)
            {
                if (left <= 0)
                {
                    break;
                }

                foreach (var slot in bag.slots)
                {
                    if (!InvItemClass.isNull(slot.invItem))
                    {
                        continue;
                    }

                    if (slot.inventory == null)
                    {
                        slot.inventory = bag;
                    }

                    slot.createItem(item, left);
                    left = 0;
                    break;
                }
            }

            if (left > 0)
            {
                player.spawnDroppedInvItem(new InvItemClass(item)
                {
                    amount = left
                });
            }

            return left;
        }

        // ---- the workbench's level ------------------------------------------

        internal void LevelRaised(int level)
        {
            if (!Coop.Guards.Sharing)
            {
                return;
            }

            var w = _session.Begin(MsgType.WorkbenchLevel);
            WorkbenchLevelMessage.Write(w, level);
            if (_session.IsHost)
            {
                _session.Broadcast(w, Channel.Control, reliable: true);
            }
            else
            {
                _session.SendToHost(w, Channel.Control, reliable: true);
            }

            Plugin.Log.LogInfo($"The workbench is now level {level}");
        }

        private void OnLevel(ulong from, PacketReader r)
        {
            var s = _session;
            if (!WorldShare.InWorld || (s.IsClient && from != s.HostId))
            {
                return;
            }

            int level = WorkbenchLevelMessage.Read(r);
            var ctl = Singleton<Controller>.Instance;
            if (ctl == null)
            {
                return;
            }

            if (!WorkbenchLevelRules.Raises(level, ctl.workbenchLevel))
            {
                return;
            }

            if (s.IsHost)
            {
                // An upgrade (CraftingRecipes.doCraft) raises the level by one,
                // at an open workbench.
                var raise = new LevelFacts
                {
                    Level = level,
                    Current = ctl.workbenchLevel,
                    AtWorkbench = AtWorkbench(from),
                };

                var verdict = WorkbenchLevelRules.MayRaise(raise);
                if (verdict.Refused)
                {
                    Plugin.Log.LogInfo(
                        $"[authority] {s.NameOf(from)} raised the workbench "
                            + $"level: refused ({Refusals.Text(verdict)})");
                    return;
                }

                var w = s.Begin(MsgType.WorkbenchLevel);
                WorkbenchLevelMessage.Write(w, level);
                s.Broadcast(w, Channel.Control, reliable: true, except: from);
            }

            ctl.workbenchLevel = level;
            Plugin.Log.LogInfo(
                $"The workbench is now level {level} ({s.NameOf(from)} "
                    + "upgraded it)");
            // its new row, before any change to it arrives
            Plugin.Instance.Host.Workbenches.ApplyAll("upgraded");
            // An open workbench shows the new level's recipes.
            Inventory open = null;
            if (Player.Instance != null)
            {
                open = Player.Instance.openedItemInventory;
            }

            if (open != null && open.isWorkbench && open.workbench != null)
            {
                open.workbench.workbenchInventory.clear();
                open.workbench.setRecipes();
                // its "Level N" label and the next upgrade
                open.workbench.refreshWorkbenchUpgrade();
            }
        }

        private static bool AtWorkbench(ulong player)
        {
            foreach (var wb in Object.FindObjectsOfType<Workbench>(true))
            {
                if (Coop.Current.Authority.Near(
                    player, wb.transform.position, WorldActions.Reach))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// The workbench's level before a craft (DoCraft), 0 with no world.
        /// </summary>
        internal static int LevelNow()
        {
            var ctl = Singleton<Controller>.Instance;
            if (ctl == null)
            {
                return 0;
            }

            return ctl.workbenchLevel;
        }

        /// <summary>
        /// A craft is done (DoCraft): if it was an upgrade, the storage gets
        /// its new row (also without co-op), and the new level is sent.
        /// </summary>
        internal static void Crafted(int before)
        {
            var ctl = Singleton<Controller>.Instance;
            if (ctl == null || ctl.workbenchLevel <= before)
            {
                return;
            }

            Plugin.Instance.Host.Workbenches.ApplyAll("upgraded");
            if (Coop.Guards.Replaying)
            {
                return;
            }

            CoopSession coop = Coop.Current;
            if (coop != null)
            {
                coop.Slots.LevelRaised(ctl.workbenchLevel);
            }
        }

        // ---- click pickups --------------------------------------------------

        /// <summary>
        /// A dropped item is about to be picked up with a click
        /// (GetDroppedItem), which moves its slot to the player and destroys
        /// the object without opening its window, so Containers never sees
        /// it. Its slot is looked at before and after, as if the window had
        /// been open for that moment: the change goes out slot by slot (a
        /// client's is the host's to accept), and the others' copy goes once
        /// it's empty. Returns the container to look at after, or null.
        /// </summary>
        internal Inventory PickingUp(Inventory inv)
        {
            if (inv == null || !Coop.Guards.Sharing || Watching(inv))
            {
                return null;
            }

            if (WorldActions.OursAlone(inv.gameObject))
            {
                return null; // the host hasn't got it (Containers.Poll)
            }

            // as it is now (over within this frame: no waiting for the host)
            Check(inv, askHost: false);
            return inv;
        }

        /// <summary>
        /// The click pickup PickingUp looked at is done.
        /// </summary>
        internal void PickedUp(Inventory inv)
        {
            if (inv == null)
            {
                return;
            }

            if (WorldActions.Debug)
            {
                Plugin.Log.LogInfo(
                    $"[world] picked up {inv.name} with a click");
            }

            Closed(inv);
        }
    }
}
