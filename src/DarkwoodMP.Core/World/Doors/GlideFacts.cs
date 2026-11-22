namespace DarkwoodMP.World.Doors
{
    /// One frame of a leaf this game is turning to another game's angle
    /// (the plugin's DoorPush.Glides). Times are Time.unscaledTime.
    public readonly struct GlideFacts
    {
        /// The door: Exists, HasBody, Opened and Destroyed are read.
        public LeafDoorFacts Door { get; init; }

        /// The leaf's signed angle now.
        public float Angle { get; init; }

        /// The newest angle heard.
        public float Target { get; init; }

        /// When it was heard.
        public Seconds Heard { get; init; }

        public Seconds Now { get; init; }

        /// Time.unscaledDeltaTime.
        public Seconds Delta { get; init; }
    }
}
