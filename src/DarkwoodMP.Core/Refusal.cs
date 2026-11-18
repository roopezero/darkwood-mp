namespace DarkwoodMP
{
    /// Why the host refused something. None means it didn't. The words for
    /// each reason are in Refusals.Text.
    public enum Refusal : byte
    {
        None = 0,

        /// What the request names isn't in the host's world.
        NotHere,

        /// It takes a key nobody has. The detail is the key's type.
        NoKey,

        /// More damage than any weapon does. The detail is the hit.
        HitTooLarge,
    }
}
