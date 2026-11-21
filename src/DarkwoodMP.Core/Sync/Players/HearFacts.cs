namespace DarkwoodMP.Sync.Players
{
    /// Whether another player's sounds are heard here
    /// (PlayerFx.OnSounds).
    public readonly struct HearFacts
    {
        /// Their puppet is there and active in the hierarchy.
        public bool HasPuppet { get; init; }

        /// WorldShare.InWorld.
        public bool InWorld { get; init; }

        /// Player.Instance isn't null.
        public bool HasPlayer { get; init; }

        /// Our player's position (transform.position).
        public Vec3 Ours { get; init; }

        /// Their puppet's root position.
        public Vec3 Puppet { get; init; }
    }
}
