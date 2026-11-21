using System.Collections.Generic;
using DarkwoodMP.Combat;
using DarkwoodMP.Net;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;

namespace DarkwoodMP.World.Inventories
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
    internal sealed partial class Containers
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

        private readonly CoopSession _coop;
        private readonly Session _session;
        private readonly System.Random _rng = new System.Random();

        public Containers(CoopSession coop)
        {
            _coop = coop;
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

            _coop.Slots.Update();

            SendPendingSpawns();
            if (Time.unscaledTime < _nextCheck)
            {
                return;
            }

            Poll();
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
            var slots = _coop.Slots;
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

            var slots = _coop.Slots;
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
    }
}
