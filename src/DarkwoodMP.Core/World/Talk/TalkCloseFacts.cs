namespace DarkwoodMP.World.Talk
{
    /// Client, after the host refused a talk: one try at closing the
    /// dialogue (TalkLock.CloseWhenReady).
    public readonly struct TalkCloseFacts
    {
        /// The dialogue window exists and still has this NPC, which is
        /// there.
        public bool SameNpc { get; init; }

        /// The window is still opening: DialogueWindow.tweening, no
        /// Player, or not Player.inDialogue yet.
        public bool Opening { get; init; }

        /// Tries left, 40 at first.
        public int Tries { get; init; }
    }
}
