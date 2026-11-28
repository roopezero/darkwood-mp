using DarkwoodMP.Net;

namespace DarkwoodMP.World.Story
{
    /// The bytes of MsgType.EpilogueStep: u8 the step, str the value; the
    /// host's Burn adds u64 the player who set the fire. Each Write writes
    /// the payload after Begin(MsgType.EpilogueStep).
    public static class EpilogueStepMessage
    {
        /// Host: the pan over the burning forest runs (value: its world
        /// event).
        public static void WritePan(PacketWriter w)
        {
            Write(w, EpilogueStep.Pan, EpilogueRules.PanEvent);
        }

        /// Host: its outcomes, "1" the forest burned, "0" it didn't.
        public static void WriteOutcomes(PacketWriter w, bool burned)
        {
            var value = "0";
            if (burned)
            {
                value = "1";
            }

            Write(w, EpilogueStep.Outcomes, value);
        }

        /// Joiner: our player crawled to their death (value empty).
        public static void WriteCrawled(PacketWriter w)
        {
            Write(w, EpilogueStep.Crawled, "");
        }

        /// Joiner: our player set the Being on fire (value: the crater's
        /// events).
        public static void WriteBurn(PacketWriter w)
        {
            Write(w, EpilogueStep.Burn, EpilogueRules.BurnEvents);
        }

        /// Host: this player set the Being on fire.
        public static void WriteBurnBy(PacketWriter w, ulong who)
        {
            WriteBurn(w);
            w.W.Write(who);
        }

        /// Joiner: the layout of this part, please (value: its preset name).
        public static void WriteLayoutAsk(PacketWriter w, string part)
        {
            Write(w, EpilogueStep.LayoutAsk, part);
        }

        /// Host, to a joiner it let in: our epilogue is on, in this part
        /// (value: its preset name).
        public static void WriteJoinPart(PacketWriter w, string part)
        {
            Write(w, EpilogueStep.JoinPart, part);
        }

        /// The step and its value. A host's Burn is followed by ReadWho.
        public static EpilogueNote Read(PacketReader r)
        {
            var step = (EpilogueStep)r.R.ReadByte();
            string value = r.ReadStr();
            return new EpilogueNote { Step = step, Value = value };
        }

        /// The player who set the fire, after Read of a host's Burn.
        public static ulong ReadWho(PacketReader r)
        {
            return r.R.ReadUInt64();
        }

        static void Write(PacketWriter w, EpilogueStep step, string value)
        {
            w.W.Write((byte)step);
            w.WriteStr(value);
        }
    }
}
