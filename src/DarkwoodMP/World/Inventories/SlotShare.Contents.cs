using DarkwoodMP.Menus;
using System.Collections.Generic;
using DarkwoodMP.Combat;
using DarkwoodMP.Net;
using DarkwoodMP.World.Workbenches;
using Newtonsoft.Json;
using UnityEngine;

namespace DarkwoodMP.World.Inventories
{
    /// <summary>
    /// SlotShare: a client takes the host's contents of a container as its
    /// starting point (ContainerSlotsAsk, ContainerSlotsAll); containers
    /// emptied here go for the others too.
    /// </summary>
    internal sealed partial class SlotShare
    {
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
                _coop.Ui.Changed(inv);
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
    }
}
