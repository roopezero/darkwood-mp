namespace DarkwoodMP.World.Skills
{
    /// The mouse in one frame, for PanelPress. Buttons 0, 1 and 2 are bits
    /// 0, 1 and 2 of Down and Held.
    public readonly struct MouseFacts
    {
        /// Time.frameCount.
        public int Frame { get; init; }

        /// The mouse is over the panel's window (screen y flipped).
        public bool Over { get; init; }

        /// Input.GetMouseButtonDown, one bit a button.
        public int Down { get; init; }

        /// Input.GetMouseButton, one bit a button.
        public int Held { get; init; }
    }
}
