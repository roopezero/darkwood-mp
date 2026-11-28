namespace DarkwoodMP.World.Scripted
{
    /// Another player's own event in a later part of the epilogue (EventFired
    /// kinds 7 and 8), as the game it reaches sees it.
    public struct OwnEventFacts
    {
        /// This game is the host.
        public bool Host;

        /// This game has the trigger or the world event.
        public bool Known;

        /// It is in the later part of the epilogue our player is in.
        public bool InOurPart;
    }
}
