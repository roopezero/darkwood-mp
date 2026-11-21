using DarkwoodMP.Net;

namespace DarkwoodMP.World.Doors
{
    /// The door part of MsgType.ObjState (host to a client), after the
    /// ObjRef and the kind byte (1, a door): u8 flags (1 open, 2
    /// barricaded, 4 broken, 8 locked, 16 blocked), i32 health, i32
    /// barricade health, str key type.
    public static class DoorObjState
    {
        public const byte Open = 1;
        public const byte Barricaded = 2;
        public const byte Broken = 4;
        public const byte Locked = 8;
        public const byte Blocked = 16;

        public static void Write(PacketWriter w, in HostDoor door)
        {
            int flags = 0;
            if (door.Status.Opened)
            {
                flags |= Open;
            }

            if (door.Status.Barricaded)
            {
                flags |= Barricaded;
            }

            if (door.Status.Destroyed)
            {
                flags |= Broken;
            }

            if (door.Status.Locked)
            {
                flags |= Locked;
            }

            if (door.Blocked)
            {
                flags |= Blocked;
            }

            w.W.Write((byte)flags);
            w.W.Write(door.Health);
            w.W.Write(door.BarricadeHealth);
            string key = door.KeyType;
            if (key == null)
            {
                key = "";
            }

            w.WriteStr(key);
        }

        public static HostDoor Read(PacketReader r)
        {
            byte f = r.R.ReadByte();
            int health = r.R.ReadInt32();
            int barricade = r.R.ReadInt32();
            string key = r.ReadStr();
            var status = new DoorStatus
            {
                Opened = (f & Open) != 0,
                Barricaded = (f & Barricaded) != 0,
                Destroyed = (f & Broken) != 0,
                Locked = (f & Locked) != 0,
            };

            return new HostDoor
            {
                Status = status,
                Blocked = (f & Blocked) != 0,
                Health = health,
                BarricadeHealth = barricade,
                KeyType = key,
            };
        }
    }
}
