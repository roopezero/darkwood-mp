namespace DarkwoodMP.World.Doors
{
    /// What this game does with a watched leaf this frame.
    public enum LeafMove
    {
        /// Nothing.
        None,

        /// It's swinging: send its angle now, so it swings in the other
        /// games too.
        Stream,

        /// It came to rest away from where it's shared: send where.
        Send,

        /// A client's leaf that something other than our player turned:
        /// glide it back to where it's shared.
        SwingBack,
    }
}
