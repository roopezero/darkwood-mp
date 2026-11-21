namespace DarkwoodMP.World.Dragging
{
    /// What our game does with its drag when the host answers its claim
    /// or tells it who drags the thing.
    public enum ClaimReply : byte
    {
        /// Nothing: our player let go meanwhile, or was made to, or the
        /// news is about something else.
        Ignore = 0,

        /// Ours: claimed, and its moves go out at once.
        Claimed,

        /// Let go at once, showing the host's words.
        ShowReason,

        /// Let go at once, showing the game's "too far"
        /// (Playermsg_tooFar).
        TooFar,

        /// Let go at once, showing "NAME is moving it" for the owner the
        /// host named.
        Moving,
    }
}
