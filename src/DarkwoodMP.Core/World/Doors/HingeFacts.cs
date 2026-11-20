namespace DarkwoodMP.World.Doors
{
    /// A door leaf's hinge, for how far the leaf may turn.
    public readonly struct HingeFacts
    {
        /// The leaf (Door.body) has a HingeJoint.
        public bool Present { get; init; }

        /// HingeJoint.useLimits: off once the game called
        /// Door.disableLimit.
        public bool UseLimits { get; init; }

        /// HingeJoint.limits.min, in degrees.
        public float Min { get; init; }

        /// HingeJoint.limits.max, in degrees.
        public float Max { get; init; }
    }
}
