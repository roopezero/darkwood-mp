namespace DarkwoodMP.World.Dragging
{
    /// Where our copy of a thing our player had to let go of may be held
    /// (DragSync.CancelLocal).
    public readonly struct HoldFacts
    {
        /// It is shown following another game's move already (_moving).
        public bool Shown { get; init; }

        /// The host's last word on it while we held it (_hostAt).
        public bool HostWord { get; init; }

        /// It is watched as loose, with where it is known to be (_loose).
        public bool Known { get; init; }
    }
}
