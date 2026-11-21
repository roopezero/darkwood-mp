using DarkwoodMP.Net;

namespace DarkwoodMP.World
{
    /// The bytes of MsgType.WorldAction (either way): u8 action, ObjRef
    /// (u8 kind, i64 id), f32 a, f32 b, u8 flags, str s.
    public static class WorldActionMessage
    {
        /// The payload, after Begin(MsgType.WorldAction).
        public static void Write(PacketWriter w, in WorldActionBody body)
        {
            w.W.Write(body.Action);
            body.Obj.Write(w);
            w.W.Write(body.X);
            w.W.Write(body.Y);
            w.W.Write(body.Flags);
            string text = body.Text;
            if (text == null)
            {
                text = "";
            }

            w.WriteStr(text);
        }

        public static WorldActionBody Read(PacketReader r)
        {
            var body = new WorldActionBody();
            body.Action = r.R.ReadByte();
            body.Obj = ObjName.Read(r);
            body.X = r.R.ReadSingle();
            body.Y = r.R.ReadSingle();
            body.Flags = r.R.ReadByte();
            body.Text = r.ReadStr();
            return body;
        }
    }
}
