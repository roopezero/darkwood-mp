namespace DarkwoodMP.World.Talk
{
    /// Since when the open dialogue's NPC has been gone, and switched
    /// off. The zero value is neither.
    public readonly struct DialogueWatch
    {
        public bool Gone { get; init; }

        public Seconds GoneSince { get; init; }

        public bool Off { get; init; }

        public Seconds OffSince { get; init; }
    }
}
