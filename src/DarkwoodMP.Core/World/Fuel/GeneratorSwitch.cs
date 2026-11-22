namespace DarkwoodMP.World.Fuel
{
    /// What a client does to its generator with the host's running state.
    public enum GeneratorSwitch : byte
    {
        Leave = 0,

        /// Item.turnOn on the generator's Item.
        TurnOnItem,

        /// Generator.turnOn: the generator has no Item.
        TurnOnGenerator,

        /// Generator.powerDown.
        PowerDown,
    }
}
