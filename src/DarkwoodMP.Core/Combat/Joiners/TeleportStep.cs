namespace DarkwoodMP.Combat.Joiners
{
    /// What the forest spirit's waitToTeleport does on the host.
    public enum TeleportStep
    {
        /// The game's method runs.
        Game,

        /// Nothing: the game's method is skipped.
        Skip,

        /// Teleport to JoinerSightRules.SpotOf; the game's method is
        /// skipped.
        Teleport,
    }
}
