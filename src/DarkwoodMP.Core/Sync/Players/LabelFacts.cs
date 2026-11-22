namespace DarkwoodMP.Sync.Players
{
    /// One puppet's name tag (PlayerSync.DrawNameTags).
    public readonly struct LabelFacts
    {
        /// Session.NameOf(owner), or "Dummy" for the dummy.
        public string Name { get; init; }

        /// The puppet is the tests' dummy (PuppetRules.DummyId).
        public bool Dummy { get; init; }

        /// A snapshot has arrived.
        public bool HasLast { get; init; }

        /// PuppetRules.IsAlive.
        public bool Alive { get; init; }

        /// The newest snapshot's health percent.
        public byte Health { get; init; }

        /// Scaled Time.time.
        public Seconds Now { get; init; }

        /// When the dummy's damage stops showing (FlashUntil).
        public Seconds FlashUntil { get; init; }

        /// The damage the dummy took since its total went back to 0.
        public float Damage { get; init; }
    }
}
