namespace DarkwoodMP.World.Progress
{
    /// What the host looks at, once the morning check has passed, when a
    /// player resets their skills (Progression.TakeReset).
    public readonly struct ResetFacts
    {
        /// The shared record is there (HostSaved, which may start it).
        public bool HasRecord { get; init; }

        /// The record has a reset day for this player.
        public bool HasDay { get; init; }

        /// That day (Saved.Resets[who]).
        public int Day { get; init; }

        /// The host's day (Controller.day; 0 with no Controller).
        public int Today { get; init; }
    }
}
