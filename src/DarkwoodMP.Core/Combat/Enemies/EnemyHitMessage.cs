using DarkwoodMP.Combat.Damage;
using DarkwoodMP.Net;

namespace DarkwoodMP.Combat.Enemies
{
    /// The bytes of MsgType.EnemyHit (client to host, inside a Request):
    /// u32 id, f32 damage, u8 flags, u8 n, n x effect (SentEffects), str
    /// weapon.
    public static class EnemyHitMessage
    {
        /// The hit cuts the enemy in half if it kills it.
        public const byte Cut = 1;
        /// By the player.
        public const byte ByPlayer = 2;
        /// The hit interrupts what the enemy does.
        public const byte Interrupt = 4;
        /// getHit's normal.
        public const byte Normal = 8;

        /// The payload, after Begin(MsgType.EnemyHit).
        public static void Write(PacketWriter w, in EnemyHitRequest hit)
        {
            w.W.Write(hit.Id);
            w.W.Write(hit.Damage);
            w.W.Write(hit.Flags);
            SentEffects.Write(w, hit.Effects);
            w.WriteStr(hit.Weapon);
        }

        public static EnemyHitRequest Read(PacketReader r)
        {
            uint id = r.R.ReadUInt32();
            float damage = r.R.ReadSingle();
            byte flags = r.R.ReadByte();
            var effects = SentEffects.Read(r);
            string weapon = r.ReadStr();
            return new EnemyHitRequest
            {
                Id = id,
                Damage = damage,
                Flags = flags,
                Effects = effects,
                Weapon = weapon,
            };
        }
    }
}
