namespace DarkwoodMP.World.Nights
{
    /// What the host's check does with a recorded night object.
    public enum LiveStep : byte
    {
        /// Nothing changed.
        Keep = 0,

        /// Its trap changed: send it as Made again.
        Resend,

        /// It is gone: drop it.
        Drop,

        /// It is gone: send Gone, then drop it.
        SendGone,
    }
}
