namespace DarkwoodMP.Combat.Joiners
{
    /// The scripted brother fighting a stand-in
    /// (Character.checkBrotherWeakAttack on the host).
    public readonly struct BrotherFacts
    {
        /// The stand-in's player's health in percent
        /// (NetPlayerProxy.HealthPercent).
        public int HealthPercent { get; init; }

        /// Player.Instance isn't null.
        public bool HasHost { get; init; }

        /// The host's player's maxHealth.
        public float HostMaxHealth { get; init; }

        /// How many attacks the brother has (attacks.Count).
        public int Attacks { get; init; }
    }
}
