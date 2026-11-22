namespace DarkwoodMP.World.Traps
{
    /// What a game knows when its player starts a disarm (Player.doDisarm),
    /// to say whether co-op makes the player crouch for it.
    public readonly struct CrouchFacts
    {
        /// CoopPause.InCoop: in a world, with someone to play with.
        public bool InCoop { get; init; }

        /// The item to disarm isn't null.
        public bool HasItem { get; init; }

        /// Player.disarmingTrap, after the game's doDisarm.
        public bool Disarming { get; init; }

        /// The item has a Trigger.
        public bool HasTrigger { get; init; }

        /// Trigger.bendDownToDisarm: the game crouches for it already.
        public bool BendsDown { get; init; }
    }
}
