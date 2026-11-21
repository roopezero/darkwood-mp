namespace DarkwoodMP.Combat.Joiners
{
    /// Which line the host logs when a stand-in's presence changes.
    public enum PresenceNote
    {
        /// "{n} is back in the world".
        Back,

        /// "{n} is away (...): the host's enemies leave them be".
        Away,

        /// "{n} is in {place}, which we run for them: ...".
        InLocation,
    }
}
