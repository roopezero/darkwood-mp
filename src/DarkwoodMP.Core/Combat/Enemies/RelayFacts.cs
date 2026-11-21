namespace DarkwoodMP.Combat.Enemies
{
    /// What the host's state pass looks at for one client and one tracked
    /// enemy (the plugin's NetEnemies.HostUpdate, every 0.05 s).
    public readonly struct RelayFacts
    {
        /// The enemy still exists (Tracked.C isn't destroyed).
        public bool Exists { get; init; }

        /// Its death was timed (Tracked.DiedAt isn't negative), this pass
        /// included.
        public bool Dead { get; init; }

        /// Time.time (scaled) less Tracked.DiedAt, when Dead.
        public Seconds SinceDeath { get; init; }

        /// Its GameObject is activeInHierarchy on the host.
        public bool Active { get; init; }

        /// Where it is (its transform's position).
        public Vec3 Enemy { get; init; }

        /// Where the client's stand-in is (NetPlayerProxy's transform).
        public Vec3 StandIn { get; init; }

        /// The client has it (_known holds its id).
        public bool Known { get; init; }

        /// The client keeps its corpse (_corpseKnown holds its id).
        public bool CorpseKept { get; init; }

        /// Corpses already sent to this client in this pass.
        public int CorpsesSent { get; init; }
    }
}
