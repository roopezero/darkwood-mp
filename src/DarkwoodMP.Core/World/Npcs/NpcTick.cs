namespace DarkwoodMP.World.Npcs
{
    /// What NpcPlaceRules.Tick decided.
    public readonly struct NpcTick
    {
        /// Look at the NPCs and send those that moved.
        public bool Sends { get; init; }

        /// Find the NPCs again first, and send all of them.
        public bool All { get; init; }

        /// The timers to keep.
        public NpcTimers Timers { get; init; }
    }
}
