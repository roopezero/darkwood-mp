namespace DarkwoodMP.World.Story
{
    /// Another player's journal entries ("je:", "jn:", "jk:", "ji:", "jl:",
    /// "jd:"), and the "journal updated" popups that show them. A kind is
    /// the key's second character: e entry, n note, k key, i item, l place,
    /// d direction.
    public static class JournalRules
    {
        /// What to do with another player's entry, in this order: a removal,
        /// a dream entry (not for places or directions) outside a shared
        /// dream, an entry of a kind the journal has, anything else.
        public static JournalTake Take(
            char kind,
            string value,
            bool sharedDream)
        {
            if (value == StoryKeys.Removed)
            {
                return JournalTake.Remove;
            }

            bool dreamless = kind == 'l' || kind == 'd';
            if (!dreamless && StoryKeys.IsDreamEntry(value) && !sharedDream)
            {
                return JournalTake.OtherDream;
            }

            switch (kind)
            {
                case 'e':
                case 'n':
                case 'k':
                case 'i':
                case 'l':
                case 'd':
                    return JournalTake.Add;
                default:
                    return JournalTake.Unknown;
            }
        }

        /// The popup an added entry shows (Journal.showJournalInfoPopup's
        /// type), or null for none: an entry, a note, and a place unless
        /// the game finds it without a popup (quiet: noDiscoverPopup or
        /// discoveredOnStart). The shell shows it only while the world is
        /// ready.
        public static string Popup(char kind, bool quiet)
        {
            switch (kind)
            {
                case 'e':
                    return "JournalEntry";
                case 'n':
                    return "Note";
                case 'l':
                    if (quiet)
                    {
                        return null;
                    }

                    return "Location";
                default:
                    return null;
            }
        }

        /// What the log calls an entry of this kind.
        public static string Noun(char kind)
        {
            switch (kind)
            {
                case 'e':
                    return "entry";
                case 'n':
                    return "note";
                case 'k':
                    return "key";
                case 'i':
                    return "item";
                case 'l':
                    return "place";
                default:
                    return "direction";
            }
        }

        /// Where the game's text for a popup of this type is. When the game
        /// has none, Language gives back the bare name and the popup would
        /// say "Location found: place_10", so the popup isn't shown.
        public static PopupText TextOf(string type, string desc)
        {
            switch (type)
            {
                case "Location":
                case "LocationAlreadyFound":
                    return new PopupText { Key = desc, Sheet = "UI" };
                case "JournalEntry":
                    return Named(desc, "JournalEntries", null);
                case "Note":
                    return Named(desc, "Notes", "Items");
                case "Key":
                case "InvItem":
                    return Named(desc, "Items", null);
                default:
                    return new PopupText { Always = true };
            }
        }

        static PopupText Named(string desc, string sheet, string other)
        {
            return new PopupText
            {
                Key = desc + "_name",
                Sheet = sheet,
                OtherSheet = other,
            };
        }
    }
}
