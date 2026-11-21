using DarkwoodMP.Net;

namespace DarkwoodMP.World.Places
{
    /// The bytes of MsgType.LocationAsk (client -> host): u8 flags (1 a
    /// dream), str the location's name.
    public static class LocationAskMessage
    {
        /// The payload, after Begin(MsgType.LocationAsk).
        public static void Write(PacketWriter w, in LocationAsk ask)
        {
            byte flags = 0;
            if (ask.Dream)
            {
                flags = 1;
            }

            w.W.Write(flags);
            w.WriteStr(ask.Name);
        }

        public static LocationAsk Read(PacketReader r)
        {
            bool dream = (r.R.ReadByte() & 1) != 0;
            string name = r.ReadStr();
            return new LocationAsk { Dream = dream, Name = name };
        }
    }
}
