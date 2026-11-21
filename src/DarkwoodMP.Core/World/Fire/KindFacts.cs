namespace DarkwoodMP.World.Fire
{
    /// Which of the game's components a burning GameObject has.
    public readonly struct KindFacts
    {
        public bool Player { get; init; }

        /// A remote player's stand-in (NetPlayerProxy).
        public bool Proxy { get; init; }

        public bool Character { get; init; }

        public bool Door { get; init; }

        public bool Window { get; init; }

        public bool Item { get; init; }
    }
}
