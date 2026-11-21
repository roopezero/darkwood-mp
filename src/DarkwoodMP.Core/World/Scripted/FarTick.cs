namespace DarkwoodMP.World.Scripted
{
    /// Host, each culling refresh: what one entered location's events do
    /// as the host's own player comes or goes.
    public enum FarTick : byte
    {
        /// The location isn't entered: remove the record.
        Forget = 0,

        /// Its enter events haven't run yet: nothing.
        Wait,

        /// First sight: store whether the host's player is in it, without
        /// the margin (FarEventRules.HostNear with was false).
        Record,

        /// No change.
        Same,

        /// The host's player came into it: store that, and run its enter
        /// events as the player's part only.
        Came,

        /// The host's player went out of it: store that, and run its exit
        /// events as the player's part only.
        Went,
    }
}
