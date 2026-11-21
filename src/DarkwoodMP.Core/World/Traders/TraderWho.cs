using DarkwoodMP.Net;

namespace DarkwoodMP.World.Traders
{
    /// Which trader a trader message is about: his name, his ObjRef, and
    /// where he stands in the sender's game, on the ground.
    public readonly struct TraderWho
    {
        public string Name { get; init; }
        public ObjName Who { get; init; }
        public Vec2 At { get; init; }

        /// str name, the ObjRef (u8 kind, i64 id), f32 x, f32 z.
        public void Write(PacketWriter w)
        {
            w.WriteStr(Name);
            Who.Write(w);
            w.W.Write(At.X);
            w.W.Write(At.Z);
        }

        public static TraderWho Read(PacketReader r)
        {
            string name = r.ReadStr();
            var who = ObjName.Read(r);
            float x = r.R.ReadSingle();
            float z = r.R.ReadSingle();
            return new TraderWho
            {
                Name = name,
                Who = who,
                At = new Vec2(x, z),
            };
        }
    }
}
