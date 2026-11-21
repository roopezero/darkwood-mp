using DarkwoodMP.Net;

namespace DarkwoodMP.World.Eggs
{
    /// The bytes of MsgType.EggDestroyed (either way): ObjRef egg, f32 x,
    /// f32 z, str name.
    public static class EggDestroyed
    {
        /// The payload, after Begin(MsgType.EggDestroyed).
        public static void Write(PacketWriter w, in EggBreak egg)
        {
            egg.Egg.Write(w);
            w.W.Write(egg.At.X);
            w.W.Write(egg.At.Z);
            w.WriteStr(egg.Name);
        }

        public static EggBreak Read(PacketReader r)
        {
            ObjName egg = ObjName.Read(r);
            float x = r.R.ReadSingle();
            float z = r.R.ReadSingle();
            string name = r.ReadStr();
            return new EggBreak
            {
                Egg = egg,
                At = new Vec2(x, z),
                Name = name,
            };
        }
    }
}
