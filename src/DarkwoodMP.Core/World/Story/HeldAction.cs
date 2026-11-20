namespace DarkwoodMP.World.Story
{
    /// What the host does with its held story step this frame.
    public enum HeldAction : byte
    {
        /// Keep holding it.
        Keep = 0,

        /// Its player left, or this game isn't the host now: drop it.
        Dropped,

        /// It ran meanwhile: drop it.
        Done,

        /// It doesn't fit the story now: drop it, with a warning.
        Refused,

        /// Run it now, and drop it.
        Run,
    }
}
