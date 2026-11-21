namespace DarkwoodMP.World.Joining
{
    /// What the host does with a joiner's Hello.
    public enum AdmitStep
    {
        /// Refused (Refusal says why).
        Refuse = 0,

        /// Told to wait (JoinWait says why).
        Wait,

        /// Let in with the world it was offered.
        LetInOffered,

        /// Make a world for it (PrepareWorld), and tell it to wait.
        Prepare,

        /// Let in with the fresh world, which it has.
        LetIn,

        /// Offered the fresh world.
        Offer,
    }
}
