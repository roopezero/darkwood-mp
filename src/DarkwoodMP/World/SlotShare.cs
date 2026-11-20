using System.Collections.Generic;
using DarkwoodMP.Combat;
using DarkwoodMP.Net;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Containers changed slot by slot, with the host deciding: the workbench's storage, used by several players at
    /// once, and every container a client changes (the host's own changes go out whole: Containers). The host
    /// settles who got there first, and refuses a change from a player who isn't at the container, to one that is
    /// still locked in the host's world (a crate not picked open there: Containers.LockedReason), or while
    /// someone else has it open.
    ///
    /// While a player has the storage open, its slots are compared a few times a second with how they were.
    /// Each slot that changed goes out as ContainerSlots: "slot i was A, is now B". The host makes the change in
    /// its world only if slot i is still A there, and passes it on; the host's own changes go straight out. If
    /// slot i had already changed (two players took the same stack at the same moment), the host refuses
    /// (ContainerSlotRefused, with what slot i holds): that player's slot is set to the host's, and what their
    /// change moved is undone for them. Items they put in go back into their bags or hotbar (at their feet only
    /// if there's no room); items they took are taken back from what they carry. Nothing is lost or doubled.
    ///
    /// A client's "slot i was A" must be what the host has, or every change is refused: its copy of the world is
    /// the host's save from when it joined, and changes the host made while it downloaded and loaded it never
    /// reached it. So a client asks the host what a container holds as it opens it (ContainerSlotsAsk), and sends
    /// no change before the answer (ContainerSlotsAll) has become its starting point; and asks for every
    /// workbench's storage once its world has loaded.
    ///
    /// A slot is compared by its item's type, amount, durability, recipe and upgrades; its contents travel as the
    /// game saves them (InvItemClass.SaveState, as JSON; "" for an empty slot).
    ///
    /// Food rots, and some items drain or recharge, by themselves while their container is on the game's
    /// refreshing list (Inventory.refreshItems), which each game runs on its own. Those durability changes are the
    /// world's, not the player's: they become the slot's starting point without being sent (Drift). The host
    /// accepts a change to a slot whose only difference from what the client saw is such an item's durability
    /// (RotOnly), and starts the rot in its own copy when a client opens the container (OnAsk), as drawing the
    /// slots would. A client takes the host's durability each time it opens the container.
    ///
    /// The workbench's level is shared too (WorkbenchLevel): an upgrade made at anyone's workbench raises it for
    /// everyone. The highest level wins.
    /// </summary>
    public sealed partial class SlotShare
    {
        public static SlotShare Instance { get; private set; }
        private readonly Session _session;

        // what each watched storage's slots held when last compared (their keys)
        private readonly Dictionary<Inventory, string[]> _watched = new Dictionary<Inventory, string[]>();
        private readonly Dictionary<Inventory, ObjRef> _refs = new Dictionary<Inventory, ObjRef>();
        private readonly Dictionary<Inventory, bool> _isStorage = new Dictionary<Inventory, bool>();

        public SlotShare(Session session)
        {
            Instance = this;
            _session = session;
            session.Register(MsgType.ContainerSlots, OnSlots);
            session.Register(MsgType.ContainerSlotRefused, OnRefused);
            session.Register(MsgType.WorkbenchLevel, OnLevel);
            session.Register(MsgType.ContainerSlotsAsk, OnAsk);
            session.Register(MsgType.ContainerSlotsAll, OnContents);
            session.Register(MsgType.StorageSort, OnSort);
            session.OnSessionEnded += () =>
            {
                _watched.Clear();
                _refs.Clear();
                _isStorage.Clear();
                _awaiting.Clear();
                _askedScene = -1;
                _sent.Clear();
            };
        }

        /// <summary>Whether this container is a workbench's storage (shared slot by slot).</summary>
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

        // ---- watching ------------------------------------------------------------------------

        /// <summary>
        /// Called a few times a second with each storage the local player has open. The first call records the
        /// slots as they are here; on a client it also asks the host what they hold there (ContainerSlotsAsk), and
        /// no change is sent until that answer has become the starting point (OnContents), or AskTimeout went by.
        /// A click pickup (askHost false) is over within one frame, so it doesn't wait.
        /// </summary>
        public void Check(Inventory inv, bool askHost = true)
        {
            if (inv == null)
            {
                return;
            }
            var keys = Keys(inv);
            if (!_watched.TryGetValue(inv, out var before) || before.Length != keys.Length)
            {
                _watched[inv] = keys; // as we found it
                var found = ObjRef.Of(inv.gameObject);
                _refs[inv] = found;
                if (askHost && _session.IsClient && found.Valid)
                {
                    Ask(inv, found);
                }
                return;
            }
            if (_awaiting.TryGetValue(inv, out float asked))
            {
                if (Time.unscaledTime - asked < AskTimeout)
                {
                    return; // what we change meanwhile goes once the host's answer is in
                }
                _awaiting.Remove(inv);
                Plugin.Log.LogWarning($"[slots] {_refs[inv]} no contents from host in {AskTimeout:0} s; sending changes against what we saw");
            }
            var changed = new List<Change>();
            for (int i = 0; i < keys.Length; i++)
            {
                if (keys[i] != before[i])
                {
                    changed.Add(new Change { Slot = i, Old = before[i], New = Json(inv, i) });
                }
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
                Plugin.Log.LogInfo($"[world] sent {changed.Count} slot(s) of {inv.name} {r}: {string.Join(", ", changed.ConvertAll(c => $"{c.Slot}: '{c.Old}' -> '{keys[c.Slot]}'"))}");
            }
            _watched[inv] = keys;
        }

        /// <summary>The local player closed it: one last look, then stop watching.</summary>
        public void Closed(Inventory inv)
        {
            _awaiting.Remove(inv); // closed before the host answered: what we changed goes against what we saw
            if (inv != null)
            {
                Check(inv);
            }
            // destroyed (a Unity object that is == null but still a key here): its slots went empty
            else if (_watched.TryGetValue(inv, out var before) && _refs.TryGetValue(inv, out var r) && r.Valid)
            {
                SendEmptied(r, before);
            }
            _watched.Remove(inv);
            _refs.Remove(inv);
        }

        // ---- the host's contents as the starting point ---------------------------------------

        /// <summary>How long a client waits for the host's contents of something it opened before sending its changes anyway.</summary>
        public const float AskTimeout = 3f;

        // client: what we have open and asked the host about, and when
        private readonly Dictionary<Inventory, float> _awaiting = new Dictionary<Inventory, float>();
        private int _askedScene = -1;

        private void Ask(Inventory inv, ObjRef r)
        {
            if (inv != null)
            {
                _awaiting[inv] = Time.unscaledTime;
            }
            var w = _session.Begin(MsgType.ContainerSlotsAsk);
            r.Write(w);
            _session.SendToHost(w, Channel.Control, reliable: true);
            if (WorldActions.Debug)
            {
                Plugin.Log.LogInfo($"[slots] {r} asked the host for its contents");
            }
        }

        /// <summary>
        /// Client, every frame: once our world has loaded (again), ask the host what every workbench's storage holds.
        /// Our world is the host's save as it was when we were offered it; what the host put in or took out while we
        /// downloaded and loaded it came to us before we were in the world, and was dropped.
        /// </summary>
        public void Update()
        {
            if (!_session.IsClient || !WorldShare.WorldReady)
            {
                return;
            }
            int scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle;
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

        /// <summary>Host: a client wants what a container holds here (ContainerSlotsAll; "" when we haven't got it).</summary>
        private void OnAsk(ulong from, PacketReader r)
        {
            var s = _session;
            if (!s.IsHost || !WorldShare.InWorld)
            {
                return;
            }
            var obj = ObjRef.Read(r);
            var inv = ObjRef.Resolve<Inventory>(obj);
            if (inv != null)
            {
                StartRot(inv);
            }
            var w = s.Begin(MsgType.ContainerSlotsAll);
            obj.Write(w);
            string s2 = "";
            if (inv != null)
            {
                s2 = JsonConvert.SerializeObject(new Inventory.SaveState(inv));
            }
            w.WriteBigStr(s2);
            s.SendTo(from, w, Channel.Control, reliable: true);
            if (WorldActions.Debug)
            {
                Plugin.Log.LogInfo($"[slots] {obj} contents sent to {s.NameOf(from)}: " + (inv != null ? inv.name : "not found here"));
            }
        }

        /// <summary>
        /// Client: what a container holds on the host. Closed, it's loaded as it is. Open, each slot we haven't
        /// changed since we opened it takes the host's contents; one we have changed stays ours if the host's slot
        /// holds what we saw there (the change goes at the next Check), and is undone here otherwise, as the host
        /// would refuse it. From then on our changes are compared with the host's slots.
        /// </summary>
        private void OnContents(ulong from, PacketReader r)
        {
            var s = _session;
            if (!s.IsClient || from != s.HostId || !WorldShare.InWorld)
            {
                return;
            }
            var obj = ObjRef.Read(r);
            string contents = r.ReadBigStr();
            var inv = ObjRef.Resolve<Inventory>(obj);
            bool waited = inv != null && _awaiting.Remove(inv);
            if (inv == null || contents.Length == 0)
            {
                Plugin.Log.LogInfo($"[slots] {obj} contents from host: " + (inv == null ? "not found here" : "the host hasn't got it"));
                return;
            }
            // Open and already sending changes (we stopped waiting, or this answers an earlier ask): the host's
            // answer is older than what we sent since, which the host settles slot by slot.
            if (_watched.ContainsKey(inv) && !waited)
            {
                Plugin.Log.LogInfo($"[slots] {obj} contents from host: not used, our changes to it already went");
                return;
            }
            Inventory.SaveState state;
            try
            {
                state = JsonConvert.DeserializeObject<Inventory.SaveState>(contents);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning($"[slots] {obj} bad contents from host: {e.Message}");
                return;
            }
            if (state == null)
            {
                return;
            }
            if (state.slotCount > inv.slots.Count && Is(inv))
            {
                WorkbenchStorage.Fit(inv, state.slotCount); // the host's rows (an upgrade)
            }
            if (state.slotCount != inv.slots.Count)
            {
                Plugin.Log.LogWarning($"[slots] {obj} has {state.slotCount} slots on the host, {inv.slots.Count} here");
            }

            if (!_watched.TryGetValue(inv, out var seen) && !inv.open)
            {
                WorldActions.Replaying = true;
                try
                {
                    state.loadValues(inv);
                }
                finally
                {
                    WorldActions.Replaying = false;
                }
                Plugin.Log.LogInfo($"[slots] {obj} contents from host ({inv.slots.Count} slots): {state.invItems?.Count ?? 0} filled, loaded whole");
                RemoveIfEmptied(inv);
                return;
            }

            Merge(inv, state, seen, $"{obj} contents from host");
        }

        /// <summary>
        /// Client: the host's whole contents of a container we have open (ContainerState: a trader's new stock, a
        /// corpse's loot, a roll-back), taken slot by slot as OnContents takes them, so the window stays open and
        /// shows them, and a change of ours not yet sent is kept or undone as the host would settle it.
        /// </summary>
        internal void MergeOpen(Inventory inv, Inventory.SaveState state, string what)
        {
            _watched.TryGetValue(inv, out var seen);
            Merge(inv, state, seen, what);
        }

        /// <summary>
        /// Host: the contents of a storage we have open were just loaded whole; they are its starting point, so the
        /// next Check doesn't send them back as our player's change. Sends what our player changed before, first.
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

        /// <summary>The storage got more slots (WorkbenchStorage.Fit): they start empty, as they are; our changes to the others still go.</summary>
        internal void Grew(Inventory inv)
        {
            if (!_watched.TryGetValue(inv, out var keys) || keys.Length >= inv.slots.Count)
            {
                return;
            }
            var grown = new string[inv.slots.Count];
            for (int i = 0; i < grown.Length; i++)
            {
                if (i < keys.Length)
                {
                    grown[i] = keys[i];
                }
                else
                {
                    grown[i] = Key(inv.slots[i]);
                }
            }
            _watched[inv] = grown;
        }

        private void Merge(Inventory inv, Inventory.SaveState state, string[] seen, string what)
        {
            int filled = state.invItems != null ? state.invItems.Count : 0, slotCount = inv.slots.Count;
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
                string hostKey = Key(Item(host[i]));
                string now = Key(inv.slots[i]);
                if (seen == null || i >= seen.Length || now == seen[i])
                {
                    if (now != hostKey) // Set makes it the slot's starting point
                    {
                        Set(inv, i, host[i]);
                        differed++;
                    }
                    continue;
                }
                if (hostKey == seen[i] || RotOnly(inv.slots[i].invItem, hostKey, seen[i]))
                {
                    continue; // we changed it, from what the host has too: it goes at the next Check
                }
                // We changed a slot that had already changed on the host: the host would refuse it, so undo it now.
                string oldKey = seen[i];
                var tried = Item(Json(inv, i));
                Set(inv, i, host[i]);
                Plugin.Log.LogInfo($"[authority] our change to slot {i} of {inv.name}: refused here (it had changed on the host before we opened it)");
                Undo(oldKey, tried);
                differed++;
                undone++;
            }
            Plugin.Log.LogInfo($"[slots] {what} ({slotCount} slots): {filled} filled, {differed} differed here, {undone} of our changes undone");
            if (differed > 0)
            {
                UiRefresh.Changed(inv);
            }
            RemoveIfEmptied(inv);
        }

        /// <summary>A container that removes itself once emptied went before we last looked: its slots are empty now.</summary>
        private void SendEmptied(ObjRef r, string[] before)
        {
            var changed = new List<Change>();
            for (int i = 0; i < before.Length; i++)
            {
                if (before[i].Length > 0)
                {
                    changed.Add(new Change { Slot = i, Old = before[i], New = "" });
                }
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
                Plugin.Log.LogInfo($"[world] sent {r} emptied ({changed.Count} slot(s))");
            }
        }

        /// <summary>
        /// One that goes when emptied goes once it's empty: a death bag (Inventory.removeWhenEmpty), and a dropped
        /// item, which isn't marked so (the game destroys it itself in Item.getDroppedItem).
        /// </summary>
        private static void RemoveIfEmptied(Inventory inv)
        {
            if (inv == null || inv.getAllItems().Count > 0)
            {
                return;
            }
            var item = inv.GetComponent<Item>();
            if (!inv.removeWhenEmpty && (item == null || !item.isDroppedItem))
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

        // ---- slots -------------------------------------------------------------------------

        private static string Key(InvSlot s) => Key(s.invItem);

        private static string Key(InvItemClass it)
        {
            if (InvItemClass.isNull(it))
            {
                return "";
            }
            int text = 0;
            if (it.isRecipe)
            {
                text = 1;
            }
            List<ItemUpgrade> upgrades = it.upgrades;
            int count = 0;
            if (upgrades != null)
            {
                count = upgrades.Count;
            }
            return $"{it.type}|{it.amount}|{Mathf.RoundToInt(it.durability * 1000f)}|{text}|{count}";
        }

        /// <summary>
        /// Items whose durability the world changes by itself in a container: food that rots, and items that
        /// drain while switched on or recharge while off.
        /// </summary>
        private static bool Drifts(InvItemClass it) =>
            !InvItemClass.isNull(it) && it.baseClass != null
            && (it.baseClass.rottenItem != null || it.baseClass.regeneratesWhenInactive || it.baseClass.durabilityDrain > 0f);

        /// <summary>How far two games' durabilities of one drifting item may be apart and still count as the same item (of its maximum).</summary>
        public const float DriftTolerance = 0.25f;

        /// <summary>
        /// Keys a and b name the same item but for its durability, by less than DriftTolerance of its maximum, and
        /// the item (here) is one whose durability the world changes by itself.
        /// </summary>
        private static bool RotOnly(InvItemClass here, string a, string b)
        {
            if (a == b)
            {
                return true;
            }
            if (!Drifts(here) || string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
            {
                return false;
            }
            var x = a.Split('|');
            var y = b.Split('|');
            if (x.Length != 5 || y.Length != 5)
            {
                return false;
            }
            if (x[0] != y[0] || x[1] != y[1] || x[3] != y[3] || x[4] != y[4])
            {
                return false;
            }
            if (!int.TryParse(x[2], out int dx) || !int.TryParse(y[2], out int dy))
            {
                return false;
            }
            float max = Mathf.Max(1f, here.baseClass.maxDurability) * 1000f;
            return Mathf.Abs(dx - dy) <= DriftTolerance * max;
        }

        /// <summary>Host: start the rot of perishables in a container a client opened, as drawing its slots here would.</summary>
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
                if (InvItemClass.isNull(it) || it.baseClass == null || it.baseClass.rottenItem == null)
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
                    it.switchActive(true); // what checkForActiveSwitches does for a seen perishable in a container
                }
            }
            if (any)
            {
                inv.RefreshingItems = true;
            }
        }

        /// <summary>Keys of a watched storage just before the world's own refresh (Inventory.refreshItems), or null.</summary>
        internal string[] BeforeDrift(Inventory inv)
        {
            if (inv != null && _watched.ContainsKey(inv))
            {
                return Keys(inv);
            }
            return null;
        }

        /// <summary>
        /// After the world's own refresh: a slot that changed in it (rot, drain, recharge) and that the player
        /// hadn't changed since the last Check takes its new key as the starting point, so it isn't sent.
        /// </summary>
        internal void Drift(Inventory inv, string[] before)
        {
            if (before == null || inv == null || !_watched.TryGetValue(inv, out var keys))
            {
                return;
            }
            for (int i = 0; i < keys.Length && i < before.Length && i < inv.slots.Count; i++)
            {
                string now = Key(inv.slots[i]);
                if (now != before[i] && keys[i] == before[i])
                {
                    keys[i] = now;
                }
            }
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
            return JsonConvert.SerializeObject(new InvItemClass.SaveState(it, i));
        }

        private static InvItemClass Item(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return null;
            }
            var st = JsonConvert.DeserializeObject<InvItemClass.SaveState>(json);
            if (st == null)
            {
                return null;
            }
            var it = new InvItemClass();
            st.loadValues(it);
            return it;
        }

        /// <summary>Make slot i hold this (JSON, "" for empty), and remember it as the slot's state.</summary>
        private void Set(Inventory inv, int i, string json)
        {
            if (i < 0 || i >= inv.slots.Count)
            {
                return;
            }
            var slot = inv.slots[i];
            slot.inventory = inv;
            // Our player holds an item on the cursor that came from this slot, which someone else has filled now: the
            // game would put the held item back there when it's let go (Controller.placeItem), over theirs. It goes
            // into our bags instead (it's ours: the slot gave it up when we picked it up).
            var ctl = Singleton<Controller>.Instance;
            InvItemClass held = null;
            if (ctl != null)
            {
                held = ctl.pickedUpItem;
            }
            var player = Player.Instance;
            if (!string.IsNullOrEmpty(json) && !InvItemClass.isNull(held) && held.slot == slot && player != null)
            {
                var keep = new InvItemClass(held);
                UIInvItem uiInvItem = held.UIInvItem;
                if (uiInvItem != null)
                {
                    uiInvItem.despawn();
                }
                ctl.pickedUpItem = null;
                int dropped = GiveBack(player, keep, keep.amount);
                Plugin.Log.LogInfo($"[slots] the {keep.type} we held came from slot {i} of {inv.name}, filled by someone else: into our bags ({dropped} at our feet)");
            }
            WorldActions.Replaying = true;
            try
            {
                slot.removeItem();
                var it = Item(json);
                // A gun's saved amount is its loaded rounds (it.ammo); its amount stays the 1 the copy gives it, not 0.
                if (it != null)
                {
                    int amount2;
                    if (it.baseClass != null && it.baseClass.hasAmmo)
                    {
                        amount2 = -1;
                    }
                    else
                    {
                        amount2 = it.amount;
                    }
                    slot.createItem(it, amount2);
                }
            }
            finally
            {
                WorldActions.Replaying = false;
            }
            if (_watched.TryGetValue(inv, out var keys) && i < keys.Length)
            {
                keys[i] = Key(slot);
            }
        }

        // ---- messages ----------------------------------------------------------------------

        private struct Change
        {
            public int Slot;
            public string Old, New;
        }

        // Client: the batches of slot changes we sent lately, to undo one whole when the host refuses it.
        private sealed class Sent
        {
            public string Obj;
            public List<Change> Changes;
            public float At;
            public bool Undone;
        }
        private readonly List<Sent> _sent = new List<Sent>();
        private const float SentKept = 15f;

        private void RememberSent(ObjRef obj, List<Change> changes)
        {
            float now = Time.unscaledTime;
            _sent.RemoveAll(b => now - b.At > SentKept);
            _sent.Add(new Sent { Obj = obj.ToString(), Changes = changes, At = now });
        }

        /// <summary>The batch we sent that had this change in it (the oldest not yet undone first), or null.</summary>
        private Sent SentWith(ObjRef obj, int slot, string oldKey, string newJson)
        {
            string key = obj.ToString();
            Sent found = null;
            foreach (var b in _sent)
            {
                if (b.Obj != key || !b.Changes.Exists(c => c.Slot == slot && c.Old == oldKey && c.New == newJson))
                {
                    continue;
                }
                if (!b.Undone)
                {
                    return b;
                }
                if (found == null)
                {
                    found = b;
                }
            }
            return found;
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
            var obj = ObjRef.Read(r);
            int n = r.R.ReadByte();
            var changes = new List<Change>(n);
            for (int k = 0; k < n; k++)
            {
                changes.Add(new Change { Slot = r.R.ReadUInt16(), Old = r.ReadStr(), New = r.ReadStr() });
            }
            var inv = ObjRef.Resolve<Inventory>(obj);
            if (WorldActions.Debug)
            {
                Plugin.Log.LogInfo($"[world] got {n} slot(s) of {obj}: " + (inv != null ? inv.name : "not found here"));
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

            // Host: the player must be at the container, it must be open in the host's world (a locked crate not
            // picked open here isn't), and nobody else may have it open (but its storage).
            string refusedAll = Containers.Refusal(from, inv);
            if (refusedAll == null && !Is(inv) && Containers.Instance != null && Containers.Instance.LockedByOther(obj, from, out string holder))
            {
                refusedAll = holder + " has it open";
            }

            // The changes stand if every slot still holds what the player saw there; if one doesn't, none does. A
            // batch is what one look at the container found changed: an item moved from one slot to another (a cursor
            // drag inside it) is two changes, and taking only one of them would double or lose that item.
            changes.RemoveAll(c => c.Slot >= inv.slots.Count);
            if (refusedAll == null)
            {
                foreach (var c in changes)
                {
                    string now = Key(inv.slots[c.Slot]);
                    if (now == c.Old || RotOnly(inv.slots[c.Slot].invItem, now, c.Old))
                    {
                        continue;
                    }
                    if (changes.Count > 1)
                    {
                        refusedAll = $"slot {c.Slot} had just changed, so the {changes.Count} changes made with it are refused too";
                    }
                    else
                    {
                        refusedAll = "it had just changed";
                    }
                    break;
                }
            }
            // A stack bigger than the host's stack size (StackSizes) isn't one the client could have made.
            if (refusedAll == null)
            {
                foreach (var c in changes)
                {
                    if (!StackSizes.Fits(Item(c.New)))
                    {
                        refusedAll = $"slot {c.Slot}: more than one stack holds";
                        break;
                    }
                }
            }
            var accepted = new List<Change>();
            foreach (var c in changes)
            {
                if (refusedAll == null)
                {
                    Set(inv, c.Slot, c.New);
                    accepted.Add(c);
                    continue;
                }
                Plugin.Log.LogInfo($"[authority] {s.NameOf(from)} changed slot {c.Slot} of {inv.name}: refused ({refusedAll})");
                var w = s.Begin(MsgType.ContainerSlotRefused);
                obj.Write(w);
                w.W.Write((ushort)c.Slot);
                w.WriteStr(Json(inv, c.Slot));
                w.WriteStr(c.Old);
                w.WriteStr(c.New);
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
        /// Client: another player's change to a slot, as the host took it. A change of ours to that slot not yet sent
        /// stands if the slot now holds what we changed it from (it goes at the next Check); otherwise the host would
        /// refuse it, so it's undone now, as Merge does, and Set doesn't double or lose what it moved.
        /// </summary>
        private void TakeHost(Inventory inv, Change c)
        {
            if (c.Slot >= inv.slots.Count || !_watched.TryGetValue(inv, out var keys) || c.Slot >= keys.Length || keys[c.Slot] == Key(inv.slots[c.Slot]))
            {
                Set(inv, c.Slot, c.New);
                return;
            }
            string oldKey = keys[c.Slot], hostKey = Key(Item(c.New));
            if (hostKey == oldKey || RotOnly(inv.slots[c.Slot].invItem, hostKey, oldKey))
            {
                return;
            }
            var tried = Item(Json(inv, c.Slot));
            Set(inv, c.Slot, c.New);
            Plugin.Log.LogInfo($"[authority] our change to slot {c.Slot} of {inv.name}: refused here (another player changed it first)");
            Undo(oldKey, tried);
        }

        /// <summary>Client: the host refused a slot change; take the host's slot, and undo what the change moved.</summary>
        private void OnRefused(ulong from, PacketReader r)
        {
            var s = _session;
            if (!s.IsClient || from != s.HostId || !WorldShare.InWorld)
            {
                return;
            }
            var obj = ObjRef.Read(r);
            int slot = r.R.ReadUInt16();
            string hostJson = r.ReadStr(), oldKey = r.ReadStr(), triedJson = r.ReadStr();
            var inv = ObjRef.Resolve<Inventory>(obj); // may be gone here: a dropped item we picked up with a click
            Plugin.Log.LogInfo($"[slots] {obj} slot {slot}: the host refused our change '{oldKey}' -> '{Key(Item(triedJson))}' (it holds '{Key(Item(hostJson))}' there)");
            // Something we changed in that slot after this change and haven't sent yet: Set replaces it too, so it's
            // undone as well. (A later change already sent is refused, and undone, by its own answer.)
            InvItemClass unsent = null;
            string sentKey = null;
            if (inv != null && slot < inv.slots.Count && _watched.TryGetValue(inv, out var keys) && slot < keys.Length && keys[slot] != Key(inv.slots[slot]))
            {
                sentKey = keys[slot];
                unsent = Item(Json(inv, slot));
            }
            if (inv != null)
            {
                Set(inv, slot, hostJson);
            }
            // What this change moved: from what we saw (its key) to what we made it. The host refuses a batch whole,
            // so the first refused slot of a batch we sent undoes the whole batch, netted per item type (an item we
            // moved within the container comes to nothing); its other slots' refusals only set the host's contents.
            var batch = SentWith(obj, slot, oldKey, triedJson);
            if (batch == null)
            {
                Undo(oldKey, Item(triedJson));
            }
            else if (!batch.Undone)
            {
                batch.Undone = true;
                var net = new Dictionary<string, (int n, InvItemClass item)>();
                foreach (var c in batch.Changes)
                {
                    Moved(c.Old, Item(c.New), net);
                }
                Undo(net, quiet: false);
            }
            if (sentKey != null)
            {
                Undo(sentKey, unsent, quiet: true);
            }
            UiRefresh.Changed(inv);
        }

        /// <summary>
        /// Our change made a slot go from oldKey (its key, "" empty) to tried. The slot now holds the host's contents
        /// (Set); undo the rest for our player: what we put in comes back into our bags or hotbar (at our feet only
        /// when there's no room), what we took goes again (from the cursor, the bags, the hotbar; never from the
        /// open storage, which is the host's now).
        /// </summary>
        private static void Undo(string oldKey, InvItemClass tried, bool quiet = false)
        {
            var net = new Dictionary<string, (int n, InvItemClass item)>();
            Moved(oldKey, tried, net);
            Undo(net, quiet);
        }

        /// <summary>
        /// Adds what a slot going from oldKey to tried moved, per item type, to net: more of a type in the container
        /// (we put it in) counts up, less (we took it) counts down. Summed over a whole batch, an item moved from one
        /// slot of the container to another comes to nothing.
        /// </summary>
        private static void Moved(string oldKey, InvItemClass tried, Dictionary<string, (int n, InvItemClass item)> net)
        {
            var old = oldKey.Split('|');
            string oldType = null;
            if (oldKey.Length > 0)
            {
                oldType = old[0];
            }
            int oldAmount = 0;
            if (oldKey.Length > 0 && old.Length > 1 && int.TryParse(old[1], out int a))
            {
                oldAmount = a;
            }
            bool any = !InvItemClass.isNull(tried);
            void Add(string type, int n, InvItemClass item)
            {
                net.TryGetValue(type, out var was);
                InvItemClass item2 = was.item;
                if (item2 == null)
                {
                    item2 = item;
                }
                net[type] = (was.n + n, item2);
            }
            if (any)
            {
                Add(tried.type, tried.amount, tried);
            }
            if (oldType != null)
            {
                Add(oldType, -oldAmount, null);
            }
        }

        /// <summary>Undo what net says our changes moved: what we put in comes back to us, what we took goes again.</summary>
        private static void Undo(Dictionary<string, (int n, InvItemClass item)> net, bool quiet)
        {
            var player = Player.Instance;
            if (player == null)
            {
                return;
            }
            bool any = false;
            foreach (var kv in net)
            {
                int put = kv.Value.n > 0 ? kv.Value.n : 0, took = kv.Value.n < 0 ? -kv.Value.n : 0;
                if (put == 0 && took == 0)
                {
                    continue;
                }
                any = true;
                int dropped = 0;
                if (put > 0 && kv.Value.item != null)
                {
                    dropped = GiveBack(player, kv.Value.item, put);
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
                        left -= player.Inventory.removeItemAmountFromPlayer(kv.Key, left, includeAdditionalInventory: false);
                    }
                }
                string data;
                if (put > 0)
                {
                    data = $"[slots] undone: gave back {put} {kv.Key} ({dropped} of them at our feet, no room)";
                }
                else
                {
                    data = $"[slots] undone: took back {took - left} of {took} {kv.Key}";
                }
                Plugin.Log.LogInfo(data);
            }
            if (any && !quiet)
            {
                player.displayMessage("Someone took that first");
            }
        }

        /// <summary>
        /// Put amount of this item back where it came from: the player's bags, then the hotbar (onto a stack of it
        /// first, then into a free slot). Not Inventory.addItemTypeToPlayer: with the workbench open that counts its
        /// storage as the player's, and would put it straight back there. Returns how many had to be dropped.
        /// </summary>
        private static int GiveBack(Player player, InvItemClass item, int amount)
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
                        if (InvItemClass.isNull(it) || it.type != item.type || it.amount >= it.baseClass.maxAmount)
                        {
                            continue;
                        }
                        int n = Mathf.Min(left, it.baseClass.maxAmount - it.amount);
                        var moved = new InvItemClass(item) { amount = n };
                        it.durability = InvItemClass.getStackedDurability(it, moved);
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
                player.spawnDroppedInvItem(new InvItemClass(item) { amount = left });
            }
            return left;
        }

        // ---- the workbench's level ---------------------------------------------------------

        internal void LevelRaised(int level)
        {
            if (!WorldActions.Sharing)
            {
                return;
            }
            var w = _session.Begin(MsgType.WorkbenchLevel);
            w.W.Write(level);
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
            int level = r.R.ReadInt32();
            var ctl = Singleton<Controller>.Instance;
            if (ctl == null)
            {
                return;
            }
            if (level <= ctl.workbenchLevel)
            {
                return;
            }
            if (s.IsHost)
            {
                // An upgrade (CraftingRecipes.doCraft) raises the level by one, at an open workbench.
                string refused = null;
                if (level != ctl.workbenchLevel + 1)
                {
                    refused = $"level {level} isn't one above {ctl.workbenchLevel}";
                }
                else if (!AtWorkbench(from))
                {
                    refused = "they aren't at a workbench";
                }
                if (refused != null)
                {
                    Plugin.Log.LogInfo($"[authority] {s.NameOf(from)} raised the workbench level: refused ({refused})");
                    return;
                }
                var w = s.Begin(MsgType.WorkbenchLevel);
                w.W.Write(level);
                s.Broadcast(w, Channel.Control, reliable: true, except: from);
            }
            ctl.workbenchLevel = level;
            Plugin.Log.LogInfo($"The workbench is now level {level} ({s.NameOf(from)} upgraded it)");
            WorkbenchStorage.ApplyAll("upgraded"); // its new row, before any change to it arrives
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
                open.workbench.refreshWorkbenchUpgrade(); // its "Level N" label and the next upgrade
            }
        }

        private static bool AtWorkbench(ulong player)
        {
            foreach (var wb in Object.FindObjectsOfType<Workbench>(true))
            {
                if (Authority.Near(player, wb.transform.position, WorldActions.Reach))
                {
                    return true;
                }
            }
            return false;
        }
    }

    /// <summary>The world's own durability changes in a watched storage (rot, drain, recharge) aren't the player's: SlotShare.Drift.</summary>
    [HarmonyPatch(typeof(Inventory), nameof(Inventory.refreshItems))]
    static class DriftPatch
    {
        static void Prefix(Inventory __instance, out string[] __state)
        {
            SlotShare instance = SlotShare.Instance;
            string[] state = null;
            if (instance != null)
            {
                state = instance.BeforeDrift(__instance);
            }
            __state = state;
        }
        static void Postfix(Inventory __instance, string[] __state)
        {
            SlotShare instance = SlotShare.Instance;
            if (instance != null)
            {
                instance.Drift(__instance, __state);
            }
        }
    }

    [HarmonyPatch(typeof(CraftingRecipes), nameof(CraftingRecipes.doCraft))]
    static class WorkbenchUpgradePatch
    {
        static void Prefix(out int __state)
        {
            if (Singleton<Controller>.Instance != null)
            {
                __state = Singleton<Controller>.Instance.workbenchLevel;
            }
            else
            {
                __state = 0;
            }
        }
        static void Postfix(int __state)
        {
            var ctl = Singleton<Controller>.Instance;
            if (ctl == null || ctl.workbenchLevel <= __state)
            {
                return;
            }
            WorkbenchStorage.ApplyAll("upgraded"); // the storage's new row (also without co-op)
            if (!WorldActions.Replaying)
            {
                SlotShare instance = SlotShare.Instance;
                if (instance != null)
                {
                    instance.LevelRaised(ctl.workbenchLevel);
                }
            }
        }
    }

    /// <summary>
    /// A dropped item picked up with a click (Item.getDroppedItem) moves its slot to the player and destroys the
    /// object without opening its window, so Containers never sees it. Look at its slot before and after, as if
    /// the window had been open for that moment: the change goes out slot by slot (a client's is the host's to
    /// accept), and the others' copy goes once it's empty.
    /// </summary>
    [HarmonyPatch(typeof(Item), nameof(Item.getDroppedItem))]
    static class ClickPickupPatch
    {
        private static readonly AccessTools.FieldRef<Item, Inventory> Inv = AccessTools.FieldRefAccess<Item, Inventory>("inventory");

        static void Prefix(Item __instance, out Inventory __state)
        {
            __state = null;
            var share = SlotShare.Instance;
            var inv = Inv(__instance);
            if (share == null || inv == null || !WorldActions.Sharing || share.Watching(inv))
            {
                return;
            }
            if (WorldActions.OursAlone(inv.gameObject))
            {
                return; // the host hasn't got it (Containers.Poll)
            }
            share.Check(inv, askHost: false); // as it is now (over within this frame: no waiting for the host)
            __state = inv;
        }

        static void Postfix(Inventory __state)
        {
            if (__state == null)
            {
                return;
            }
            if (WorldActions.Debug)
            {
                Plugin.Log.LogInfo($"[world] picked up {__state.name} with a click");
            }
            SlotShare instance = SlotShare.Instance;
            if (instance != null)
            {
                instance.Closed(__state);
            }
        }
    }
}
