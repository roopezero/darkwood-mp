namespace DarkwoodMP.Combat.Cues
{
    /// A client's cue arriving at the host (PlayerCues.OnCue).
    public readonly struct CueFacts
    {
        /// Session.Instance isn't null and is the host.
        public bool Hosting { get; init; }

        /// The cue's kind byte.
        public byte Kind { get; init; }

        /// The sender has a stand-in, not null and alive.
        public bool StandInAlive { get; init; }
    }
}
