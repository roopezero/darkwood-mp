namespace DarkwoodMP.World.Places
{
    /// What a joiner's place names.
    public enum PlaceKind : byte
    {
        /// The shared world, or a place unknown ("?"): nothing the host runs
        /// for them.
        None = 0,

        /// A dream ("dream:" and its grid).
        Dream,

        /// A separate location, by name.
        Location,
    }
}
