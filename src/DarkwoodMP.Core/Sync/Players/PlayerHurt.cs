using DarkwoodMP.Net;

namespace DarkwoodMP.Sync.Players
{
    /// The bytes of MsgType.PlayerHurt (either way): u64 player, u8 what,
    /// then for 1 (bled) str prefab, f32 height, f32 yaw; for 2 (started
    /// burning) str flames, str fire sound; for 3 (stopped) nothing.
    public static class PlayerHurt
    {
        /// The payload, after Begin(MsgType.PlayerHurt). Only Blood and
        /// Burning write more than the kind.
        public static void Write(PacketWriter w, ulong owner, in HurtLook hurt)
        {
            w.W.Write(owner);
            w.W.Write(hurt.Kind);
            if (hurt.Kind == (byte)HurtKind.Blood)
            {
                w.WriteStr(hurt.Prefab);
                w.W.Write(hurt.Height);
                w.W.Write(hurt.Yaw);
            }
            else if (hurt.Kind == (byte)HurtKind.Burning)
            {
                w.WriteStr(hurt.Prefab);
                w.WriteStr(hurt.Sound);
            }
        }

        /// The player it says, and what it shows. The strings are "" when
        /// the kind has none.
        public static (ulong owner, HurtLook hurt) Read(PacketReader r)
        {
            ulong owner = r.R.ReadUInt64();
            byte kind = r.R.ReadByte();
            string prefab = "";
            string sound = "";
            float height = 0f;
            float yaw = 0f;
            if (kind == (byte)HurtKind.Blood)
            {
                prefab = r.ReadStr();
                height = r.R.ReadSingle();
                yaw = r.R.ReadSingle();
            }
            else if (kind == (byte)HurtKind.Burning)
            {
                prefab = r.ReadStr();
                sound = r.ReadStr();
            }

            var hurt = new HurtLook
            {
                Kind = kind,
                Prefab = prefab,
                Sound = sound,
                Height = height,
                Yaw = yaw,
            };

            return (owner, hurt);
        }
    }
}
