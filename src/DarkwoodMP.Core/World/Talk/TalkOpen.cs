namespace DarkwoodMP.World.Talk
{
    /// What happens when our player's dialogue opens.
    public enum TalkOpen
    {
        /// Nobody else can talk to them: not shared, a dream's NPC of our
        /// own, or one with no ObjRef. Nothing is kept.
        NotShared,

        /// Host: keep it as ours, and take its lock at once.
        Take,

        /// Client: keep it as ours, and ask the host with a Request. The
        /// dialogue is open already: that is the prediction.
        Ask,
    }
}
