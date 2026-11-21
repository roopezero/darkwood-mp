using DarkwoodMP.Net;

namespace DarkwoodMP.Sync.Players
{
    /// The bytes of MsgType.PlayerShot (either way): u64 player, str muzzle
    /// prefab, str muzzle particles, u8 flash, vec3 muzzle offset.
    public static class PlayerShot
    {
        /// The payload, after Begin(MsgType.PlayerShot).
        public static void Write(
            PacketWriter w, ulong owner, in MuzzleFlash shot)
        {
            w.W.Write(owner);
            w.WriteStr(shot.Muzzle);
            w.WriteStr(shot.Particles);
            w.W.Write(shot.Flash);
            w.Write(shot.Offset);
        }

        /// The player it says, and what the shot shows.
        public static (ulong owner, MuzzleFlash shot) Read(PacketReader r)
        {
            ulong owner = r.R.ReadUInt64();
            string muzzle = r.ReadStr();
            string particles = r.ReadStr();
            byte flash = r.R.ReadByte();
            Vec3 offset = r.ReadVec3();
            var shot = new MuzzleFlash
            {
                Muzzle = muzzle,
                Particles = particles,
                Flash = flash,
                Offset = offset,
            };

            return (owner, shot);
        }
    }
}
