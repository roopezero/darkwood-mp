namespace DarkwoodMP.World.Talk
{
    /// One try at closing a refused dialogue.
    public enum CloseTry
    {
        /// Stop trying: the window has changed, or it is still opening
        /// with no tries left.
        Stop,

        /// It is still opening: try again in TalkRules.RetryEvery, with
        /// one try less.
        Wait,

        /// Close it (DialogueGuard.Close). If the window still has the NPC
        /// after that, try again in RetryEvery with one try less; the
        /// tries left aren't looked at then.
        Close,
    }
}
