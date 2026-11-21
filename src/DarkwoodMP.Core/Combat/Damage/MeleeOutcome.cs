namespace DarkwoodMP.Combat.Damage
{
    /// What an enemy's melee sensor does to what it touched
    /// (MeleeRules.Touch).
    public enum MeleeOutcome : byte
    {
        /// Not a stand-in: the game's own OnTriggerEnter runs.
        Game = 0,

        /// A player's sensor, or one with no attacker: players don't hit
        /// players. The game's method is skipped.
        NotAnEnemyHit,

        /// The sensor hit this stand-in's collider already. Skipped.
        AlreadyHit,

        /// The attacker can't see the stand-in. The collider is added to
        /// the sensor's ignore list first; skipped.
        Unseen,

        /// It hits: the collider is added to the ignore list, the
        /// sensor's onHit runs and the damage goes to the player.
        Hits,
    }
}
