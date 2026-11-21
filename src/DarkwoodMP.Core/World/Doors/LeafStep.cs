namespace DarkwoodMP.World.Doors
{
    /// What LeafWatchRules.Step decided for one leaf in one frame.
    public readonly struct LeafStep
    {
        /// What to remember about the leaf from here on.
        public LeafWatch Next { get; init; }

        public LeafMove Move { get; init; }

        /// The angle to send (Stream, Send) or to glide back to
        /// (SwingBack).
        public float Angle { get; init; }
    }
}
