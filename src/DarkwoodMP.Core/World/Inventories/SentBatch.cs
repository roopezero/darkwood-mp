using System.Collections.Generic;

namespace DarkwoodMP.World.Inventories
{
    /// A batch of slot changes a client sent to the host.
    public sealed class SentBatch
    {
        /// The container's ObjRef as text.
        public string Obj { get; }

        public IReadOnlyList<SlotChange> Changes { get; }

        /// When it was sent, on the unscaled clock.
        public Seconds At { get; }

        /// A refusal of one of its changes undid all of it.
        public bool Undone { get; set; }

        public SentBatch(
            string obj,
            IReadOnlyList<SlotChange> changes,
            Seconds at)
        {
            Obj = obj;
            Changes = changes;
            At = at;
        }

        /// Whether the batch holds this change to that container.
        public bool Holds(string obj, SlotChange change)
        {
            if (Obj != obj)
            {
                return false;
            }

            foreach (var c in Changes)
            {
                if (c.Slot == change.Slot && c.Old == change.Old
                    && c.New == change.New)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
