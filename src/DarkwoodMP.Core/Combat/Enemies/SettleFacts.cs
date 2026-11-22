namespace DarkwoodMP.Combat.Enemies
{
    /// What the client's frame looks at for one copy before drawing it
    /// (the plugin's NetEnemies.MovePuppets).
    public readonly struct SettleFacts
    {
        /// Its character exists.
        public bool Exists { get; init; }

        /// It died here (Puppet.DiedAt isn't negative).
        public bool Dead { get; init; }

        /// Its corpse was made already (Puppet.Settled).
        public bool Settled { get; init; }

        /// Time.unscaledTime less Puppet.DiedAt, when Dead.
        public Seconds SinceDeath { get; init; }
    }
}
