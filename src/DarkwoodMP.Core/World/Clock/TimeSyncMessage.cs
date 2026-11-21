using DarkwoodMP.Net;

namespace DarkwoodMP.World.Clock
{
    /// The bytes of MsgType.TimeSync (host to clients): u32 day, u16 minute
    /// of the day. Named apart from the plugin's TimeSync, which sends it.
    public static class TimeSyncMessage
    {
        /// The payload, after Begin(MsgType.TimeSync).
        public static void Write(PacketWriter w, HostClock clock)
        {
            w.W.Write((uint)clock.Day);
            w.W.Write((ushort)clock.Minute);
        }

        /// The host's day and minute.
        public static HostClock Read(PacketReader r)
        {
            int day = (int)r.R.ReadUInt32();
            int minute = r.R.ReadUInt16();
            return new HostClock { Day = day, Minute = minute };
        }
    }
}
