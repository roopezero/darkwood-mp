using DarkwoodMP.Net;

namespace DarkwoodMP.World.Stacks
{
    /// The bytes of MsgType.StackSize (host to client): u16 size, the
    /// host's stack size (0: the game's own sizes).
    public static class StackSize
    {
        /// The payload, after Begin(MsgType.StackSize): the size clamped
        /// to 0 to StackRules.MostOnWire.
        public static void Write(PacketWriter w, int size)
        {
            w.W.Write(StackRules.OnWire(size));
        }

        /// The host's size.
        public static int Read(PacketReader r)
        {
            return r.R.ReadUInt16();
        }
    }
}
