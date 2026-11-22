using DarkwoodMP.Net;

namespace DarkwoodMP.Combat.Enemies
{
    /// A rotation on the wire: f32 x, y, z, w.
    public static class QuatBytes
    {
        public static void Write(PacketWriter w, Quat q)
        {
            w.W.Write(q.X);
            w.W.Write(q.Y);
            w.W.Write(q.Z);
            w.W.Write(q.W);
        }

        public static Quat Read(PacketReader r)
        {
            float x = r.R.ReadSingle();
            float y = r.R.ReadSingle();
            float z = r.R.ReadSingle();
            float w = r.R.ReadSingle();
            return new Quat(x, y, z, w);
        }
    }
}
