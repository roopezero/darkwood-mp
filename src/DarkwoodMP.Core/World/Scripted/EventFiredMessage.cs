using DarkwoodMP.Net;

namespace DarkwoodMP.World.Scripted
{
    /// The bytes of MsgType.EventFired (either way): u8 kind, ObjRef the
    /// EventTriggers, u8 the trigger's index, str the world events' names.
    public static class EventFiredMessage
    {
        /// The payload, after Begin(MsgType.EventFired).
        public static void Write(PacketWriter w, in FiredEvent fired)
        {
            w.W.Write((byte)fired.Kind);
            fired.Trigger.Write(w);
            w.W.Write((byte)fired.Index);
            w.WriteStr(fired.Name);
        }

        public static FiredEvent Read(PacketReader r)
        {
            var kind = (EventKind)r.R.ReadByte();
            ObjName trigger = ObjName.Read(r);
            int index = r.R.ReadByte();
            string name = r.ReadStr();
            return new FiredEvent
            {
                Kind = kind,
                Trigger = trigger,
                Index = index,
                Name = name,
            };
        }
    }
}
