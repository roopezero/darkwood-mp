namespace DarkwoodMP.World.Talk
{
    /// What a TalkLock carries: the NPC, whether our player's dialogue
    /// with them opened (true) or closed, and the name they are locked by
    /// ("" when they go by the ObjRef).
    public readonly struct TalkHold
    {
        public ObjName Npc { get; init; }

        public bool Talking { get; init; }

        public string Name { get; init; }
    }
}
