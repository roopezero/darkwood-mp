namespace DarkwoodMP.World.Story
{
    /// What happens here when someone else set the Being on fire
    /// (EpilogueSync.Burned).
    public enum BurnStep : byte
    {
        /// It was shared already: nothing.
        Ignored = 0,

        /// Our part has no such events: a warning.
        Missing,

        /// The events fired already and fire once: nothing more.
        AlreadyFired,

        /// Fire them here, as a replay.
        Runs,
    }
}
