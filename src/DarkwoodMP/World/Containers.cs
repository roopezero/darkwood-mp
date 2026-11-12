using System.Collections.Generic;
using DarkwoodMP.Combat;
using DarkwoodMP.Net;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;

namespace DarkwoodMP.World
{
    /// <summary>
    /// What's in the world's containers, shared: cupboards, crates, corpses, items lying on the ground (a dropped
    /// item is a one-slot container, Items/DroppedItem), so what one player takes is gone for everyone.
    ///
    /// A container's contents change only while a player has it open, so the container each player has open is
    /// checked a few times a second, and once more as it closes. When its contents changed, they're sent as the
    /// game saves them (Inventory.SaveState, as JSON) in ContainerState: ObjRef, str contents. The others load
    /// them into their copy (closing it first if they have it open), and remove it if it's one that goes when
    /// emptied. Clients send theirs to the host, which keeps them and passes them on.
    ///
    /// Items dropped while playing together come to the others as ObjSpawn: i64 id, u8 flags, str prefab, vec3 pos,
    /// quat rot, str contents, i32 experience (a death bag's DeathDrop.expAmount, else 0), sent once the item has
    /// come to rest. The host's enemies' loot comes to a client
    /// as the corpse's contents, when it dies.
    ///
    /// One player at a time has a container open: opening one asks the host for it (ContainerLock: ObjRef, u8 open);
    /// if someone else has it, the host says so (ContainerBusy: ObjRef, str who) and it closes again. Traders'
    /// stock is a container like the others while someone trades with them.
    /// </summary>
    public sealed class Containers
    {
        /// <summary>Prefabs of things made while playing that are shared with the others.</summary>
        public static readonly HashSet<string> SharedPrefabs = new HashSet<string>
        {
            "Items/DroppedItem", "Items/DroppedItem_water",
            "Objects/_Unique/deathDrop", "Objects/_Unique/deathDrop_water", // what a player who died left behind
            GasolineTrail, // poured by a player (Player.waitToSpillLiquid); each game merges trails into a puddle itself
        };

        /// <summary>
        /// Sent at once, not once at rest: a trail doesn't move, and the pourer's game merges a few into a puddle
        /// (GasolineTrail.init) and removes them half a second later. Every trail has to reach the others first,
        /// so their games merge the same ones.
        /// </summary>
        public const string GasolineTrail = "Items/GasolineTrail";

        /// <summary>True while the local player is placing something (a trap...), set around Player.progressBarCompleted.</summary>
        public static bool Placing;

        /// <summary>
        /// What a player can place, by prefab: each item that canBePlaced (InvItem.item, which
        /// Player.progressBarCompleted makes with the Object overload of Core.AddPrefab), with its path under
        /// Resources/Prefabs ("Traps/beartrap"), which is what ObjSpawn names. Built once from the item database.
        /// </summary>
        private static Dictionary<Object, string> _placeables;
        private static HashSet<string> _placeablePaths;
        /// <summary>The item types that place each path (more than one item may place the same trap).</summary>
        private static Dictionary<string, HashSet<string>> _placeableTypes;

        private static void BuildPlaceables()
        {
            var db = Singleton<ItemsDatabase>.Instance;
            if (db == null || db.itemsDict == null || db.itemsDict.Count == 0) return;
            _placeables = new Dictionary<Object, string>();
            _placeablePaths = new HashSet<string>();
            _placeableTypes = new Dictionary<string, HashSet<string>>();
            foreach (var kv in db.itemsDict)
            {
                var item = (Resources.Load(kv.Value) as GameObject)?.GetComponent<InvItem>();
                if (item == null || !item.canBePlaced || item.item == null) continue;
                string path = "Traps/" + item.item.name;
                if (Resources.Load("Prefabs/" + path) != item.item) { Plugin.Log.LogWarning($"Placeable {kv.Key}: its prefab {item.item.name} isn't in Prefabs/Traps; placing it won't be shared"); continue; }
                _placeables[item.item] = path;
                _placeablePaths.Add(path);
                if (!_placeableTypes.TryGetValue(path, out var types)) _placeableTypes[path] = types = new HashSet<string>();
                types.Add(kv.Key);
            }
            Plugin.Log.LogInfo($"Placeable things: {string.Join(", ", _placeablePaths)}");
        }

        /// <summary>The path (under Resources/Prefabs) of a prefab a player can place, or null.</summary>
        public static string PlaceablePath(Object prefab)
        {
            if (_placeables == null) BuildPlaceables();
            return prefab != null && _placeables != null && _placeables.TryGetValue(prefab, out var path) ? path : null;
        }

        /// <summary>Whether a player can place the prefab at this path (under Resources/Prefabs).</summary>
        public static bool IsPlaceable(string path)
        {
            if (_placeables == null) BuildPlaceables();
            return _placeablePaths != null && _placeablePaths.Contains(path);
        }

        /// <summary>The item types a player places the prefab at this path with, or null.</summary>
        public static HashSet<string> PlacedWith(string path)
        {
            if (_placeables == null) BuildPlaceables();
            return _placeableTypes != null && _placeableTypes.TryGetValue(path, out var types) ? types : null;
        }

        public static Containers Instance { get; private set; }
        private readonly Session _session;
        private readonly System.Random _rng = new System.Random();

        public Containers(Session session)
        {
            Instance = this;
            _session = session;
            session.Register(MsgType.ContainerState, OnContainer);
            session.Register(MsgType.ObjSpawn, OnSpawn);
            session.Register(MsgType.DeathBagMarker, OnBagMarker);
            session.Register(MsgType.ContainerLock, OnLock);
            session.Register(MsgType.ContainerBusy, OnBusy);
            session.OnPeerDisconnected += peer => { var gone = new List<string>(); foreach (var kv in _locks) if (kv.Value == peer) gone.Add(kv.Key); foreach (var k in gone) _locks.Remove(k); };
            session.OnSessionEnded += () => { _watched.Clear(); _pending.Clear(); ObjRef.ClearSpawned(); _corpsesSent.Clear(); _locks.Clear(); };
        }

        private static bool Sharing => WorldActions.Sharing;

        // ---- watching the open containers ------------------------------------------------

        private readonly Dictionary<Inventory, string> _watched = new Dictionary<Inventory, string>();
        private readonly HashSet<Inventory> _slotOpen = new HashSet<Inventory>();
        private readonly List<Inventory> _slotGone = new List<Inventory>();
        private float _nextCheck;

        public void Update()
        {
            WorldActions.ResetDepth();
            if (!Sharing) return;
            SlotShare.Instance?.Update();
            SendPendingSpawns();
            if (Time.unscaledTime < _nextCheck) return;
            Poll();
        }

        /// <summary>
        /// The local player opened a container (Player.setOpenedItemInventory): look at it now, not at the next of
        /// the checks a few times a second. What it held before our player could touch it is the starting point;
        /// a thing taken in the first quarter second after opening was otherwise in the starting point already, so
        /// the take was never sent, and the item stayed in the container too (doubled; seen with three players
        /// taking from one container at the same moment).
        /// </summary>
        internal void OpenedNow()
        {
            if (!Sharing) return;
            Poll();
        }

        private void Poll()
        {
            _nextCheck = Time.unscaledTime + 0.25f;
            var player = Player.Instance;
            var open = new List<Inventory>();
            if (player != null)
            {
                if (IsWorldContainer(player.openedItemInventory)) open.Add(player.openedItemInventory);
                if (IsWorldContainer(player.openedItemInventory2)) open.Add(player.openedItemInventory2);
            }
            // Slot by slot (SlotShare): the workbench's storage, used by everyone at once; and, on a client, every
            // container, so each change is the host's to accept (a refused one is undone for our player). Ordinary
            // containers are still one player at a time (the lock).
            var slots = SlotShare.Instance;
            for (int i = open.Count - 1; i >= 0; i--)
            {
                var inv = open[i];
                bool storage = slots != null && slots.Is(inv);
                if (slots == null || !(storage || _session.IsClient)) continue;
                if (!storage && !_slotOpen.Contains(inv)) { _refs[inv] = ObjRef.Of(inv.gameObject); Opened(inv); }
                slots.Check(inv);
                _slotOpen.Add(inv);
                open.RemoveAt(i);
            }
            _slotGone.Clear();
            foreach (var inv in _slotOpen)
                if (inv == null || (player != null && player.openedItemInventory != inv && player.openedItemInventory2 != inv)) _slotGone.Add(inv);
            foreach (var inv in _slotGone)
            {
                bool storage = slots != null && slots.Is(inv);
                slots?.Closed(inv);
                if (!storage) { Closed(inv); _refs.Remove(inv); }
                _slotOpen.Remove(inv);
            }

            foreach (var inv in open)
            {
                if (!_watched.ContainsKey(inv))
                {
                    _watched[inv] = Contents(inv); // its contents as we found it
                    Opened(inv);
                }
                else Check(inv);
            }
            // Closed since the last check: one last look (it may be gone: emptied and removed).
            var closed = new List<Inventory>();
            foreach (var inv in _watched.Keys) if (!open.Contains(inv)) closed.Add(inv);
            foreach (var inv in closed)
            {
                if (inv != null) Check(inv);
                else SendGone(inv);
                Closed(inv);
                _watched.Remove(inv);
            }
            if (_session.IsHost) SendCorpseLoot();
        }

        private static bool IsWorldContainer(Inventory inv) =>
            inv != null && (inv.invType == Inventory.InvType.itemInv || inv.invType == Inventory.InvType.deathDrop || inv.invType == Inventory.InvType.shop) && !inv.isWorkbench;

        /// <summary>Send what's in a container to the others now (a trader's new stock).</summary>
        public void SendContents(Inventory inv)
        {
            if (inv == null) return;
            if (WorldActions.Debug) Plugin.Log.LogInfo($"[world] sent contents of {inv.name} {ObjRef.Of(inv.gameObject)}");
            Send(ObjRef.Of(inv.gameObject), JsonConvert.SerializeObject(new Inventory.SaveState(inv)), except: 0);
        }

        /// <summary>Host: send what's in a container to one client (a corpse it has only now been sent).</summary>
        public void SendContentsTo(ulong peer, Inventory inv)
        {
            var r = inv != null ? ObjRef.Of(inv.gameObject) : default;
            if (!r.Valid || !_session.IsHost) return;
            var w = _session.Begin(MsgType.ContainerState);
            r.Write(w);
            w.WriteBigStr(JsonConvert.SerializeObject(new Inventory.SaveState(inv))); // bigstr, as OnContainer reads it
            _session.SendTo(peer, w, Channel.Control, reliable: true);
        }

        // ---- one player at a time ------------------------------------------------------------

        /// <summary>Host: who has which container open ("kind:id" of its ObjRef).</summary>
        private readonly Dictionary<string, ulong> _locks = new Dictionary<string, ulong>();
        public int LockCount => _locks.Count;
        private static string LockKey(ObjRef r) => r.ToString();

        private void Opened(Inventory inv)
        {
            var r = ObjRef.Of(inv.gameObject);
            if (!r.Valid) return;
            if (_session.IsClient) { SendLock(r, open: true); return; }
            // Host: our own player opening it.
            if (_locks.TryGetValue(LockKey(r), out var holder) && holder != _session.LocalId) Busy(inv, _session.NameOf(holder));
            else _locks[LockKey(r)] = _session.LocalId;
        }

        private void Closed(Inventory inv)
        {
            if (!_refs.TryGetValue(inv, out var r) || !r.Valid) return;
            if (_session.IsClient) SendLock(r, open: false);
            else if (_locks.TryGetValue(LockKey(r), out var holder) && holder == _session.LocalId) _locks.Remove(LockKey(r));
        }

        /// <summary>Host: whether someone other than this player has the container open.</summary>
        public bool LockedByOther(ObjRef r, ulong who, out string holderName)
        {
            holderName = null;
            if (!_locks.TryGetValue(LockKey(r), out var holder) || holder == who) return false;
            holderName = _session.NameOf(holder);
            return true;
        }

        private void SendLock(ObjRef r, bool open)
        {
            var w = _session.Begin(MsgType.ContainerLock);
            r.Write(w);
            w.W.Write((byte)(open ? 1 : 0));
            _session.SendToHost(w, Channel.Control, reliable: true);
        }

        private void OnLock(ulong from, PacketReader r)
        {
            if (!_session.IsHost) return;
            var obj = ObjRef.Read(r);
            bool open = r.R.ReadByte() != 0;
            string key = LockKey(obj);
            bool held = _locks.TryGetValue(key, out var holder);
            if (!open) { if (held && holder == from) _locks.Remove(key); return; }
            if (held && holder != from)
            {
                var w = _session.Begin(MsgType.ContainerBusy);
                obj.Write(w);
                w.WriteStr(_session.NameOf(holder));
                _session.SendTo(from, w, Channel.Control, reliable: true);
                return;
            }
            _locks[key] = from;
        }

        private void OnBusy(ulong from, PacketReader r)
        {
            if (from != _session.HostId) return;
            var obj = ObjRef.Read(r);
            string who = r.ReadStr();
            var inv = ObjRef.Resolve<Inventory>(obj);
            if (inv != null) Busy(inv, who);
        }

        private void Busy(Inventory inv, string who)
        {
            _watched.Remove(inv); // nothing of ours to send from it
            if (inv.open) inv.hide();
            Player.Instance?.displayMessage($"{who} is using this");
            Plugin.Log.LogInfo($"{inv.name} is in use by {who}");
        }

        private void Check(Inventory inv)
        {
            string now = Contents(inv);
            if (_watched.TryGetValue(inv, out var before) && before == now) return;
            _watched[inv] = now;
            if (WorldActions.Debug) Plugin.Log.LogInfo($"[world] sent contents of {inv.name} {ObjRef.Of(inv.gameObject)}");
            Send(ObjRef.Of(inv.gameObject), now, except: 0);
        }

        // An emptied container that removes itself is destroyed as it closes; tell the others it's empty.
        private readonly Dictionary<Inventory, ObjRef> _refs = new Dictionary<Inventory, ObjRef>();
        private void SendGone(Inventory inv)
        {
            if (_refs.TryGetValue(inv, out var r)) { Send(r, Empty, except: 0); _refs.Remove(inv); }
        }

        private static readonly string Empty = JsonConvert.SerializeObject(new Inventory.SaveState());

        private string Contents(Inventory inv)
        {
            _refs[inv] = ObjRef.Of(inv.gameObject); // remembered, as the object may be gone at the last look
            return JsonConvert.SerializeObject(new Inventory.SaveState(inv));
        }

        private void Send(ObjRef r, string contents, ulong except)
        {
            if (!r.Valid) return;
            var w = _session.Begin(MsgType.ContainerState);
            r.Write(w);
            w.WriteBigStr(contents);
            if (_session.IsHost) _session.Broadcast(w, Channel.Control, reliable: true, except: except);
            else _session.SendToHost(w, Channel.Control, reliable: true);
        }

        /// <summary>
        /// Why nobody may take from or put into this container in the host's world, or null if they may: a locked
        /// crate not yet picked open (Item.disarmTime &gt; 0 and not searched: Item.openInventory starts the lockpick
        /// instead), one with a Locked that is locked, or one that must be broken open first (a larva egg).
        /// </summary>
        public static string LockedReason(Inventory inv)
        {
            if (inv == null) return null;
            var item = inv.GetComponent<Item>();
            if (WorldActions.Crate(item) != null && !item.searched) return "locked";
            var l = inv.GetComponent<Locked>();
            if (l != null && l.locked) return "locked";
            if (item != null && item.destroyToAccessInventory && !item.destroyed) return "locked (not broken open)";
            return null;
        }

        /// <summary>Host: why a client's change to this container is refused (too far from it, locked), or null.</summary>
        public static string Refusal(ulong from, Inventory inv)
        {
            if (NetPlayerProxy.All.TryGetValue(from, out var proxy) && proxy != null)
            {
                var d = proxy.transform.position - inv.transform.position;
                if (new Vector2(d.x, d.z).magnitude > WorldActions.Reach) return "too far from it";
            }
            return LockedReason(inv);
        }

        private void OnContainer(ulong from, PacketReader r)
        {
            if (!WorldShare.InWorld) return;
            if (_session.IsClient && from != _session.HostId) return;
            var obj = ObjRef.Read(r);
            string contents = r.ReadBigStr();
            var inv = ObjRef.Resolve<Inventory>(obj);
            if (WorldActions.Debug) Plugin.Log.LogInfo($"[world] got contents for {obj}: " + (inv != null ? inv.name : "not found here"));
            // A client's whole contents (it sends an emptied container that went this way): refused if it couldn't
            // have changed it, and the host's contents go back to it to roll back to.
            if (_session.IsHost && inv != null && Refusal(from, inv) is string why)
            {
                Plugin.Log.LogInfo($"[authority] {_session.NameOf(from)} changed the contents of {inv.name}: refused ({why})");
                var w = _session.Begin(MsgType.ContainerState);
                obj.Write(w);
                w.WriteBigStr(JsonConvert.SerializeObject(new Inventory.SaveState(inv)));
                _session.SendTo(from, w, Channel.Control, reliable: true);
                return;
            }
            if (inv != null) Apply(inv, contents);
            if (_session.IsHost) Send(obj, contents, except: from); // pass it on
        }

        internal void Apply(Inventory inv, string contents)
        {
            Inventory.SaveState state;
            try { state = JsonConvert.DeserializeObject<Inventory.SaveState>(contents); }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Bad container contents: {e.Message}"); return; }
            if (state == null) return;
            if (inv.open && state.slotCount == inv.slots.Count && UiRefresh.Enabled) { ApplyOpen(inv, state); return; }
            if (inv.open) inv.hide(); // a different number of slots than its window has drawn: close it rather than show stale slots
            if (inv == null) return; // hide() removed it (it was empty and goes when emptied)
            WorldActions.Replaying = true;
            try { state.loadValues(inv); }
            finally { WorldActions.Replaying = false; }
            _watched.Remove(inv);
            if (inv.removeWhenEmpty && inv.getAllItems().Count == 0) Object.Destroy(inv.gameObject);
        }

        /// <summary>
        /// The contents of a container whose window is open here, loaded into it without closing it: its slots draw
        /// their new icons as they're filled (InvSlot.createItem), and UiRefresh redraws the rest of the window.
        /// A client watching it slot by slot takes them as SlotShare.MergeOpen settles them (a change of ours not
        /// yet sent stays or is undone, as the host would decide). Otherwise what our player changed since the last
        /// look goes first, and the loaded contents become the starting point, so they aren't sent back.
        /// </summary>
        private void ApplyOpen(Inventory inv, Inventory.SaveState state)
        {
            var slots = SlotShare.Instance;
            if (_session.IsClient && slots != null && slots.Watching(inv))
            {
                slots.MergeOpen(inv, state, $"{ObjRef.Of(inv.gameObject)} contents from host, open here");
                return;
            }
            slots?.FlushBeforeLoad(inv);
            if (_watched.ContainsKey(inv)) Check(inv);
            WorldActions.Replaying = true;
            try { state.loadValues(inv); }
            finally { WorldActions.Replaying = false; }
            slots?.Loaded(inv);
            if (_watched.ContainsKey(inv)) _watched[inv] = Contents(inv);
            Plugin.Log.LogInfo($"[ui] {inv.name} {ObjRef.Of(inv.gameObject)}: contents loaded into its open window ({inv.getAllItems().Count} items)");
            UiRefresh.Changed(inv);
        }

        // ---- things dropped while playing together ---------------------------------------

        private sealed class Pending { public long Id; public GameObject Go; public string Prefab; public bool Placed; public float SendAt; }
        private readonly List<Pending> _pending = new List<Pending>();

        /// <summary>Core.AddPrefab made something; share it if it's a shared kind (and we made it, not a replay).</summary>
        public void Made(string prefab, GameObject go)
        {
            if (go == null || WorldActions.Replaying || !Sharing || !(SharedPrefabs.Contains(prefab) || Placing)) return;
            long id = ((long)_rng.Next() << 31) | (uint)_rng.Next();
            ObjRef.RegisterSpawned(id, go);
            _pending.Add(new Pending { Id = id, Go = go, Prefab = prefab, Placed = Placing, SendAt = Time.time + (Placing || prefab == GasolineTrail ? 0.2f : 1.5f) }); // dropped: once it has come to rest
        }

        /// <summary>The Object overload of Core.AddPrefab made something: share it if our player is placing it.</summary>
        public void MadeFrom(Object prefab, GameObject go)
        {
            if (ThrownSpawning != null) { ShareThrownSpawn(ThrownSpawning, go); return; }
            if (!Placing || go == null || WorldActions.Replaying || !Sharing) return;
            string path = PlaceablePath(prefab);
            if (path == null) { Plugin.Log.LogWarning($"Placed {go.name}, which isn't a placeable prefab here: not shared"); return; }
            Made(path, go);
        }

        /// <summary>
        /// What a thrown item leaves behind when it goes off (Explodes.spawnObject: a molotov's gasoline), by prefab.
        /// The host's game makes it and sends it; a client's game makes none of its own (ThrownLeavesPatch), so the
        /// fire the host then lights in it (ObjectActions, LiquidLit) is found by ObjRef in every game. Accepted
        /// from the host only: Items/Gasoline is also what each game merges gasoline trails into by itself.
        /// </summary>
        public static readonly HashSet<string> ThrownSpawns = new HashSet<string> { "Items/Gasoline" };

        /// <summary>Set while an Explodes on a thrown item spawns its objects on the host: their prefab path.</summary>
        internal static string ThrownSpawning;

        /// <summary>Host: a thrown item that went off made this (ThrownSpawns): send it now, before the fire that lights it.</summary>
        private void ShareThrownSpawn(string prefab, GameObject go)
        {
            if (go == null || !_session.IsHost || !Sharing) return;
            long id = ((long)_rng.Next() << 31) | (uint)_rng.Next();
            ObjRef.RegisterSpawned(id, go);
            if (WorldActions.Debug) Plugin.Log.LogInfo($"[world] sent {prefab} #{id} at {go.transform.position} (left by a thrown item)");
            SendSpawn(id, prefab, go.transform.position, go.transform.rotation, "", false, 0, except: 0);
        }

        /// <summary>
        /// Our player has just placed something (the end of Player.progressBarCompleted): send it now, in the frame
        /// the item was used up, so a client's request carries that and the item comes back if the host refuses.
        /// </summary>
        public void SendPlacedNow() => SendPendingSpawns(placedNow: true);

        private void SendPendingSpawns(bool placedNow = false)
        {
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                var p = _pending[i];
                if (p.Go == null) { _pending.RemoveAt(i); continue; }
                if (placedNow ? !p.Placed : Time.time < p.SendAt) continue;
                _pending.RemoveAt(i);
                var inv = p.Go.GetComponent<Inventory>();
                if (WorldActions.Debug) Plugin.Log.LogInfo($"[world] sent {p.Prefab} #{p.Id} at {p.Go.transform.position}");
                var drop = p.Go.GetComponent<DeathDrop>();
                if (p.Placed && _session.IsClient)
                {
                    // Placed by our player: the host decides (within reach, a thing a player can place). Refused,
                    // ours goes, and the item used up comes back (InventoryWatch).
                    var go = p.Go;
                    long id = p.Id;
                    string prefab = p.Prefab;
                    var w = SpawnMessage(id, prefab, go.transform.position, go.transform.rotation, inv != null ? JsonConvert.SerializeObject(new Inventory.SaveState(inv)) : "", true, 0);
                    Authority.Request(w, $"placing {prefab} #{id}", (ok, why, result) =>
                    {
                        if (ok || go == null) return;
                        ObjRef.Spawned.Remove(id);
                        Object.Destroy(go);
                        Plugin.Log.LogInfo($"[world] our {prefab} #{id} was refused: removed it");
                    });
                    continue;
                }
                SendSpawn(p.Id, p.Prefab, p.Go.transform.position, p.Go.transform.rotation, inv != null ? JsonConvert.SerializeObject(new Inventory.SaveState(inv)) : "", p.Placed, drop != null ? drop.expAmount : 0, except: 0);
                if (drop != null && drop.additionalMapMarker != null) SendBagMarker(p.Id, drop.additionalMapMarker.transform.position, except: 0);
            }
        }

        /// <summary>The map marker the game puts for a death bag left in a separate location (Player.dropBody).</summary>
        public const string DeathBagMarkerPrefab = "Objects/_Unique/deathDrop_marker";

        private void SendBagMarker(long id, Vector3 pos, ulong except)
        {
            var w = _session.Begin(MsgType.DeathBagMarker);
            w.W.Write(id);
            w.Write(pos);
            if (_session.IsHost) _session.Broadcast(w, Channel.Control, reliable: true, except: except);
            else _session.SendToHost(w, Channel.Control, reliable: true);
            if (WorldActions.Debug) Plugin.Log.LogInfo($"[world] sent the map marker of death bag #{id} at {pos}");
        }

        private void OnBagMarker(ulong from, PacketReader r)
        {
            if (!WorldShare.InWorld) return;
            if (_session.IsClient && from != _session.HostId) return;
            long id = r.R.ReadInt64();
            Vector3 pos = r.ReadVector3();
            var drop = ObjRef.Spawned.TryGetValue(id, out var go) && go != null ? go.GetComponent<DeathDrop>() : null;
            if (WorldActions.Debug) Plugin.Log.LogInfo($"[world] got the map marker of death bag #{id}: " + (drop != null ? drop.name : "no such bag here"));
            if (drop == null || drop.additionalMapMarker != null) return;
            WorldActions.Replaying = true;
            try { drop.additionalMapMarker = Core.AddPrefab(DeathBagMarkerPrefab, pos, Quaternion.Euler(90f, 0f, 0f), null); }
            finally { WorldActions.Replaying = false; }
            if (_session.IsHost) SendBagMarker(id, pos, except: from);
        }

        private void SendSpawn(long id, string prefab, Vector3 pos, Quaternion rot, string contents, bool placed, int exp, ulong except)
        {
            var w = SpawnMessage(id, prefab, pos, rot, contents, placed, exp);
            if (_session.IsHost) _session.Broadcast(w, Channel.Control, reliable: true, except: except);
            else _session.SendToHost(w, Channel.Control, reliable: true);
        }

        private PacketWriter SpawnMessage(long id, string prefab, Vector3 pos, Quaternion rot, string contents, bool placed, int exp)
        {
            var w = _session.Begin(MsgType.ObjSpawn);
            w.W.Write(id);
            w.W.Write((byte)(placed ? 1 : 0));
            w.WriteStr(prefab);
            w.Write(pos);
            w.W.Write(rot.x); w.W.Write(rot.y); w.W.Write(rot.z); w.W.Write(rot.w);
            w.WriteBigStr(contents);
            w.W.Write(exp);
            return w;
        }

        private void OnSpawn(ulong from, PacketReader r)
        {
            if (!WorldShare.InWorld) return;
            if (_session.IsClient && from != _session.HostId) return;
            long id = r.R.ReadInt64();
            bool placed = (r.R.ReadByte() & 1) != 0;
            string prefab = r.ReadStr();
            Vector3 pos = r.ReadVector3();
            var rot = new Quaternion(r.R.ReadSingle(), r.R.ReadSingle(), r.R.ReadSingle(), r.R.ReadSingle());
            string contents = r.ReadBigStr();
            int exp = r.R.ReadInt32();
            bool thrownSpawn = _session.IsClient && ThrownSpawns.Contains(prefab);
            if (!(SharedPrefabs.Contains(prefab) || placed || thrownSpawn) || ObjRef.Spawned.ContainsKey(id)) return;
            if (_session.IsHost && placed)
            {
                // A client's player placed it: only as a request, within reach, and only a thing a player can place.
                if (!Authority.Handling) { Plugin.Log.LogWarning($"[world] {_session.NameOf(from)} placed {prefab} without asking: ignored"); return; }
                if (!IsPlaceable(prefab)) { Authority.Refuse($"{prefab} isn't something a player can place"); return; }
                if (!Authority.InReach(pos, WorldActions.Reach, prefab)) return;
                // And only with the item in their bags: the character the client sent as its player began placing
                // (PlacingPatch), less what it has placed since. Not known yet (no character from it this session): allowed.
                int? carried = Characters.Instance?.Carries(from, PlacedWith(prefab));
                if (carried.HasValue && carried.Value < 1) { Authority.Refuse($"they don't carry anything that places {prefab}"); return; }
                if (carried.HasValue) Characters.Instance.Placed(from);
            }
            if (Resources.Load("Prefabs/" + prefab) == null) { Authority.Refuse($"there's no {prefab} here"); Plugin.Log.LogWarning($"No prefab {prefab} here"); return; }

            GameObject go;
            WorldActions.Replaying = true;
            try
            {
                go = Core.AddPrefab(prefab, pos, rot, Core.ItemContainer, worldSpace: true);
                if (go == null) return;
                if (placed)
                {
                    var trap = go.GetComponent<Trigger>();
                    if (trap != null) trap.setByPlayer = true; // as the game marks what a player sets
                }
                if (!thrownSpawn) Core.addToSaveable(go, isDynamic: true, assignID: placed); // (the host's isn't saved either)
                Singleton<WorldGrid>.Instance?.registerToNode(go);
                var rb = go.GetComponent<Rigidbody>();
                if (rb != null) rb.velocity = Vector3.zero;
            }
            finally { WorldActions.Replaying = false; }
            ObjRef.RegisterSpawned(id, go);
            if (thrownSpawn) ThrownHits.Spawned.Add(go); // its flames count as thrown here, as our own copy's did
            var drop = go.GetComponent<DeathDrop>();
            if (drop != null) drop.expAmount = exp; // the experience its player lost, for whoever takes it
            var inv = go.GetComponent<Inventory>();
            if (inv != null && contents.Length > 0) Apply(inv, contents);
            if (WorldActions.Debug) Plugin.Log.LogInfo($"[world] made {prefab} #{id} at {pos}: {(inv != null ? inv.getAllItems().Count : 0)} items");
            if (_session.IsHost) SendSpawn(id, prefab, pos, rot, contents, placed, exp, except: from); // pass it on
        }

        // ---- the host's enemies' loot ------------------------------------------------------

        private readonly HashSet<Character> _corpsesSent = new HashSet<Character>();

        /// <summary>Host: when an enemy the clients see has died, send them what its corpse holds.</summary>
        private void SendCorpseLoot()
        {
            var ne = NetEnemies.Instance;
            if (ne == null) return;
            foreach (var c in ne.HostDeadSince(2f))
            {
                if (c == null || !_corpsesSent.Add(c)) continue;
                var inv = c.GetComponent<Inventory>();
                if (inv == null) continue;
                Send(ObjRef.Of(c.gameObject), JsonConvert.SerializeObject(new Inventory.SaveState(inv)), except: 0);
            }
        }
    }

    /// <summary>What the player places (a trap...) is made in Player.progressBarCompleted; share it.</summary>
    [HarmonyPatch(typeof(Player), nameof(Player.progressBarCompleted))]
    static class PlacingPatch
    {
        static void Prefix(Player __instance)
        {
            Containers.Placing = __instance.placingItem;
            // A client's character goes to the host first, the item to place still in it (it's used up below), so the
            // host's check of what it carries (Containers.OnSpawn) sees what was picked up or made a moment ago.
            if (__instance.placingItem && NetEnemies.ClientMode && WorldActions.Sharing) Characters.Instance?.SaveNow();
        }
        static void Postfix()
        {
            bool placed = Containers.Placing;
            Containers.Placing = false;
            if (placed) Containers.Instance?.SendPlacedNow();
        }
        static System.Exception Finalizer(System.Exception __exception) { Containers.Placing = false; return __exception; }
    }

    /// <summary>Things made while playing together (a dropped item) are shared with the others.</summary>
    [HarmonyPatch(typeof(Core), nameof(Core.AddPrefab), new[] { typeof(string), typeof(Vector3), typeof(Quaternion), typeof(GameObject), typeof(bool) })]
    static class SharedPrefabPatch
    {
        /// <summary>Inside the string overload, which makes it with the Object overload: shared from here only.</summary>
        internal static int Depth;
        static void Prefix() => Depth++;
        static void Postfix(string prefab, GameObject __result) => Containers.Instance?.Made(prefab, __result);
        static System.Exception Finalizer(System.Exception __exception) { Depth--; if (Depth < 0) Depth = 0; return __exception; }
    }

    /// <summary>
    /// What the player places is made with the Object overload (Player.progressBarCompleted passes the item's
    /// prefab, InvItem.item), not the string one: shared from here, unless the string overload is the caller.
    /// </summary>
    [HarmonyPatch(typeof(Core), nameof(Core.AddPrefab), new[] { typeof(Object), typeof(Vector3), typeof(Quaternion), typeof(GameObject), typeof(bool) })]
    static class PlacedPrefabPatch
    {
        static void Postfix(Object prefab, GameObject __result)
        {
            if (SharedPrefabPatch.Depth == 0) Containers.Instance?.MadeFrom(prefab, __result);
        }
    }

    /// <summary>See Containers.OpenedNow.</summary>
    [HarmonyPatch(typeof(Player), nameof(Player.setOpenedItemInventory))]
    static class ContainerOpenedNowPatch
    {
        static void Postfix(Inventory inv)
        {
            if (inv == null || Containers.Instance == null) return;
            try { Containers.Instance.OpenedNow(); }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Container open check failed: {e.Message}"); }
        }
    }
}
