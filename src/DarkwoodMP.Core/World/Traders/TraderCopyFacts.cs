namespace DarkwoodMP.World.Traders
{
    /// What the host looks at for one of its copies of a joiner's morning
    /// trader (TraderSync.HostUpdate).
    public readonly struct TraderCopyFacts
    {
        /// The NPC or his Location is gone (destroyed).
        public bool Gone { get; init; }

        /// He has a Character and it isn't alive.
        public bool Dead { get; init; }

        /// A joiner is in a morning (TraderCopyRules.SomeoneInMorning).
        public bool Someone { get; init; }

        /// Time.unscaledTime now.
        public Seconds Now { get; init; }

        /// Time.unscaledTime when the copy was put there.
        public Seconds Since { get; init; }

        /// The host's own player is in a morning (Controller.isAfterNight)
        /// in that hideout (Player.whereAmI.bigLocation).
        public bool OursNow { get; init; }
    }
}
