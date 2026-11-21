namespace DarkwoodMP.Combat.Damage
{
    /// A hit on a character in a client's game (CharacterGetHitFromClient).
    public readonly struct ClientHitFacts
    {
        /// a session (CoopSession.Enemies) exists and NetEnemies.ClientMode.
        public bool Handled { get; init; }

        /// NetEnemies.IsPuppet: the character is the host's enemy.
        public bool Puppet { get; init; }

        /// NetEnemies.Frozen holds it.
        public bool Frozen { get; init; }

        /// getHit's Damage.
        public float Damage { get; init; }

        /// NetEnemies.HitEffects.Count, 0 when it is null.
        public int Effects { get; init; }

        /// World.ThrownHits.Now().
        public bool Thrown { get; init; }

        /// World.ThrownHits.CurrentFlame isn't null.
        public bool Flame { get; init; }

        /// EnemyAreaHits.ByLocalPlayer(that flame), when Flame.
        public bool OurFlame { get; init; }

        /// getHit's byPlayer.
        public bool ByPlayer { get; init; }

        /// attackerTransform isn't null and has a Player.
        public bool PlayerAttacker { get; init; }
    }
}
