using DarkwoodMP.Net;

namespace DarkwoodMP.World.Breaking
{
    /// The item part of MsgType.ObjState (host to a client), after the
    /// ObjRef and the kind byte (4, an item): u8 flags (1 switched on, 2
    /// locked, 4 searched, 8 broken, 16 its InteractiveItem is on, 32 its
    /// feeder is full), i32 health.
    public static class ItemObjState
    {
        public const byte On = 1;
        public const byte Locked = 2;
        public const byte Searched = 4;
        public const byte Broken = 8;
        public const byte Switched = 16;
        public const byte FeederFull = 32;

        public static void Write(PacketWriter w, in HostItem item)
        {
            int flags = 0;
            if (item.On)
            {
                flags |= On;
            }

            if (item.Locked)
            {
                flags |= Locked;
            }

            if (item.Searched)
            {
                flags |= Searched;
            }

            if (item.Broken)
            {
                flags |= Broken;
            }

            if (item.Switched)
            {
                flags |= Switched;
            }

            if (item.FeederFull)
            {
                flags |= FeederFull;
            }

            w.W.Write((byte)flags);
            w.W.Write(item.Health);
        }

        public static HostItem Read(PacketReader r)
        {
            byte f = r.R.ReadByte();
            int health = r.R.ReadInt32();
            return new HostItem
            {
                On = (f & On) != 0,
                Locked = (f & Locked) != 0,
                Searched = (f & Searched) != 0,
                Broken = (f & Broken) != 0,
                Switched = (f & Switched) != 0,
                FeederFull = (f & FeederFull) != 0,
                Health = health,
            };
        }
    }
}
