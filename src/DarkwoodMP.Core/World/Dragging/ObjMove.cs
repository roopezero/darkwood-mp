using DarkwoodMP.Net;

namespace DarkwoodMP.World.Dragging
{
    /// The bytes of MsgType.ObjMove (either way): ObjRef, vec3 pos, quat rot
    /// (x, y, z, w), u8 flags (1: last).
    public static class ObjMove
    {
        /// The payload, after Begin(MsgType.ObjMove).
        public static void Write(PacketWriter w, in Moved move)
        {
            move.Name.Write(w);
            w.Write(move.Pos);
            w.W.Write(move.Rot.X);
            w.W.Write(move.Rot.Y);
            w.W.Write(move.Rot.Z);
            w.W.Write(move.Rot.W);
            byte flags = 0;
            if (move.Last)
            {
                flags = 1;
            }

            w.W.Write(flags);
        }

        /// The move, as written.
        public static Moved Read(PacketReader r)
        {
            var name = ObjName.Read(r);
            var pos = r.ReadVec3();
            var rot = new Rotation(
                r.R.ReadSingle(), r.R.ReadSingle(), r.R.ReadSingle(),
                r.R.ReadSingle());
            bool last = (r.R.ReadByte() & 1) != 0;
            return new Moved
            {
                Name = name,
                Pos = pos,
                Rot = rot,
                Last = last,
            };
        }
    }
}
