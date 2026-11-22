using DarkwoodMP.Net;

namespace DarkwoodMP.World.Places
{
    /// The bytes of MsgType.LocationSlot (host -> client): str the
    /// location's name, vec3 its marker's spot, i16 its turn in degrees.
    public static class LocationSlotMessage
    {
        /// The payload, after Begin(MsgType.LocationSlot).
        public static void Write(PacketWriter w, in LocationSlot slot)
        {
            w.WriteStr(slot.Name);
            w.Write(slot.Spot.Pos);
            w.W.Write((short)slot.Spot.Turn);
        }

        public static LocationSlot Read(PacketReader r)
        {
            string name = r.ReadStr();
            Vec3 pos = r.ReadVec3();
            int turn = r.R.ReadInt16();
            return new LocationSlot
            {
                Name = name,
                Spot = new Spot { Pos = pos, Turn = turn },
            };
        }
    }
}
