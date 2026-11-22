using DarkwoodMP.Net;
using DarkwoodMP.World;

namespace DarkwoodMP.Combat.Hostiles
{
    /// The bytes of MsgType.NpcAttack (client to host, inside a Request):
    /// ObjRef npc.
    public static class NpcAttackMessage
    {
        /// The payload, after Begin(MsgType.NpcAttack).
        public static void Write(PacketWriter w, ObjName npc)
        {
            npc.Write(w);
        }

        public static ObjName Read(PacketReader r) => ObjName.Read(r);
    }
}
