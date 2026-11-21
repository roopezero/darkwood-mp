namespace DarkwoodMP.World.Dragging
{
    /// One thing shown following another game's move, this frame
    /// (DragSync.ShowOthers, an entry of _moving).
    public readonly struct GlideFacts
    {
        /// How far it is from where the move puts it (3D).
        public float Off { get; init; }

        /// Degrees between its turn and the move's (Quaternion.Angle).
        public float Turn { get; init; }

        /// The move is where it came to rest (the message's last).
        public bool Last { get; init; }

        /// When the move was heard.
        public Seconds Heard { get; init; }

        /// Time.unscaledTime.
        public Seconds Now { get; init; }
    }
}
