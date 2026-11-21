namespace DarkwoodMP.World.Saves
{
    /// The save book's small choices (SaveBook): who a player is, and
    /// whether a slot's name changed.
    public static class SaveBookRules
    {
        /// A player in the book is the one joining (SaveBook.AddPlayer): by
        /// key when the joiner has one, else by name.
        public static bool SamePlayer(in BookPlayer kept, in BookPlayer joiner)
        {
            if (joiner.Key != "")
            {
                return kept.Key == joiner.Key;
            }

            return kept.Name == joiner.Name;
        }

        /// The name a slot is given (SaveBook.SetName): trimmed, null as
        /// empty.
        public static string SlotName(string typed)
        {
            if (typed == null)
            {
                return "";
            }

            return typed.Trim();
        }

        /// Naming a slot writes the book only when the name changes.
        public static bool Renames(string old, string typed) =>
            old != SlotName(typed);
    }
}
