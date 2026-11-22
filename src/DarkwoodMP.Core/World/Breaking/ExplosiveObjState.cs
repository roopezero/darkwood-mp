using DarkwoodMP.Net;

namespace DarkwoodMP.World.Breaking
{
    /// The explosive part of MsgType.ObjState (host to a client), after
    /// the ObjRef and the kind byte (6, an Explodes that isn't an item):
    /// u8 flags (1 gone off).
    public static class ExplosiveObjState
    {
        public const byte WentOff = 1;

        public static void Write(PacketWriter w, bool wentOff)
        {
            int flags = 0;
            if (wentOff)
            {
                flags |= WentOff;
            }

            w.W.Write((byte)flags);
        }

        /// Whether it had gone off in the host's world.
        public static bool Read(PacketReader r)
        {
            return (r.R.ReadByte() & WentOff) != 0;
        }
    }
}
