namespace DarkwoodMP.Sync
{
    /// What the movement check decided about one reported position.
    public readonly struct MoveStep
    {
        /// What the host remembers about the player from here on.
        public PlayerMovement Next { get; init; }

        /// Where the player may be: the reported position, or where they
        /// were. The host relays this one to the others.
        public Vec3 Accepted { get; init; }

        public MoveOutcome Outcome { get; init; }

        /// Tell the client to put its player back at Accepted now. At most
        /// once per Window, though every update that is too fast is held.
        public bool SendCorrection { get; init; }

        /// When put back: the speed measured, in units a second.
        public float Speed { get; init; }
    }
}
