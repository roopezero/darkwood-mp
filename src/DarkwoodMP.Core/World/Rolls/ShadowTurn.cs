namespace DarkwoodMP.World.Rolls
{
    /// Who turns the shadows' lights off (RollRules.ShadowLights).
    public enum ShadowTurn : byte
    {
        /// The game's own Player.shadowLightsTurnOff.
        Game = 0,

        /// A joiner in the shared world: nothing, it waits for the host's.
        Skip,

        /// The host picks the lamp and sends it.
        HostRolls,
    }
}
