using System;

namespace DarkwoodMP.Combat.Damage
{
    /// Which of EnemyAreaHits' sets a spawned object goes in. It can go in
    /// both.
    [Flags]
    public enum SpawnSets : byte
    {
        None = 0,

        /// What the host's enemies spawned (EnemySpawned).
        Enemy = 1,

        /// What the local player's weapon spawned (PlayerSpawned).
        Player = 2,
    }
}
