namespace DarkwoodMP.World.Fuel
{
    /// What Generator.drainFuel does with the player's electrician
    /// modifier for the call.
    public enum DrainStart : byte
    {
        /// The game's own modifier stays.
        Leave = 0,

        /// A client: 0. Nothing burns there; the host's level comes in.
        Zero,

        /// The host with players in: the nearest player's skill
        /// (DrainRules.Nearest).
        Nearest,
    }
}
