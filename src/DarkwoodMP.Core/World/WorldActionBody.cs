namespace DarkwoodMP.World
{
    /// One MsgType.WorldAction's payload: what was done (the plugin's
    /// WorldActions.Action), to what, and the action's own numbers.
    public struct WorldActionBody
    {
        /// Flags 1 and 2: the action's own.
        public const byte First = 1;

        public const byte Second = 2;

        /// Flags 4, host to clients: a player did it and sends the
        /// triggers it set off, so applying it doesn't set them off.
        public const byte ByPlayer = 4;

        public byte Action;

        public ObjName Obj;

        public float X;

        public float Y;

        public byte Flags;

        /// Never null once read; written as "" when null.
        public string Text;

        public bool Has(byte flag) => (Flags & flag) != 0;
    }
}
