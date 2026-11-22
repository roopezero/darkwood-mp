using DarkwoodMP.Net;

namespace DarkwoodMP.World.Talk
{
    /// The bytes of MsgType.TalkLock (client to host): the NPC's ObjRef
    /// (u8 kind, i64 id), u8 talking (1 opened, 0 closed), str name.
    /// Named apart from the plugin's TalkLock, which sends it.
    public static class TalkLockMessage
    {
        /// The payload, after Begin(MsgType.TalkLock).
        public static void Write(PacketWriter w, TalkHold hold)
        {
            hold.Npc.Write(w);
            byte talking = 0;
            if (hold.Talking)
            {
                talking = 1;
            }

            w.W.Write(talking);
            w.WriteStr(hold.Name);
        }

        /// The NPC, whether talking (any byte but 0), and the name.
        public static TalkHold Read(PacketReader r)
        {
            var npc = ObjName.Read(r);
            bool talking = r.R.ReadByte() != 0;
            string name = r.ReadStr();
            return new TalkHold { Npc = npc, Talking = talking, Name = name };
        }
    }
}
