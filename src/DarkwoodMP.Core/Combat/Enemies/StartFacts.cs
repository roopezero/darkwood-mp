namespace DarkwoodMP.Combat.Enemies
{
    /// What the client looks at as one of its characters starts
    /// (ClientFreezeCharacterPatch, on Character.Start).
    public readonly struct StartFacts
    {
        /// NetEnemies.Thaw is starting it itself
        /// (ClientFreezeCharacterPatch.Starting).
        public bool Thawing { get; init; }

        /// FreezeRules.ClientMode.
        public bool ClientMode { get; init; }

        /// EnemyTrackRules.Eligible of it.
        public bool Eligible { get; init; }

        /// FreezeRules.LocalOnly of it.
        public bool Local { get; init; }
    }
}
