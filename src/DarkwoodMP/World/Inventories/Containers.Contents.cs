using System.Collections.Generic;
using DarkwoodMP.Combat;
using DarkwoodMP.Net;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;

namespace DarkwoodMP.World.Inventories
{
    /// <summary>
    /// Containers: a container's contents, sent when they changed and loaded
    /// from the others (ContainerState), and the host's checks on them.
    /// </summary>
    internal sealed partial class Containers
    {
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
    }
}
