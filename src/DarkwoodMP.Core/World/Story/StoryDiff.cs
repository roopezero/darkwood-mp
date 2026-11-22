using System.Collections.Generic;

namespace DarkwoodMP.World.Story
{
    /// What changed in the story between two looks, and what a game's
    /// baseline (the story as last sent or got) becomes. The shell keeps
    /// the baseline; these work on the dictionaries it gives.
    public static class StoryDiff
    {
        /// The changes since the last look, to send to the others: each
        /// entry that is new or different (a reputation as the change when
        /// both values are whole numbers, else whole), then each removable
        /// journal entry that went, unless it was found in a dream and this
        /// game isn't in a shared dream (every game drops its own).
        public static List<KeyValuePair<string, string>> Changes(
            IReadOnlyDictionary<string, string> was,
            IReadOnlyDictionary<string, string> now,
            bool sharedDream)
        {
            var changed = new List<KeyValuePair<string, string>>();
            foreach (var kv in now)
            {
                bool had = was.TryGetValue(kv.Key, out var old);
                if (had && old == kv.Value)
                {
                    continue;
                }

                if (!kv.Key.StartsWith(StoryKeys.Reputation))
                {
                    changed.Add(kv);
                    continue;
                }

                changed.Add(Pair(kv.Key, Reputation(had, old, kv.Value)));
            }

            changed.AddRange(Removed(was, now, sharedDream));
            return changed;
        }

        /// The host's changes since a joiner's snapshot, to send to that
        /// joiner: as Changes, but a reputation always goes whole.
        public static List<KeyValuePair<string, string>> SinceSnapshot(
            IReadOnlyDictionary<string, string> at,
            IReadOnlyDictionary<string, string> now,
            bool sharedDream)
        {
            var changed = new List<KeyValuePair<string, string>>();
            foreach (var kv in now)
            {
                if (at.TryGetValue(kv.Key, out var old) && old == kv.Value)
                {
                    continue;
                }

                if (!kv.Key.StartsWith(StoryKeys.Reputation))
                {
                    changed.Add(kv);
                    continue;
                }

                changed.Add(Pair(kv.Key, StoryKeys.Whole(kv.Value)));
            }

            changed.AddRange(Removed(at, now, sharedDream));
            return changed;
        }

        /// A reputation's new baseline value after a change every game makes
        /// by itself (or another message carries), so it isn't sent: the old
        /// one plus the change, or null to leave it as it is (no change, no
        /// entry, not a whole number).
        public static string Absorbed(string was, int delta)
        {
            if (delta == 0 || was == null)
            {
                return null;
            }

            if (!int.TryParse(was, out int old))
            {
                return null;
            }

            return (old + delta).ToString();
        }

        /// The baseline's value for a key another game sent, once applied
        /// here, or null to take the key out of it. A removal leaves it; a
        /// journal entry is our own journal's (own, null when we don't have
        /// it), which can differ from theirs; anything else is the value
        /// sent, known now and not ours to send back.
        public static string AfterTaking(
            string key,
            string value,
            string own)
        {
            if (!StoryKeys.IsRemovable(key))
            {
                return value;
            }

            if (value == StoryKeys.Removed)
            {
                return null;
            }

            return own;
        }

        static string Reputation(bool had, string was, string now)
        {
            if (had && int.TryParse(was, out int old)
                && int.TryParse(now, out int value))
            {
                return StoryKeys.Change(value - old);
            }

            return StoryKeys.Whole(now);
        }

        static List<KeyValuePair<string, string>> Removed(
            IReadOnlyDictionary<string, string> was,
            IReadOnlyDictionary<string, string> now,
            bool sharedDream)
        {
            var removed = new List<KeyValuePair<string, string>>();
            foreach (var kv in was)
            {
                if (!StoryKeys.IsRemovable(kv.Key) || now.ContainsKey(kv.Key))
                {
                    continue;
                }

                if (sharedDream || !StoryKeys.IsDreamEntry(kv.Value))
                {
                    removed.Add(Pair(kv.Key, StoryKeys.Removed));
                }
            }

            return removed;
        }

        static KeyValuePair<string, string> Pair(string key, string value)
        {
            return new KeyValuePair<string, string>(key, value);
        }
    }
}
