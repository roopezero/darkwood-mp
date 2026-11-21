using DarkwoodMP.Net;

namespace DarkwoodMP.World.Throws
{
    /// The bytes of MsgType.ItemThrown (either way): u64 thrower, str item
    /// type, vec3 pos, quat rot (x y z w), vec3 velocity, vec3 land
    /// target, f32 distance (-1: dropped at the thrower's feet).
    public static class ItemThrown
    {
        /// The payload, after Begin(MsgType.ItemThrown).
        public static void Write(PacketWriter w, in ThrowReport report)
        {
            w.W.Write(report.By);
            w.WriteStr(report.Type);
            w.Write(report.Pos);
            w.W.Write(report.Rot.X);
            w.W.Write(report.Rot.Y);
            w.W.Write(report.Rot.Z);
            w.W.Write(report.Rot.W);
            w.Write(report.Shot.Velocity);
            w.Write(report.Shot.Target);
            w.W.Write(report.Shot.Distance);
        }

        /// The throw the message reports.
        public static ThrowReport Read(PacketReader r)
        {
            ulong by = r.R.ReadUInt64();
            string type = r.ReadStr();
            var pos = r.ReadVec3();
            var rot = new Quat(
                r.R.ReadSingle(), r.R.ReadSingle(), r.R.ReadSingle(),
                r.R.ReadSingle());
            var velocity = r.ReadVec3();
            var target = r.ReadVec3();
            float distance = r.R.ReadSingle();
            return new ThrowReport
            {
                By = by,
                Type = type,
                Pos = pos,
                Rot = rot,
                Shot = new ThrowShot
                {
                    Velocity = velocity,
                    Target = target,
                    Distance = distance,
                },
            };
        }
    }
}
