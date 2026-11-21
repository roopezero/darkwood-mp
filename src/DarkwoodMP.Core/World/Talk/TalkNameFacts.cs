namespace DarkwoodMP.World.Talk
{
    /// What decides the name an NPC is locked by (TalkLock.UniqueName).
    public readonly struct TalkNameFacts
    {
        /// The NPC's name; null when it has none.
        public string Name { get; init; }

        /// NPC.trader.
        public bool Trader { get; init; }

        /// Its ObjRef is of kind Saved: it's from the shared save.
        public bool Saved { get; init; }
    }
}
