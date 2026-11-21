namespace DarkwoodMP.Combat.Joiners
{
    /// What an enemy does when a stand-in bumps into it.
    public enum BumpStep
    {
        /// The game's onCollideWith runs as it is.
        Game,

        /// Add the collider to touchingColliders (unless it is there),
        /// then the game's method runs.
        Touch,

        /// Touch, react to the stand-in (reactToCharacter), then the
        /// game's method runs.
        React,

        /// Touch and wake the enemy (wakeup); the game's method doesn't
        /// run.
        Wake,

        /// Touch and start the banshee's attack (initiateBansheeAttack);
        /// the game's method doesn't run.
        Banshee,
    }
}
