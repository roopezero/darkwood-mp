using DarkwoodMP.Net;

namespace DarkwoodMP.World.Fire
{
    /// The bytes of MsgType.Burning (either way): ObjRef thing, u8 what,
    /// f32 burn time, u8 on.
    public static class Burning
    {
        /// The payload, after Begin(MsgType.Burning).
        public static void Write(PacketWriter w, in FireReport fire)
        {
            fire.Thing.Write(w);
            w.W.Write((byte)fire.Kind);
            w.W.Write(fire.Time);
            byte on = 0;
            if (fire.On)
            {
                on = 1;
            }

            w.W.Write(on);
        }

        public static FireReport Read(PacketReader r)
        {
            ObjName thing = ObjName.Read(r);
            var kind = (FireKind)r.R.ReadByte();
            float time = r.R.ReadSingle();
            bool on = r.R.ReadByte() != 0;
            return new FireReport
            {
                Thing = thing,
                Kind = kind,
                Time = time,
                On = on,
            };
        }
    }
}
