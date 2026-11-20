namespace DarkwoodMP.World.Story
{
    /// The keys and values of the story (FlagsDelta, the plugin's
    /// FlagsSync): a prefix says what each key names, and some values have
    /// a grammar of their own.
    public static class StoryKeys
    {
        /// The host's karma, a whole number. Only the host sends it.
        public const string Karma = "k";

        /// A flag, "f:name", valued FlagValue.
        public const string Flag = "f:";

        /// An NPC's state as JSON without its reputation, "n:name".
        public const string Npc = "n:";

        /// An NPC's reputation, "r:name": a number in the story, sent as
        /// "+change" or "=whole".
        public const string Reputation = "r:";

        /// A trader's after-night reward, "ra:name", valued RewardValue.
        public const string Reward = "ra:";

        /// A dialogue's state as JSON, "d:name".
        public const string Dialogue = "d:";

        /// A journal entry, "je:type".
        public const string Entry = "je:";

        /// A note, "jn:type".
        public const string Note = "jn:";

        /// A key, "jk:type".
        public const string Key = "jk:";

        /// A journal item, "ji:type".
        public const string Item = "ji:";

        /// A place found, "jl:name", valued "".
        public const string Place = "jl:";

        /// A place's direction, "jd:name".
        public const string Direction = "jd:";

        /// The value that says a journal entry was removed.
        public const string Removed = "-";

        /// The mod's own flags start with this. The game's setFlag refuses a
        /// name that isn't in its flag list, so they go straight into
        /// Flags.flagsDict.
        public const string ModPrefix = "dwmp_";

        /// A reputation sent as the change, so that changes made in two
        /// games add up: "+" and the change, which can be negative ("+-5").
        public static string Change(int change) => "+" + change;

        /// A reputation sent whole: "=" and the value as the story has it.
        public static string Whole(string value) => "=" + value;

        /// Whether the key is a journal entry that can go away (handed over
        /// in a dialogue): "je:", "jn:", "jk:" or "ji:" and at least one
        /// more character.
        public static bool IsRemovable(string key)
        {
            if (key.Length <= 3 || key[0] != 'j' || key[2] != ':')
            {
                return false;
            }

            char kind = key[1];
            return kind == 'e' || kind == 'n' || kind == 'k' || kind == 'i';
        }

        /// Whether a journal entry's value (its JSON) is one found in a
        /// dream: it holds "inDream":true. A null value isn't.
        public static bool IsDreamEntry(string value)
        {
            return value != null && value.Contains("\"inDream\":true");
        }

        /// A flag's value: the bool as .NET writes it ("True" or "False"),
        /// "|", and the amount.
        public static string FlagValue(bool on, int amount)
        {
            return on + "|" + amount;
        }

        /// A reward's value: the day, "|", and the amount.
        public static string RewardValue(int day, int amount)
        {
            return day + "|" + amount;
        }

        /// Reads a reward's value as the shell does: split at '|', both
        /// halves int.Parse'd, so a bad value throws (FormatException,
        /// IndexOutOfRangeException) and the shell's catch logs it.
        public static void ReadReward(
            string value,
            out int day,
            out int amount)
        {
            var parts = value.Split('|');
            day = int.Parse(parts[0]);
            amount = int.Parse(parts[1]);
        }

        /// What a key names, for applying it. Malformed is a key the shell's
        /// Apply throws on (too short for its prefix), which it logs and
        /// still relays.
        public static StoryKey KindOf(string key)
        {
            if (key == Karma)
            {
                return StoryKey.Karma;
            }

            if (key.Length == 0)
            {
                return StoryKey.Malformed;
            }

            if (key[0] == 'j')
            {
                if (key.Length < 3)
                {
                    return StoryKey.Malformed;
                }

                return StoryKey.Journal;
            }

            if (key.Length < 2)
            {
                return StoryKey.Malformed;
            }

            switch (key[0])
            {
                case 'f':
                    return StoryKey.Flag;
                case 'n':
                    return StoryKey.Npc;
                case 'd':
                    return StoryKey.Dialogue;
                default:
                    return StoryKey.Unknown;
            }
        }
    }
}
