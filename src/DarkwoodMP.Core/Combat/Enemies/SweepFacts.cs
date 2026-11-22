namespace DarkwoodMP.Combat.Enemies
{
    /// What the client's sweep looks at for one active, enabled character
    /// of its game (the plugin's NetEnemies.ClientSweep).
    public readonly struct SweepFacts
    {
        /// EnemyTrackRules.Eligible of it.
        public bool Eligible { get; init; }

        /// It shows one of the host's enemies (_byChar holds it).
        public bool Copy { get; init; }

        /// FreezeRules.LocalOnly of it.
        public bool Local { get; init; }

        /// NetEnemies.Frozen holds it.
        public bool Frozen { get; init; }
    }
}
