namespace DarkwoodMP.World.Talk
{
    /// How a dialogue is closed the game's way.
    public enum DialogueClose
    {
        /// No window, or it is opening or closing: nothing (false).
        Busy,

        /// Its NPC was destroyed: ForceClose(w, "destroyed") (true).
        Force,

        /// Its NPC was cleared, a close on its way: nothing (false).
        Cleared,

        /// A trade: closeTrade(), then as Close (true).
        CloseTrade,

        /// Set the menu to gossip and call close(); ForceClose(w, "close
        /// failed") if that throws (true).
        Close,
    }
}
