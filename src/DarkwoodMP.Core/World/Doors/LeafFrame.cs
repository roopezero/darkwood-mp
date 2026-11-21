namespace DarkwoodMP.World.Doors
{
    /// One frame of a watched leaf, for LeafWatchRules.Step: a door
    /// still watched and not held (LeafWatchRules.Unwatches and Waits
    /// said no).
    public readonly struct LeafFrame
    {
        /// What was remembered about the leaf.
        public LeafWatch Watch { get; init; }

        /// Time.unscaledTime.
        public Seconds Now { get; init; }

        /// The leaf's signed angle now (LeafRules.Signed).
        public float Angle { get; init; }

        /// The leaf's Rigidbody.angularVelocity.y.
        public float Spin { get; init; }

        /// This game is the host: it sends wherever the leaf rests,
        /// whatever turned it.
        public bool Host { get; init; }

        /// The leaf on the ground, and our player's position.
        public LeafShape Shape { get; init; }

        public Vec2 Player { get; init; }

        /// Map test: count a push of this door as our player's, wherever
        /// the player stands (DoorPush.TestTouch is this door).
        public bool TestTouch { get; init; }

        /// The door has a name the other games know (ObjRef.Of is valid):
        /// without one, nothing is sent.
        public bool Named { get; init; }
    }
}
