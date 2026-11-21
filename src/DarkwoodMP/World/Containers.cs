using System.Collections.Generic;
using DarkwoodMP.Combat;
using DarkwoodMP.Net;
using DarkwoodMP.World.Inventories;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;

namespace DarkwoodMP.World
{
    /// <summary>
    /// What's in the world's containers, shared: cupboards, crates, corpses,
    /// items lying on the ground (a dropped item is a one-slot container,
    /// Items/DroppedItem), so what one player takes is gone for everyone.
    ///
    /// A container's contents change only while a player has it open, so the
    /// container each player has open is checked a few times a second, and once
    /// more as it closes. When its contents changed, they're sent as the game
    /// saves them (Inventory.SaveState, as JSON) in ContainerState: ObjRef,
    /// str contents. The others load them into their copy (closing it first if
    /// they have it open), and remove it if it's one that goes when emptied.
    /// Clients send theirs to the host, which keeps them and passes them on.
    ///
    /// Items dropped while playing together come to the others as ObjSpawn:
    /// i64 id, u8 flags, str prefab, vec3 pos, quat rot, str contents,
    /// i32 experience (a death bag's DeathDrop.expAmount, else 0), sent once
    /// the item has come to rest. The host's enemies' loot comes to a client as
    /// the corpse's contents, when it dies.
    ///
    /// One player at a time has a container open: opening one asks the host for
    /// it (ContainerLock: ObjRef, u8 open); if someone else has it, the host
    /// says so (ContainerBusy: ObjRef, str who) and it closes again. Traders'
    /// stock is a container like the others while someone trades with them.
    /// </summary>
    internal sealed class Containers
    {
        /// <summary>
        /// Sent at once, not once at rest: a trail doesn't move, and the
        /// pourer's game merges a few into a puddle (GasolineTrail.init) and
        /// removes them half a second later. Every trail has to reach the
        /// others first, so their games merge the same ones.
        /// </summary>
        public const string GasolineTrail = SpawnRules.GasolineTrail;

        /// <summary>
        /// True while the local player is placing something (a trap...), set
        /// around Player.progressBarCompleted.
        /// </summary>
        public bool Placing;

        /// <summary>
        /// The path (under Resources/Prefabs) of a prefab a player can place,
        /// or null (ModHost.Placeables).
        /// </summary>
        public static string PlaceablePath(Object prefab)
        {
            return Plugin.Instance.Host.Placeables.PathOf(prefab);
        }

        /// <summary>
        /// Whether a player can place the prefab at this path (under
        /// Resources/Prefabs).
        /// </summary>
        public static bool IsPlaceable(string path)
        {
            return Plugin.Instance.Host.Placeables.Has(path);
        }

        /// <summary>
        /// The item types a player places the prefab at this path with, or
        /// null.
        /// </summary>
        public static HashSet<string> PlacedWith(string path)
        {
            return Plugin.Instance.Host.Placeables.TypesOf(path);
        }

        private readonly Session _session;
        private readonly System.Random _rng = new System.Random();

        public Containers(CoopSession coop)
        {
            _session = coop.Net;
            coop.Handle(MsgType.ContainerState, OnContainer);
            coop.Handle(MsgType.ObjSpawn, OnSpawn);
            coop.Handle(MsgType.DeathBagMarker, OnBagMarker);
            coop.Handle(MsgType.ContainerLock, OnLock);
            coop.Handle(MsgType.ContainerBusy, OnBusy);
            _session.OnPeerDisconnected += OnPeerLeft;
            coop.Hook(() => _session.OnPeerDisconnected -= OnPeerLeft);
        }

        private void OnPeerLeft(ulong peer)
        {
            _locks.Leave(peer);
        }

        private static bool Sharing => Coop.Guards.Sharing;

        // ---- watching the open containers -----------------------------------

        private readonly Dictionary<Inventory, string> _watched =
            new Dictionary<Inventory, string>();
        private readonly HashSet<Inventory> _slotOpen =
            new HashSet<Inventory>();
        private readonly List<Inventory> _slotGone = new List<Inventory>();
        private float _nextCheck;

        /// <summary>
        /// Plugin.Update, each frame, also with no session: the guards'
        /// depth goes back to 0, and a window left by a container that went
        /// while open is removed.
        /// </summary>
        public static void EachFrame()
        {
            Coop.Guards.ResetDepth();
            DropOrphanedWindows();
        }

        /// <summary>
        /// Plugin.Update, each frame while a session runs, after EachFrame.
        /// </summary>
        public void Update()
        {
            if (!Sharing)
            {
                return;
            }

            SlotShare instance = SlotShare.Instance;
            if (instance != null)
            {
                instance.Update();
            }

            SendPendingSpawns();
            if (Time.unscaledTime < _nextCheck)
            {
                return;
            }

            Poll();
        }

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

        /// <summary>
        /// The local player opened a container (Player.setOpenedItemInventory):
        /// look at it now, not at the next of the checks a few times a second.
        /// What it held before our player could touch it is the starting point;
        /// a thing taken in the first quarter second after opening would
        /// otherwise be in the starting point already, so the take would never
        /// be sent and the item would stay in the container too (doubled).
        /// </summary>
        internal void OpenedNow(Inventory inv)
        {
            if (inv == null || !Sharing)
            {
                return;
            }

            try
            {
                Poll();
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning(
                    $"Container open check failed: {e.Message}");
            }
        }

        private void Poll()
        {
            _nextCheck = Time.unscaledTime + ContainerRules.PollEvery;
            var player = Player.Instance;
            var open = new List<Inventory>();
            if (player != null)
            {
                // Not one only our player has (in a dream of our own): the host
                // hasn't got it, so it's ours, as alone.
                AddShared(open, player.openedItemInventory);
                AddShared(open, player.openedItemInventory2);
            }

            // Slot by slot (SlotShare): the workbench's storage, used by
            // everyone at once; and, on a client, every container, so each
            // change is the host's to accept (a refused one is undone for our
            // player). Ordinary containers are still one player at a time (the
            // lock).
            var slots = SlotShare.Instance;
            for (int i = open.Count - 1; i >= 0; i--)
            {
                var inv = open[i];
                var path = ContainerRules.PathOf(FactsOf(inv));
                if (slots == null || path == ContainerPath.Whole)
                {
                    continue;
                }

                bool storage = path == ContainerPath.Slots;

                if (!storage && !_slotOpen.Contains(inv))
                {
                    _refs[inv] = ObjRef.Of(inv.gameObject);
                    Opened(inv);
                }

                slots.Check(inv);
                _slotOpen.Add(inv);
                open.RemoveAt(i);
            }

            _slotGone.Clear();
            foreach (var inv in _slotOpen)
            {
                if (inv == null
                    || (player != null && player.openedItemInventory != inv
                        && player.openedItemInventory2 != inv))
                {
                    _slotGone.Add(inv);
                }
            }

            foreach (var inv in _slotGone)
            {
                bool storage = slots != null && slots.Is(inv);
                if (slots != null)
                {
                    slots.Closed(inv);
                }

                if (!storage)
                {
                    Closed(inv);
                    _refs.Remove(inv);
                }

                _slotOpen.Remove(inv);
            }

            foreach (var inv in open)
            {
                if (!_watched.ContainsKey(inv))
                {
                    // its contents as we found it
                    _watched[inv] = Contents(inv);
                    Opened(inv);
                }
                else
                {
                    Check(inv);
                }
            }

            // Closed since the last check: one last look (it may be gone:
            // emptied and removed).
            var closed = new List<Inventory>();
            foreach (var inv in _watched.Keys)
            {
                if (!open.Contains(inv))
                {
                    closed.Add(inv);
                }
            }

            foreach (var inv in closed)
            {
                if (inv != null)
                {
                    Check(inv);
                }
                else
                {
                    SendGone(inv);
                }

                Closed(inv);
                _watched.Remove(inv);
            }

            if (_session.IsHost)
            {
                SendCorpseLoot();
            }
        }

        private void AddShared(List<Inventory> open, Inventory inv)
        {
            if (ContainerRules.PathOf(FactsOf(inv)) != ContainerPath.Ignore)
            {
                open.Add(inv);
            }
        }

        /// <summary>
        /// What the container rules look at in a container our player has
        /// open: the only code that reads it for them. Null is no container.
        /// </summary>
        private OpenContainerFacts FactsOf(Inventory inv)
        {
            if (inv == null)
            {
                return default;
            }

            var slots = SlotShare.Instance;
            return new OpenContainerFacts
            {
                Kind = KindOf(inv),
                Workbench = inv.isWorkbench,
                OursAlone = WorldActions.OursAlone(inv.gameObject),
                Storage = slots != null && slots.Is(inv),
                IsClient = _session.IsClient,
            };
        }

        private static ContainerKind KindOf(Inventory inv)
        {
            switch (inv.invType)
            {
                case Inventory.InvType.itemInv:
                    return ContainerKind.Item;
                case Inventory.InvType.deathDrop:
                    return ContainerKind.DeathDrop;
                case Inventory.InvType.shop:
                    return ContainerKind.Shop;
                default:
                    return ContainerKind.Other;
            }
        }

        /// <summary>
        /// Send what's in a container to the others now (a trader's new stock).
        /// </summary>
        public void SendContents(Inventory inv)
        {
            if (inv != null)
            {
                SendNow(inv, Serialize(inv));
            }
        }

        private void SendNow(Inventory inv, string contents)
        {
            if (WorldActions.Debug)
            {
                Plugin.Log.LogInfo(
                    $"[world] sent contents of {inv.name} "
                        + $"{ObjRef.Of(inv.gameObject)}");
            }

            Send(ObjRef.Of(inv.gameObject), contents, except: 0);
        }

        /// <summary>
        /// Host: send what's in a container to one client (a corpse it has only
        /// now been sent).
        /// </summary>
        public void SendContentsTo(ulong peer, Inventory inv)
        {
            ObjRef r = default;
            if (inv != null)
            {
                r = ObjRef.Of(inv.gameObject);
            }

            if (!r.Valid || !_session.IsHost)
            {
                return;
            }

            var w = _session.Begin(MsgType.ContainerState);
            ContainerStateMessage.Write(w, ObjRef.NameOf(r), Serialize(inv));
            _session.SendTo(peer, w, Channel.Control, reliable: true);
        }

        // ---- one player at a time -------------------------------------------

        /// <summary>
        /// Host: who has which container open ("kind:id" of its ObjRef).
        /// </summary>
        private readonly ContainerLocks _locks = new ContainerLocks();
        public int LockCount => _locks.Count;
        private static string LockKey(ObjRef r) => r.ToString();

        private void Opened(Inventory inv)
        {
            var r = ObjRef.Of(inv.gameObject);
            if (!r.Valid)
            {
                return;
            }

            if (_session.IsClient)
            {
                SendLock(r, open: true);
                return;
            }

            // Host: our own player opening it.
            if (!_locks.Open(LockKey(r), _session.LocalId, out var holder))
            {
                Busy(inv, _session.NameOf(holder));
            }
        }

        private void Closed(Inventory inv)
        {
            if (!_refs.TryGetValue(inv, out var r) || !r.Valid)
            {
                return;
            }

            if (_session.IsClient)
            {
                SendLock(r, open: false);
            }
            else
            {
                _locks.Close(LockKey(r), _session.LocalId);
            }
        }

        /// <summary>
        /// Host: whether someone other than this player has the container open.
        /// </summary>
        public bool LockedByOther(ObjRef r, ulong who, out string holderName)
        {
            holderName = null;
            if (!_locks.HeldByOther(LockKey(r), who, out var holder))
            {
                return false;
            }

            holderName = _session.NameOf(holder);
            return true;
        }

        private void SendLock(ObjRef r, bool open)
        {
            var w = _session.Begin(MsgType.ContainerLock);
            ContainerLockMessage.Write(w, ObjRef.NameOf(r), open);
            _session.SendToHost(w, Channel.Control, reliable: true);
        }

        private void OnLock(ulong from, PacketReader r)
        {
            if (!_session.IsHost)
            {
                return;
            }

            var obj = ObjRef.RefOf(ContainerLockMessage.Read(r, out bool open));
            string key = LockKey(obj);
            if (!open)
            {
                _locks.Close(key, from);
                return;
            }

            if (!_locks.Open(key, from, out var holder))
            {
                var w = _session.Begin(MsgType.ContainerBusy);
                ContainerBusyMessage.Write(
                    w, ObjRef.NameOf(obj), _session.NameOf(holder));
                _session.SendTo(from, w, Channel.Control, reliable: true);
            }
        }

        private void OnBusy(ulong from, PacketReader r)
        {
            if (from != _session.HostId)
            {
                return;
            }

            var obj = ObjRef.RefOf(
                ContainerBusyMessage.Read(r, out string who));
            var inv = ObjRef.Resolve<Inventory>(obj);
            if (inv != null)
            {
                Busy(inv, who);
            }
        }

        private void Busy(Inventory inv, string who)
        {
            _watched.Remove(inv); // nothing of ours to send from it
            if (inv.open)
            {
                inv.hide();
            }

            Player instance = Player.Instance;
            if (instance != null)
            {
                instance.displayMessage($"{who} is using this");
            }

            Plugin.Log.LogInfo($"{inv.name} is in use by {who}");
        }

        private void Check(Inventory inv)
        {
            string now = Contents(inv);
            if (_watched.TryGetValue(inv, out var before) && before == now)
            {
                return;
            }

            _watched[inv] = now;
            SendNow(inv, now);
        }

        // An emptied container that removes itself is destroyed as it closes;
        // tell the others it's empty.
        private readonly Dictionary<Inventory, ObjRef> _refs =
            new Dictionary<Inventory, ObjRef>();
        private void SendGone(Inventory inv)
        {
            if (_refs.TryGetValue(inv, out var r))
            {
                Send(r, Empty, except: 0);
                _refs.Remove(inv);
            }
        }

        private static readonly string Empty =
            JsonConvert.SerializeObject(new Inventory.SaveState());

        /// <summary>
        /// A container's contents as the game saves them (Inventory.SaveState,
        /// as JSON).
        /// </summary>
        private static string Serialize(Inventory inv) =>
            JsonConvert.SerializeObject(new Inventory.SaveState(inv));

        private string Contents(Inventory inv)
        {
            // remembered, as the object may be gone at the last look
            _refs[inv] = ObjRef.Of(inv.gameObject);
            return Serialize(inv);
        }

        /// <summary>
        /// The host's to everyone but except, a client's to the host.
        /// </summary>
        private void Share(PacketWriter w, ulong except)
        {
            if (_session.IsHost)
            {
                _session.Broadcast(
                    w, Channel.Control, reliable: true, except: except);
            }
            else
            {
                _session.SendToHost(w, Channel.Control, reliable: true);
            }
        }

        private void Send(ObjRef r, string contents, ulong except)
        {
            if (!r.Valid)
            {
                return;
            }

            var w = _session.Begin(MsgType.ContainerState);
            ContainerStateMessage.Write(w, ObjRef.NameOf(r), contents);
            Share(w, except);
        }

        /// <summary>
        /// Why nobody may take from or put into this container in the host's
        /// world, or null if they may: a locked crate not yet picked open
        /// (Item.disarmTime &gt; 0 and not searched: Item.openInventory starts
        /// the lockpick instead), one with a Locked that is locked, or one that
        /// must be broken open first (a larva egg).
        /// </summary>
        public static string LockedReason(Inventory inv)
        {
            return TextOf(ContainerRules.LockedReason(LockOf(inv)));
        }

        /// <summary>
        /// Whether a container is shut in this world: the only code that
        /// reads its Item and Locked for the rules. Null is open.
        /// </summary>
        private static LockFacts LockOf(Inventory inv)
        {
            if (inv == null)
            {
                return default;
            }

            var item = inv.GetComponent<Item>();
            var l = inv.GetComponent<Locked>();
            var facts = new LockFacts
            {
                Crate = WorldActions.Crate(item) != null,
                Locked = l != null && l.locked,
            };

            if (item != null)
            {
                facts = facts with
                {
                    Searched = item.searched,
                    DestroyToAccess = item.destroyToAccessInventory,
                    Destroyed = item.destroyed,
                };
            }

            return facts;
        }

        // A refusal's words, or null when nothing was refused.
        private static string TextOf(Verdict verdict)
        {
            if (!verdict.Refused)
            {
                return null;
            }

            return Refusals.Text(verdict);
        }

        /// <summary>
        /// Host: why a client's change to this container is refused (too far
        /// from it, locked), or null.
        /// </summary>
        public static string Refusal(ulong from, Inventory inv)
        {
            return TextOf(ContainerRules.MayChange(FactsOf(from, inv)));
        }

        /// <summary>
        /// What the host knows of a client's stand-in and a container it
        /// changed: the only code that reads them for MayChange.
        /// </summary>
        private static ContainerFacts FactsOf(ulong from, Inventory inv)
        {
            var facts = new ContainerFacts { Lock = LockOf(inv) };
            if (NetPlayerProxy.All.TryGetValue(from, out var proxy)
                && proxy != null)
            {
                Vector3 p = proxy.transform.position;
                Vector3 at = inv.transform.position;
                facts = facts with
                {
                    Known = true,
                    StandIn = new Vec2(p.x, p.z),
                    At = new Vec2(at.x, at.z),
                };
            }

            return facts;
        }

        // Whether this game takes a world message from that peer.
        private bool Accepts(ulong from)
        {
            return ContainerRules.Accepts(
                WorldShare.InWorld,
                _session.IsClient,
                from == _session.HostId);
        }

        private void OnContainer(ulong from, PacketReader r)
        {
            if (!Accepts(from))
            {
                return;
            }

            var obj = ObjRef.RefOf(
                ContainerStateMessage.Read(r, out string contents));
            var inv = ObjRef.Resolve<Inventory>(obj);
            if (WorldActions.Debug)
            {
                string name = "not found here";
                if (inv != null)
                {
                    name = inv.name;
                }

                Plugin.Log.LogInfo($"[world] got contents for {obj}: " + name);
            }

            // A client's whole contents (it sends an emptied container that
            // went this way): refused if it couldn't have changed it, and the
            // host's contents go back to it to roll back to.
            var change = Verdict.Allow;
            if (_session.IsHost && inv != null)
            {
                change = ContainerRules.MayChange(FactsOf(from, inv));
            }

            var answer = ContainerRules.Received(
                _session.IsHost, inv != null, change);
            if (answer.Verdict.Refused)
            {
                string why = Refusals.Text(answer.Verdict);
                Plugin.Log.LogInfo(
                    $"[authority] {_session.NameOf(from)} changed the contents "
                        + $"of {inv.name}: refused ({why})");
                var w = _session.Begin(MsgType.ContainerState);
                ContainerStateMessage.Write(
                    w, ObjRef.NameOf(obj), Serialize(inv));
                _session.SendTo(from, w, Channel.Control, reliable: true);
                return;
            }

            if (answer.Apply)
            {
                Apply(inv, contents);
            }

            if (answer.Relay)
            {
                Send(obj, contents, except: from); // pass it on
            }
        }

        internal void Apply(Inventory inv, string contents)
        {
            Inventory.SaveState state;
            try
            {
                state = JsonConvert.DeserializeObject<Inventory.SaveState>(
                    contents);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning($"Bad container contents: {e.Message}");
                return;
            }

            if (state == null)
            {
                return;
            }

            bool sameSlots = inv.open && state.slotCount == inv.slots.Count;
            var load = ContainerRules.LoadPathOf(
                inv.open, sameSlots, UiRefresh.Enabled);
            if (load == LoadPath.InWindow)
            {
                ApplyOpen(inv, state);
                return;
            }

            if (load == LoadPath.CloseFirst)
            {
                // a different number of slots than its window has drawn: close
                // it rather than show stale slots
                inv.hide();
            }

            if (inv == null)
            {
                // hide() removed it (it was empty and goes when emptied)
                return;
            }

            Coop.Guards.Replaying = true;
            try
            {
                state.loadValues(inv);
            }
            finally
            {
                Coop.Guards.Replaying = false;
            }

            _watched.Remove(inv);
            if (ContainerRules.RemovesAfterLoad(
                inv.removeWhenEmpty, inv.getAllItems().Count))
            {
                Object.Destroy(inv.gameObject);
            }
        }

        /// <summary>
        /// The contents of a container whose window is open here, loaded into
        /// it without closing it: its slots draw their new icons as they're
        /// filled (InvSlot.createItem), and UiRefresh redraws the rest of the
        /// window.
        /// A client watching it slot by slot takes them as SlotShare.MergeOpen
        /// settles them (a change of ours not yet sent stays or is undone, as
        /// the host would decide). Otherwise what our player changed since the
        /// last look goes first, and the loaded contents become the starting
        /// point, so they aren't sent back.
        /// </summary>
        private void ApplyOpen(Inventory inv, Inventory.SaveState state)
        {
            var slots = SlotShare.Instance;
            if (slots != null
                && ContainerRules.MergesInWindow(
                    _session.IsClient, slots.Watching(inv)))
            {
                slots.MergeOpen(
                    inv, state,
                    $"{ObjRef.Of(inv.gameObject)} contents from host, open "
                        + "here");
                return;
            }

            if (slots != null)
            {
                slots.FlushBeforeLoad(inv);
            }

            if (_watched.ContainsKey(inv))
            {
                Check(inv);
            }

            Coop.Guards.Replaying = true;
            try
            {
                state.loadValues(inv);
            }
            finally
            {
                Coop.Guards.Replaying = false;
            }

            if (slots != null)
            {
                slots.Loaded(inv);
            }

            if (_watched.ContainsKey(inv))
            {
                _watched[inv] = Contents(inv);
            }

            Plugin.Log.LogInfo(
                $"[ui] {inv.name} "
                    + $"{ObjRef.Of(inv.gameObject)}: contents loaded into "
                    + $"its open window ({inv.getAllItems().Count} items)");
            UiRefresh.Changed(inv);
        }

        // ---- things dropped while playing together --------------------------

        private sealed class Pending
        {
            public long Id;
            public GameObject Go;
            public string Prefab;
            public bool Placed;
            public float SendAt;
        }

        private readonly List<Pending> _pending = new List<Pending>();

        /// <summary>
        /// A random id for something spawned while playing
        /// (ObjRef.RegisterSpawned).
        /// </summary>
        private long NewId() => ((long)_rng.Next() << 31) | (uint)_rng.Next();

        /// <summary>
        /// Core.AddPrefab made something; share it if it's a shared kind (and
        /// we made it, not a replay).
        /// </summary>
        public void Made(string prefab, GameObject go)
        {
            // Not dropped in a dream of our own: nobody else is there.
            if (!SpawnRules.Shares(FactsOf(prefab, go)))
            {
                return;
            }

            long id = NewId();
            ObjRef.RegisterSpawned(id, go);
            Seconds delay = SpawnRules.SendDelay(Placing, prefab);
            _pending.Add(new Pending
            {
                Id = id,
                Go = go,
                Prefab = prefab,
                Placed = Placing,
                SendAt = Time.time + delay.Value
            });
        }

        /// <summary>
        /// What the spawn rules look at in something Core.AddPrefab made:
        /// the only code that reads it for them.
        /// </summary>
        private MadeFacts FactsOf(string prefab, GameObject go)
        {
            var facts = new MadeFacts
            {
                Exists = go != null,
                Replaying = Coop.Guards.Replaying,
                Sharing = Sharing,
                Prefab = prefab,
                Placing = Placing,
            };

            if (go != null)
            {
                facts = facts with { OursAlone = WorldActions.OursAlone(go) };
            }

            return facts;
        }

        /// <summary>
        /// The Object overload of Core.AddPrefab made something: share it if
        /// our player is placing it.
        /// </summary>
        public void MadeFrom(Object prefab, GameObject go)
        {
            if (_makingDepth > 0)
            {
                return;
            }

            var step = SpawnRules.FromPrefab(FactsOf(prefab, go));
            if (step == MadeFromStep.Thrown)
            {
                ShareThrownSpawn(ThrownSpawning, go);
            }
            else if (step == MadeFromStep.NotPlaceable)
            {
                Plugin.Log.LogWarning(
                    $"Placed {go.name}, which isn't a placeable prefab here: "
                        + "not shared");
            }
            else if (step == MadeFromStep.Share)
            {
                Made(PlaceablePath(prefab), go);
            }
        }

        /// <summary>
        /// What MadeFrom's rule looks at in something made from a prefab
        /// object: the only code that reads it for that rule. The placeable
        /// path is only looked up once the checks before it passed, as
        /// before.
        /// </summary>
        private MadeFromFacts FactsOf(Object prefab, GameObject go)
        {
            var facts = new MadeFromFacts
            {
                Thrown = ThrownSpawning != null,
                Placing = Placing,
                Exists = go != null,
                Replaying = Coop.Guards.Replaying,
                Sharing = Sharing,
            };

            if (facts.Thrown || !facts.Placing || !facts.Exists
                || facts.Replaying || !facts.Sharing)
            {
                return facts;
            }

            return facts with { Placeable = PlaceablePath(prefab) != null };
        }

        /// <summary>
        /// What a thrown item leaves behind when it goes off
        /// (Explodes.spawnObject: a molotov's gasoline), by prefab. The host's
        /// game makes it and sends it; a client's game makes none of its own
        /// (ThrownLeavesPatch), so the fire the host then lights in it
        /// (ObjectActions, LiquidLit) is found by ObjRef in every game.
        /// Accepted from the host only: Items/Gasoline is also what each game
        /// merges gasoline trails into by itself.
        /// </summary>
        public static readonly HashSet<string> ThrownSpawns =
            new HashSet<string> { SpawnRules.ThrownGasoline };

        /// <summary>
        /// Set while an Explodes on a thrown item spawns its objects on the
        /// host: their prefab path.
        /// </summary>
        internal string ThrownSpawning;

        /// <summary>
        /// How deep inside the string overload of Core.AddPrefab we are,
        /// which makes it with the Object overload: shared from there only
        /// (MadeFrom is skipped while it's above 0).
        /// </summary>
        private int _makingDepth;

        /// <summary>
        /// The string overload of Core.AddPrefab starts (AddPrefabByPath).
        /// </summary>
        public void MakingStarts()
        {
            _makingDepth++;
        }

        /// <summary>
        /// The string overload of Core.AddPrefab is done, or threw.
        /// </summary>
        public void MakingEnds()
        {
            _makingDepth--;
            if (_makingDepth < 0)
            {
                _makingDepth = 0;
            }
        }

        /// <summary>
        /// Host: a thrown item that went off made this (ThrownSpawns): send it
        /// now, before the fire that lights it.
        /// </summary>
        private void ShareThrownSpawn(string prefab, GameObject go)
        {
            if (!SpawnRules.SharesThrown(go != null, _session.IsHost, Sharing))
            {
                return;
            }

            long id = NewId();
            ObjRef.RegisterSpawned(id, go);
            if (WorldActions.Debug)
            {
                Plugin.Log.LogInfo(
                    $"[world] sent {prefab} #{id} at {go.transform.position} "
                        + "(left by a thrown item)");
            }

            SendSpawn(
                id, prefab, go.transform.position, go.transform.rotation, "",
                false, 0, except: 0);
        }

        /// <summary>
        /// Our player has just placed something (the end of
        /// Player.progressBarCompleted): send it now, in the frame the item was
        /// used up, so a client's request carries that and the item comes back
        /// if the host refuses.
        /// </summary>
        public void SendPlacedNow() => SendPendingSpawns(placedNow: true);

        /// <summary>
        /// Player.progressBarCompleted starts: our player may be placing
        /// something. A client's character goes to the host first, the item
        /// to place still in it (it's used up below), so the host's check of
        /// what it carries (OnSpawn) sees what was picked up or made a moment
        /// ago.
        /// </summary>
        public void PlacingStarts(Player player)
        {
            Placing = player.placingItem;
            if (player.placingItem && NetEnemies.ClientMode
                && Coop.Guards.Sharing)
            {
                Characters instance = Characters.Instance;
                if (instance != null)
                {
                    instance.SaveNow();
                }
            }
        }

        /// <summary>
        /// Player.progressBarCompleted is done: what was placed goes now
        /// (SendPlacedNow).
        /// </summary>
        public void PlacingEnds()
        {
            bool placed = Placing;
            Placing = false;
            if (placed)
            {
                SendPlacedNow();
            }
        }

        private void SendPendingSpawns(bool placedNow = false)
        {
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                var p = _pending[i];
                if (p.Go == null)
                {
                    _pending.RemoveAt(i);
                    continue;
                }

                bool condition;
                if (placedNow)
                {
                    condition = !p.Placed;
                }
                else
                {
                    condition = !SpawnRules.Due(
                        new Seconds(Time.time), new Seconds(p.SendAt));
                }

                if (condition)
                {
                    continue;
                }

                _pending.RemoveAt(i);
                var inv = p.Go.GetComponent<Inventory>();
                string contents = "";
                if (inv != null)
                {
                    contents = Serialize(inv);
                }

                if (WorldActions.Debug)
                {
                    Plugin.Log.LogInfo(
                        $"[world] sent {p.Prefab} #{p.Id} at "
                            + $"{p.Go.transform.position}");
                }

                var drop = p.Go.GetComponent<DeathDrop>();
                if (SpawnRules.AsksHost(p.Placed, _session.IsClient))
                {
                    // Placed by our player: the host decides (within reach, a
                    // thing a player can place). Refused, ours goes, and the
                    // item used up comes back (InventoryWatch).
                    var go = p.Go;
                    long id = p.Id;
                    string prefab = p.Prefab;
                    var w = SpawnMessage(
                        id, prefab, go.transform.position,
                        go.transform.rotation, contents, true, 0);
                    Coop.Current.Authority.Request(w, $"placing {prefab} #{id}",
                        (ok, why, result) =>
                    {
                        if (ok || go == null)
                        {
                            return;
                        }

                        ObjRef.Spawned.Remove(id);
                        CloseIfOpen(go);
                        Object.Destroy(go);
                        Plugin.Log.LogInfo(
                            $"[world] our {prefab} #{id} was refused: removed "
                                + "it");
                    });

                    continue;
                }

                int exp = 0;
                if (drop != null)
                {
                    exp = drop.expAmount;
                }

                SendSpawn(
                    p.Id, p.Prefab, p.Go.transform.position,
                    p.Go.transform.rotation, contents, p.Placed, exp,
                    except: 0);
                if (drop != null && drop.additionalMapMarker != null)
                {
                    SendBagMarker(
                        p.Id, drop.additionalMapMarker.transform.position,
                        except: 0);
                }
            }
        }

        /// <summary>
        /// The map marker the game puts for a death bag left in a separate
        /// location (Player.dropBody).
        /// </summary>
        public const string DeathBagMarkerPrefab = SpawnRules.BagMarker;

        private void SendBagMarker(long id, Vector3 pos, ulong except)
        {
            var w = _session.Begin(MsgType.DeathBagMarker);
            DeathBagMarkerMessage.Write(w, id, new Vec3(pos.x, pos.y, pos.z));
            Share(w, except);
            if (WorldActions.Debug)
            {
                Plugin.Log.LogInfo(
                    $"[world] sent the map marker of death bag #{id} at {pos}");
            }
        }

        private void OnBagMarker(ulong from, PacketReader r)
        {
            if (!Accepts(from))
            {
                return;
            }

            long id = DeathBagMarkerMessage.Read(r, out Vec3 marker);
            var pos = new Vector3(marker.X, marker.Y, marker.Z);
            DeathDrop drop = null;
            if (ObjRef.Spawned.TryGetValue(id, out var go) && go != null)
            {
                drop = go.GetComponent<DeathDrop>();
            }

            if (WorldActions.Debug)
            {
                string bagName = "no such bag here";
                if (drop != null)
                {
                    bagName = drop.name;
                }

                Plugin.Log.LogInfo(
                    $"[world] got the map marker of death bag #{id}: "
                        + bagName);
            }

            if (!SpawnRules.TakesMarker(
                drop != null, drop != null && drop.additionalMapMarker != null))
            {
                return;
            }

            Coop.Guards.Replaying = true;
            try
            {
                drop.additionalMapMarker = Core.AddPrefab(
                    DeathBagMarkerPrefab,
                    pos,
                    Quaternion.Euler(SpawnRules.MarkerTilt, 0f, 0f),
                    null);
            }
            finally
            {
                Coop.Guards.Replaying = false;
            }

            if (_session.IsHost)
            {
                SendBagMarker(id, pos, except: from);
            }
        }

        private void SendSpawn(
            long id, string prefab, Vector3 pos, Quaternion rot,
            string contents, bool placed, int exp, ulong except)
        {
            var w = SpawnMessage(id, prefab, pos, rot, contents, placed, exp);
            Share(w, except);
        }

        private PacketWriter SpawnMessage(
            long id, string prefab, Vector3 pos, Quaternion rot,
            string contents, bool placed, int exp)
        {
            var w = _session.Begin(MsgType.ObjSpawn);
            ObjSpawnMessage.Write(
                w,
                new SpawnedThing
                {
                    Id = id,
                    Placed = placed,
                    Prefab = prefab,
                    Pos = new Vec3(pos.x, pos.y, pos.z),
                    Rot = new Quat(rot.x, rot.y, rot.z, rot.w),
                    Contents = contents,
                    Experience = exp,
                });

            return w;
        }

        /// <summary>
        /// What the spawn rules look at in something another game made: the
        /// only code that reads this game for them. A client's placed thing
        /// (on the host) is checked in the old order, each only once the one
        /// before it passed: a request, a placeable prefab, in reach, and the
        /// items its character carries (not known: no character from it this
        /// session).
        /// </summary>
        private SpawnFacts FactsOf(ulong from, in SpawnedThing thing)
        {
            string prefab = thing.Prefab;
            var facts = new SpawnFacts
            {
                IsHost = _session.IsHost,
                IsClient = _session.IsClient,
                Prefab = prefab,
                Placed = thing.Placed,
                Known = ObjRef.Spawned.ContainsKey(thing.Id),
                PrefabExists = Resources.Load("Prefabs/" + prefab) != null,
            };

            CoopSession coop = Coop.Current;
            if (!facts.IsHost || !facts.Placed || coop == null)
            {
                return facts;
            }

            facts = facts with
            {
                Handling = coop.Authority.Handling,
                Placeable = IsPlaceable(prefab),
            };

            if (!facts.Handling || !facts.Placeable)
            {
                return facts;
            }

            var pos = new Vector3(thing.Pos.X, thing.Pos.Y, thing.Pos.Z);
            facts = facts with
            {
                Reach = coop.Authority.ReachOf(
                    pos, WorldActions.Reach, prefab),
            };

            Characters chars = Characters.Instance;
            if (facts.Reach.Refused || chars == null)
            {
                return facts;
            }

            int? carried = chars.Carries(from, PlacedWith(prefab));
            if (carried.HasValue)
            {
                facts = facts with
                {
                    CharacterKnown = true,
                    Carried = carried.Value,
                };
            }

            return facts;
        }

        private void OnSpawn(ulong from, PacketReader r)
        {
            if (!Accepts(from))
            {
                return;
            }

            SpawnedThing thing = ObjSpawnMessage.Read(r);
            long id = thing.Id;
            bool placed = thing.Placed;
            string prefab = thing.Prefab;
            var pos = new Vector3(thing.Pos.X, thing.Pos.Y, thing.Pos.Z);
            var rot = new Quaternion(
                thing.Rot.X, thing.Rot.Y, thing.Rot.Z, thing.Rot.W);
            string contents = thing.Contents;
            int exp = thing.Experience;
            var answer = SpawnRules.Receive(FactsOf(from, thing));
            if (answer.Step == SpawnStep.Ignore)
            {
                return;
            }

            if (answer.Step == SpawnStep.Unasked)
            {
                Plugin.Log.LogWarning(
                    $"[world] {_session.NameOf(from)} placed {prefab} "
                        + "without asking: ignored");
                return;
            }

            // One placed thing less in the client's character's bags
            // (PlacingStarts), even when its prefab isn't here.
            if (answer.CountsPlaced)
            {
                Characters.Instance.Placed(from);
            }

            if (answer.Step == SpawnStep.Refuse)
            {
                Coop.Current.Authority.Refuse(answer.Verdict);
                if (answer.Verdict.Reason == DarkwoodMP.Refusal.NoSuchPrefab)
                {
                    Plugin.Log.LogWarning($"No prefab {prefab} here");
                }

                return;
            }

            bool thrownSpawn = answer.Thrown;

            GameObject go;
            Coop.Guards.Replaying = true;
            try
            {
                go = Core.AddPrefab(
                    prefab, pos, rot, Core.ItemContainer, worldSpace: true);
                if (go == null)
                {
                    return;
                }

                if (placed)
                {
                    var trap = go.GetComponent<Trigger>();
                    if (trap != null)
                    {
                        // as the game marks what a player sets
                        trap.setByPlayer = true;
                    }
                }

                if (!thrownSpawn)
                {
                    // (the host's isn't saved either)
                    Core.addToSaveable(go, isDynamic: true, assignID: placed);
                }

                WorldGrid grid = Singleton<WorldGrid>.Instance;
                if (grid != null)
                {
                    grid.registerToNode(go);
                }

                var rb = go.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.velocity = Vector3.zero;
                }
            }
            finally
            {
                Coop.Guards.Replaying = false;
            }

            ObjRef.RegisterSpawned(id, go);
            if (thrownSpawn)
            {
                // its flames count as thrown here, as our own copy's did
                ThrownHits.Spawned.Add(go);
            }

            var drop = go.GetComponent<DeathDrop>();
            if (drop != null)
            {
                // the experience its player lost, for whoever takes it
                drop.expAmount = exp;
            }

            var inv = go.GetComponent<Inventory>();
            if (inv != null && contents.Length > 0)
            {
                Apply(inv, contents);
            }

            if (WorldActions.Debug)
            {
                int count = 0;
                if (inv != null)
                {
                    count = inv.getAllItems().Count;
                }

                Plugin.Log.LogInfo(
                    $"[world] made {prefab} #{id} at {pos}: {count} items");
            }

            if (_session.IsHost)
            {
                // pass it on
                SendSpawn(
                    id, prefab, pos, rot, contents, placed, exp, except: from);
            }
        }

        // ---- the host's enemies' loot ---------------------------------------

        private readonly HashSet<Character> _corpsesSent =
            new HashSet<Character>();

        /// <summary>
        /// Host: when an enemy the clients see has died, send them what its
        /// corpse holds.
        /// </summary>
        private void SendCorpseLoot()
        {
            var ne = NetEnemies.Instance;
            if (ne == null)
            {
                return;
            }

            foreach (var c in ne.HostDeadSince(SpawnRules.CorpseDelay))
            {
                if (c == null || !_corpsesSent.Add(c))
                {
                    continue;
                }

                var inv = c.GetComponent<Inventory>();
                if (inv == null)
                {
                    continue;
                }

                Send(ObjRef.Of(c.gameObject), Serialize(inv), except: 0);
            }
        }
    }
}
