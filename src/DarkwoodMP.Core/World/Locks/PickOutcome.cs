namespace DarkwoodMP.World.Locks
{
    /// How the host's roll for a client's pick of a crate went.
    public enum PickOutcome : byte
    {
        /// The pick failed: the answer is "0" and nothing changes.
        Failed = 0,

        /// The crate was searched already: the answer is "1", and nothing
        /// changes or is passed on.
        AlreadyOpen,

        /// The pick worked: the answer is "1", the crate is searched now,
        /// and the others hear it as ItemSearched.
        Opened,
    }
}
