namespace DarkwoodMP.Combat.Effects
{
    /// One of the host's characters a client shows, on a tick
    /// (EnemyFx.BansheeSight; NetEnemies.PuppetCharacters()).
    public readonly struct BansheeFacts
    {
        /// The character is a banshee.
        public bool Banshee { get; init; }

        /// EnemyAreaHits.PlayerAway().
        public bool Away { get; init; }

        /// Our player is alive.
        public bool Alive { get; init; }

        /// Player.isInSight(its transform, playerCanBeFarAway), the second
        /// from its inSightOfPlayer (false when that is null). The game
        /// reads it only when the three above let it, and so may the
        /// shell.
        public bool InSight { get; init; }
    }
}
