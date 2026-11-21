using System.Collections.Generic;

namespace DarkwoodMP.World.Owed
{
    /// Host: who had their own part of which trigger, by player key, and
    /// which triggers the host's own player is owed. Its two properties
    /// are the file's (OwedRules.FileName, Newtonsoft JSON), by the same
    /// names. A null id is never owed, given or had.
    public sealed class OwedRecord
    {
        /// Player key (OwedRules) to the ids of the triggers they had
        /// their part of.
        public Dictionary<string, HashSet<string>> Had { get; set; } =
            new Dictionary<string, HashSet<string>>();

        /// The ids of the triggers a joiner set off first: the host's
        /// player is owed its part of them.
        public HashSet<string> HostOwed { get; set; } = new HashSet<string>();

        /// Whether it holds nothing.
        public bool Empty => Had.Count == 0 && HostOwed.Count == 0;

        /// Puts back an empty set for one the file left out (null).
        public void Repair()
        {
            if (Had == null)
            {
                Had = new Dictionary<string, HashSet<string>>();
            }

            if (HostOwed == null)
            {
                HostOwed = new HashSet<string>();
            }
        }

        /// Whether the host's player is owed its part of this trigger.
        public bool IsHostOwed(string id)
        {
            return id != null && HostOwed.Contains(id);
        }

        /// A joiner set off this trigger first: the host's player is owed
        /// its part. True only when it wasn't owed already.
        public bool HostOwe(string id)
        {
            return id != null && HostOwed.Add(id);
        }

        /// The host's player had its part: not owed any more. True when it
        /// was.
        public bool HostHad(string id)
        {
            return id != null && HostOwed.Remove(id);
        }

        /// Whether this player had their part of this trigger.
        public bool Given(string player, string id)
        {
            if (id == null || !Had.TryGetValue(player, out var set))
            {
                return false;
            }

            return set.Contains(id);
        }

        /// This player has their part of this trigger now.
        public void Give(string player, string id)
        {
            if (id == null)
            {
                return;
            }

            if (!Had.TryGetValue(player, out var set))
            {
                set = new HashSet<string>();
                Had[player] = set;
            }

            set.Add(id);
        }
    }
}
