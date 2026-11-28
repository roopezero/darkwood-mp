namespace DarkwoodMP.World.Places
{
    /// Our player's dream, against the shared one.
    public readonly struct DreamFacts
    {
        /// Our player dreams (Dreams exists and dreaming).
        public bool Dreaming { get; init; }

        /// Dreams.dreamLocation exists.
        public bool HasLocation { get; init; }

        /// The shared level dream's preset name (DreamSync.Shared); null
        /// for none.
        public string Shared { get; init; }

        /// Dreams.preset exists.
        public bool HasPreset { get; init; }

        /// Its name.
        public string Preset { get; init; }

        /// The epilogue is played together (EpilogueSync.Playing): Shared
        /// stays its first part's name while the parts move on.
        public bool Epilogue { get; init; }
    }
}
