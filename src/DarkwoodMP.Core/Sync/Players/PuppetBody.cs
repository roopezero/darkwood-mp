namespace DarkwoodMP.Sync.Players
{
    /// Another player's puppet, for easing our player out of it.
    public readonly struct PuppetBody
    {
        /// RemotePlayer.Owner.
        public ulong Owner { get; init; }

        /// Its root is there and active (activeSelf).
        public bool Shown { get; init; }

        /// PuppetRules.IsAlive.
        public bool Alive { get; init; }

        /// PuppetRules.IsSpectating.
        public bool Spectating { get; init; }

        /// Its root's position on the ground plane.
        public Vec2 At { get; init; }
    }
}
