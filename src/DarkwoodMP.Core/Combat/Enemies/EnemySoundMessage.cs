using DarkwoodMP.Net;

namespace DarkwoodMP.Combat.Enemies
{
    /// The bytes of MsgType.EnemySound (host -> client): u32 id, str audio
    /// id, f32 volume.
    public static class EnemySoundMessage
    {
        /// The payload, after Begin(MsgType.EnemySound).
        public static void Write(PacketWriter w, in EnemySound s)
        {
            w.W.Write(s.Id);
            w.WriteStr(s.Audio);
            w.W.Write(s.Volume);
        }

        public static EnemySound Read(PacketReader r)
        {
            uint id = r.R.ReadUInt32();
            string audio = r.ReadStr();
            float volume = r.R.ReadSingle();
            return new EnemySound
            {
                Id = id,
                Audio = audio,
                Volume = volume,
            };
        }
    }
}
