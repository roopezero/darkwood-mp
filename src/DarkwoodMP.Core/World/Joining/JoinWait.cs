namespace DarkwoodMP.World.Joining
{
    /// Why the host tells a joiner to wait (JoinWait). The words are in
    /// JoinWaits.Text; the joiner logs them and says Hello again.
    public enum JoinWait
    {
        None = 0,

        /// The prologue's intro film, or the map test's hold.
        Intro,

        /// Between the prologue's stages or in their cutscenes.
        BetweenPrologueParts,

        Dreaming,

        /// In a separate location.
        Away,

        /// The world isn't ready yet.
        Loading,

        /// The host is saving its world for the joiner.
        Preparing,

        /// The joiner asked for a piece of a world it isn't offered.
        WorldChanged,

        /// Between two parts of the shared epilogue.
        BetweenEpilogueParts,
    }
}
