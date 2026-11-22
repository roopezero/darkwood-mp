namespace DarkwoodMP.World.Scripted
{
    /// Host: where the host's own player is, for the far events.
    public readonly struct HostSpot
    {
        /// The host has a player (Player.Instance isn't null).
        public bool Known { get; init; }

        /// The host's player is in a separate location or a dream
        /// (SharedCullingPatch.HostAway).
        public bool Away { get; init; }

        /// On the ground plane.
        public Vec2 Pos { get; init; }
    }
}
