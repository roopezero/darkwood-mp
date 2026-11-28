namespace DarkwoodMP.World.Dreaming
{
    /// What a replayed end of the shared dream does this frame.
    public enum ReplayStep
    {
        /// End the dream now: nothing of ours is open.
        End,

        /// Close our dialogue and menus first, then end the dream.
        CloseFirst,

        /// The dialogue is opening or closing: try again next frame.
        Wait,
    }
}
