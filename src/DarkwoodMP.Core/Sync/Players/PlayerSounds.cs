using System.Collections.Generic;
using DarkwoodMP.Net;

namespace DarkwoodMP.Sync.Players
{
    /// The bytes of MsgType.PlayerSounds (either way): u64 player, u8 n,
    /// n x (str audio id, f32 volume).
    public static class PlayerSounds
    {
        /// The payload, after Begin(MsgType.PlayerSounds). At most
        /// PlayerFxRules.MaxSounds sounds, so n fits in a byte.
        public static void Write(
            PacketWriter w, ulong owner, IReadOnlyList<PlayedSound> sounds)
        {
            w.W.Write(owner);
            w.W.Write((byte)sounds.Count);
            for (int i = 0; i < sounds.Count; i++)
            {
                w.WriteStr(sounds[i].Id);
                w.W.Write(sounds[i].Volume);
            }
        }

        /// The player it says, and how many sounds follow.
        public static (ulong owner, int count) ReadHead(PacketReader r)
        {
            ulong owner = r.R.ReadUInt64();
            int count = r.R.ReadByte();
            return (owner, count);
        }

        /// One of the sounds after the head.
        public static PlayedSound ReadSound(PacketReader r)
        {
            string id = r.ReadStr();
            float volume = r.R.ReadSingle();
            return new PlayedSound { Id = id, Volume = volume };
        }
    }
}
