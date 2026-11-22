namespace DarkwoodMP.World.Npcs
{
    /// Host: when the next NpcPos sends are due (unscaled). Zero sends
    /// every NPC at the first tick.
    public readonly struct NpcTimers
    {
        /// The next send of the NPCs that moved.
        public Seconds NextMoved { get; init; }

        /// The next send of all of them, which finds them again first.
        public Seconds NextAll { get; init; }
    }
}
