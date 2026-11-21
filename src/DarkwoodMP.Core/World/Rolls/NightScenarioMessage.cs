using DarkwoodMP.Net;

namespace DarkwoodMP.World.Rolls
{
    /// The bytes of MsgType.NightScenario (host to client): str the night
    /// scenario the host chose ("" none).
    public static class NightScenarioMessage
    {
        /// The payload, after Begin(MsgType.NightScenario).
        public static void Write(PacketWriter w, string scenario)
        {
            w.WriteStr(scenario);
        }

        public static string Read(PacketReader r)
        {
            return r.ReadStr();
        }
    }
}
