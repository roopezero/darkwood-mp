namespace DarkwoodMP.World.Doors
{
    /// Where a door's leaf is on the ground, for how near a player is to
    /// it.
    public readonly struct LeafShape
    {
        /// The leaf (Door.body) has a HingeJoint.
        public bool HasHinge { get; init; }

        /// HingeJoint.connectedAnchor's x and z, when HasHinge.
        public Vec2 Hinge { get; init; }

        /// The leaf's centre: Door.body.position's x and z.
        public Vec2 Centre { get; init; }
    }
}
