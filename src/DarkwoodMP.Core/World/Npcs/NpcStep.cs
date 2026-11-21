namespace DarkwoodMP.World.Npcs
{
    /// What NpcPlaceRules.Follow decided.
    public readonly struct NpcStep
    {
        public NpcMove Move { get; init; }

        /// For Snap and Step: where the copy goes.
        public Vec3 To { get; init; }

        /// For Leave, Snap and Step: how far the copy was from the host's
        /// on the ground plane.
        public float Distance { get; init; }
    }
}
