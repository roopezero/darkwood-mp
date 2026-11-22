namespace DarkwoodMP.World.Rolls
{
    /// A MsgType.NightEvent as read (NightEventMessage.Read). Only the
    /// fields of its Kind are set.
    public readonly struct NightEventBody
    {
        /// NightEventMessage.Fired or NightEventMessage.ShadowLights; any
        /// other byte is kept, with nothing after it read.
        public byte Kind { get; init; }

        /// Fired: the night event.
        public NightFired Fired { get; init; }

        /// ShadowLights: the generator.
        public ObjName Generator { get; init; }

        /// ShadowLights: the lamp that stays on.
        public ObjName Lamp { get; init; }
    }
}
