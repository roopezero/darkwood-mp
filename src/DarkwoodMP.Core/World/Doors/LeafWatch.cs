namespace DarkwoodMP.World.Doors
{
    /// What this game remembers about one open door's leaf it watches for
    /// pushes (the plugin's DoorPush.Watched). Times are
    /// Time.unscaledTime.
    public readonly struct LeafWatch
    {
        /// The signed angle last sent or heard.
        public float Shared { get; init; }

        /// The leaf is being followed: it swung, or turned away from
        /// Shared, and hasn't come to rest yet.
        public bool Moving { get; init; }

        /// Our player was at the leaf while it moved, so where it rests
        /// is ours to send.
        public bool Touched { get; init; }

        /// The leaf stopped swinging, at RestingSince (the plugin's
        /// StillSince, which is -1 when not resting).
        public bool Resting { get; init; }

        public Seconds RestingSince { get; init; }

        /// Where it rests isn't sent again before this.
        public Seconds NextSend { get; init; }

        /// While it swings, its angle isn't streamed again before this.
        public Seconds NextStream { get; init; }
    }
}
