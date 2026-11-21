using DarkwoodMP.Combat.Damage;
using DarkwoodMP.Net;
using DarkwoodMP.World;

namespace DarkwoodMP.Combat.Hostiles
{
    /// The bytes of MsgType.NpcHit (client to host, inside a Request):
    /// ObjRef npc, f32 damage, u8 flags, u8 n, n x effect (SentEffects),
    /// str weapon.
    public static class NpcHitMessage
    {
        /// The hit cuts the NPC in half if it kills it.
        public const byte Cut = 1;
        /// By our player: always set.
        public const byte ByPlayer = 2;
        /// The hit interrupts what the NPC does.
        public const byte Interrupt = 4;
        /// getHit's normal.
        public const byte Normal = 8;

        /// The flags of our player's hit, ByPlayer always among them.
        public static byte FlagsOf(bool cut, bool interrupt, bool normal)
        {
            byte flags = ByPlayer;
            if (cut)
            {
                flags |= Cut;
            }

            if (interrupt)
            {
                flags |= Interrupt;
            }

            if (normal)
            {
                flags |= Normal;
            }

            return flags;
        }

        /// The payload, after Begin(MsgType.NpcHit).
        public static void Write(PacketWriter w, in NpcHitRequest hit)
        {
            hit.Npc.Write(w);
            w.W.Write(hit.Damage);
            w.W.Write(hit.Flags);
            SentEffects.Write(w, hit.Effects);
            w.WriteStr(hit.Weapon);
        }

        public static NpcHitRequest Read(PacketReader r)
        {
            ObjName npc = ObjName.Read(r);
            float damage = r.R.ReadSingle();
            byte flags = r.R.ReadByte();
            var effects = SentEffects.Read(r);
            string weapon = r.ReadStr();
            return new NpcHitRequest
            {
                Npc = npc,
                Damage = damage,
                Flags = flags,
                Effects = effects,
                Weapon = weapon,
            };
        }
    }
}
