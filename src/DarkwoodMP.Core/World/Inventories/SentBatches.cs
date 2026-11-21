using System.Collections.Generic;

namespace DarkwoodMP.World.Inventories
{
    /// Client: the batches of slot changes we sent lately, to undo one
    /// whole when the host refuses it (the plugin's SlotShare._sent).
    public sealed class SentBatches
    {
        readonly List<SentBatch> _sent = new List<SentBatch>();

        public int Count => _sent.Count;

        /// Remember a batch sent now. Batches sent more than
        /// SlotRules.SentKept ago are forgotten first.
        public SentBatch Remember(
            string obj,
            IReadOnlyList<SlotChange> changes,
            Seconds now)
        {
            _sent.RemoveAll(b => (now - b.At).Value > SlotRules.SentKept);
            var batch = new SentBatch(obj, changes, now);
            _sent.Add(batch);
            return batch;
        }

        /// The batch we sent with this change in it: the oldest not yet
        /// undone, else the oldest undone one, else null.
        public SentBatch Find(string obj, SlotChange change)
        {
            SentBatch found = null;
            foreach (var batch in _sent)
            {
                if (!batch.Holds(obj, change))
                {
                    continue;
                }

                if (!batch.Undone)
                {
                    return batch;
                }

                if (found == null)
                {
                    found = batch;
                }
            }

            return found;
        }

        /// The session ended.
        public void Clear()
        {
            _sent.Clear();
        }
    }
}
