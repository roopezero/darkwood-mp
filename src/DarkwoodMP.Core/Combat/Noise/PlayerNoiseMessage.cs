using DarkwoodMP.Net;

namespace DarkwoodMP.Combat.Noise
{
    /// The bytes of MsgType.PlayerNoise (client -> host): vec3 pos, f32
    /// range, f32 volume, u8 flags (1 dangerous, 2 gunshot).
    public static class PlayerNoiseMessage
    {
        const byte DangerousFlag = 1;
        const byte GunshotFlag = 2;

        /// The payload, after Begin(MsgType.PlayerNoise).
        public static void Write(PacketWriter w, in SentNoise noise)
        {
            w.Write(noise.At);
            w.W.Write(noise.Range);
            w.W.Write(noise.Volume);
            byte flags = 0;
            if (noise.Dangerous)
            {
                flags |= DangerousFlag;
            }

            if (noise.Gunshot)
            {
                flags |= GunshotFlag;
            }

            w.W.Write(flags);
        }

        /// The payload as sent: the host clamps the range and volume
        /// (NoiseRules.Range and Volume).
        public static SentNoise Read(PacketReader r)
        {
            Vec3 at = r.ReadVec3();
            float range = r.R.ReadSingle();
            float volume = r.R.ReadSingle();
            byte flags = r.R.ReadByte();
            return new SentNoise
            {
                At = at,
                Range = range,
                Volume = volume,
                Dangerous = (flags & DangerousFlag) != 0,
                Gunshot = (flags & GunshotFlag) != 0,
            };
        }
    }
}
