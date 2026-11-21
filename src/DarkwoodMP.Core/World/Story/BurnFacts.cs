namespace DarkwoodMP.World.Story
{
    /// Someone else set the Being on fire (EpilogueSync.Burned).
    public readonly struct BurnFacts
    {
        /// The fire was shared already (EpilogueSync._burnShared).
        public bool Shared { get; init; }

        /// A GameEvents named EpilogueRules.BurnEvents is in our game,
        /// inactive ones too.
        public bool Found { get; init; }

        /// Its fired.
        public bool Fired { get; init; }

        /// Its multipleFire.
        public bool Multiple { get; init; }
    }
}
