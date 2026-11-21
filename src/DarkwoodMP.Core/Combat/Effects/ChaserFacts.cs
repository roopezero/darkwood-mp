namespace DarkwoodMP.Combat.Effects
{
    /// One of the host's active characters and one joiner's stand-in
    /// (EnemyFx.HostCombat; AllCharacters.Active()).
    public readonly struct ChaserFacts
    {
        /// The character isn't null and is alive.
        public bool Alive { get; init; }

        /// NetEnemies.Eligible: the host runs it as an enemy.
        public bool Enemy { get; init; }

        /// behaviour is Behaviour.chasingTarget.
        public bool Chasing { get; init; }

        /// Its target is the stand-in's transform.
        public bool OnStandIn { get; init; }
    }
}
