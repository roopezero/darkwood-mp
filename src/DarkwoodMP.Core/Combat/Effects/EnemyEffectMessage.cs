using DarkwoodMP.Net;

namespace DarkwoodMP.Combat.Effects
{
    /// The bytes of MsgType.EnemyEffect (host -> client): u8 kind, then
    /// for kind 3 (an emitter) u32 id, i32 toggle; for any other kind
    /// str name, vec3 position, and for kind 1 (a prefab) f32 rotation
    /// about Y.
    public static class EnemyEffectMessage
    {
        /// The payload, after Begin(MsgType.EnemyEffect).
        public static void Write(PacketWriter w, in EnemyEffect fx)
        {
            w.W.Write(fx.Kind);
            if (fx.Kind == EffectRules.EmitterKind)
            {
                w.W.Write(fx.Id);
                w.W.Write(fx.Toggle);
                return;
            }

            w.WriteStr(fx.Name);
            w.Write(fx.At);
            if (EffectRules.Rotates(fx.Kind))
            {
                w.W.Write(fx.RotY);
            }
        }

        public static EnemyEffect Read(PacketReader r)
        {
            byte kind = r.R.ReadByte();
            if (kind == EffectRules.EmitterKind)
            {
                uint id = r.R.ReadUInt32();
                int toggle = r.R.ReadInt32();
                return new EnemyEffect
                {
                    Kind = kind,
                    Id = id,
                    Toggle = toggle,
                };
            }

            string name = r.ReadStr();
            Vec3 at = r.ReadVec3();
            float rotY = 0f;
            if (EffectRules.Rotates(kind))
            {
                rotY = r.R.ReadSingle();
            }

            return new EnemyEffect
            {
                Kind = kind,
                Name = name,
                At = at,
                RotY = rotY,
            };
        }
    }
}
