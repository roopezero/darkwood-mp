namespace DarkwoodMP.World.Nights
{
    /// What a client does with the host's night object.
    public enum ApplyStep : byte
    {
        /// Gone, and we have it: destroy ours, as a replay.
        Destroy = 0,

        /// Gone, and we haven't got it: a log line only.
        Forget,

        /// We have it already: only its trap's state.
        ArmOnly,

        /// No night event step here makes it: a warning.
        NoPrefab,

        /// Made before we came: look for our copy from the save
        /// (NightObjectRules.NearestSaved), else make it.
        MatchSaved,

        /// Make it, as a replay.
        Make,
    }
}
