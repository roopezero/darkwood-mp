namespace DarkwoodMP.World.Fuel
{
    /// How a client gives back fuel the host didn't take.
    public enum RefundKind : byte
    {
        /// No player, or the fuel wasn't from a can.
        Nothing = 0,

        /// The can was used up and removed: a new gasoline can, dropped
        /// when there's no room.
        NewCan,

        /// Back into the can, up to its most (FuelRules.Refilled).
        Refill,
    }
}
