namespace DarkwoodMP.World.Fuel
{
    /// What holds the fuel, as the Fuel message's u8 says it. The values
    /// are on the wire, so they never change.
    public enum FuelTank : byte
    {
        Generator = 1,

        Saw = 2,
    }
}
