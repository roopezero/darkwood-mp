namespace DarkwoodMP.World.Traps
{
    /// What a client does with the host's answer to its disarm.
    public enum DisarmOutcome : byte
    {
        /// Refused: the host's ObjState already put the trap as it is
        /// there.
        Nothing = 0,

        /// The host's roll worked: the game's disarm and its message.
        Disarmed,

        /// The host's roll failed, or the requester was away: the failure
        /// message and Item.failDisarm.
        Failed,
    }
}
