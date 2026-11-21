namespace DarkwoodMP.World.Objects
{
    /// What a client does with the host's answer to its use of a feeder.
    public enum FeederOutcome : byte
    {
        /// The feeder or the player is gone: nothing.
        Nothing = 0,

        /// Refused: the message Playermsg_feederInactive.
        Inactive,

        /// Allowed: Feeder.activate, with Coop.Guards.Replaying set so it
        /// isn't sent again.
        Activates,
    }
}
