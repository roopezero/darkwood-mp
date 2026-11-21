namespace DarkwoodMP.Combat.Damage
{
    /// An enemy's melee sensor touching something
    /// (MeleeSensorProxyPatch.Prefix).
    public readonly struct MeleeFacts
    {
        /// The collider isn't null and has a NetPlayerProxy.
        public bool StandIn { get; init; }

        /// sensor.type is MeleeSensorType.character.
        public bool EnemySensor { get; init; }

        /// sensor.attackerTransform isn't null.
        public bool Attacker { get; init; }

        /// sensor.collidersToIgnore holds the collider already.
        public bool AlreadyHit { get; init; }

        /// sensor.doesNotNeedLineOfSight.
        public bool NoSightNeeded { get; init; }

        /// Core.canSeeAttack(attacker, the stand-in). The game reads it
        /// only when NoSightNeeded is false, and so may the shell.
        public bool Sees { get; init; }

        /// sensor.damage.
        public float Damage { get; init; }

        /// The attacker has a CharBase.
        public bool Strength { get; init; }

        /// Its strengthModifier, when Strength.
        public float Modifier { get; init; }
    }
}
