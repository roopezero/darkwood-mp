namespace DarkwoodMP.Combat.Damage
{
    /// What a client does with a hit on a character in the host's world
    /// (ClientHitRules.Forward).
    public enum ClientHit : byte
    {
        /// Not ours to handle (no NetEnemies, or not ClientMode): the
        /// game's getHit runs.
        Game = 0,

        /// Not one of the host's enemies, but a frozen copy: it isn't
        /// really there. Skipped.
        FrozenCopy,

        /// Not one of the host's enemies: HostileNpcs.ClientHit decides,
        /// and the game's getHit runs unless it took the hit.
        AskNpcs,

        /// The host's enemy, a hit of no damage and no effects. Skipped.
        NoDamage,

        /// Something thrown: the host's copy of the throw hits it.
        /// Skipped.
        Thrown,

        /// A flame that isn't our weapon's: it burns on the host. Skipped.
        OthersFlame,

        /// Not by our player: it happened on the host too. Skipped.
        NotOurs,

        /// Our player's hit: sent to the host as EnemyHit. The game's
        /// getHit is skipped.
        ToHost,
    }
}
