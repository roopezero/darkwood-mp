namespace DarkwoodMP.World.Throws
{
    /// How a throw or drop flies, as ItemThrown sends it.
    public readonly struct ThrowShot
    {
        /// The Rigidbody's velocity; zero for a drop.
        public Vec3 Velocity { get; init; }

        /// Where it lands (ThrownItem.landTarget); where it is for a drop.
        public Vec3 Target { get; init; }

        /// From the thrower to the target; ThrowRules.DroppedDistance for
        /// a drop.
        public float Distance { get; init; }
    }
}
