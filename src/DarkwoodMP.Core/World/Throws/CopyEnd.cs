namespace DarkwoodMP.World.Throws
{
    /// What becomes of a copy of another's throw (ThrowRules.Landing).
    public enum CopyEnd
    {
        /// It stays in this game's world, unsaved.
        Stays,

        /// The host keeps it in its save (Core.addToSaveable, dynamic).
        Saved,

        /// Destroyed after its flight (ThrowRules.GoesAfter).
        Goes,
    }
}
