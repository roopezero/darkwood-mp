using DarkwoodMP.Net;

namespace DarkwoodMP.World.Doors
{
    /// The bytes of MsgType.DoorLeaf (either way): ObjRef door, f32 leaf
    /// angle (signed degrees from shut). Named apart from the plugin's
    /// DoorLeaf, the leaf's shell.
    public static class DoorLeafMessage
    {
        /// The payload, after Begin(MsgType.DoorLeaf).
        public static void Write(PacketWriter w, ObjName door, float angle)
        {
            door.Write(w);
            w.W.Write(angle);
        }

        /// The door it names, and the angle as it came (not checked: a
        /// NaN is the rules' to drop).
        public static ObjName Read(PacketReader r, out float angle)
        {
            var door = ObjName.Read(r);
            angle = r.R.ReadSingle();
            return door;
        }
    }
}
