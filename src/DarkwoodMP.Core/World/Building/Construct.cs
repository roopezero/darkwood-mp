using DarkwoodMP.Net;

namespace DarkwoodMP.World.Building
{
    /// The bytes of MsgType.Construct (either way): the site's name (u8
    /// kind, i64 id), then i32 option.
    public static class Construct
    {
        /// The payload, after Begin(MsgType.Construct).
        public static void Write(PacketWriter w, BuildOrder order)
        {
            order.Site.Write(w);
            w.W.Write(order.Option);
        }

        /// The site and the option, as they came.
        public static BuildOrder Read(PacketReader r)
        {
            ObjName site = ObjName.Read(r);
            return new BuildOrder { Site = site, Option = r.R.ReadInt32() };
        }
    }
}
