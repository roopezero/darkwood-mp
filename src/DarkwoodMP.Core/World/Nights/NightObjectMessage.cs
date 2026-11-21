using DarkwoodMP.Net;

namespace DarkwoodMP.World.Nights
{
    /// The bytes of MsgType.NightObject (host to joiners): u8 kind, i64 id;
    /// unless the kind is Gone, then str scenario, str event, u8 group, u8
    /// step, vec3 pos, quat rot (x y z w), ref parent, u8 armed.
    public static class NightObjectMessage
    {
        /// The payload, after Begin(MsgType.NightObject).
        public static void Write(PacketWriter w, in NightObjectBody night)
        {
            w.W.Write(night.Kind);
            w.W.Write(night.Id);
            if (night.Kind == NightObjectRules.Gone)
            {
                return;
            }

            w.WriteStr(night.Scenario);
            w.WriteStr(night.Event);
            w.W.Write((byte)night.Group);
            w.W.Write((byte)night.Step);
            w.Write(night.Pos);
            w.W.Write(night.Rot.X);
            w.W.Write(night.Rot.Y);
            w.W.Write(night.Rot.Z);
            w.W.Write(night.Rot.W);
            night.Parent.Write(w);
            w.W.Write(night.Armed);
        }

        /// A Gone one comes with the rest at its defaults.
        public static NightObjectBody Read(PacketReader r)
        {
            byte kind = r.R.ReadByte();
            long id = r.R.ReadInt64();
            if (kind == NightObjectRules.Gone)
            {
                return new NightObjectBody { Kind = kind, Id = id };
            }

            string scenario = r.ReadStr();
            string name = r.ReadStr();
            int group = r.R.ReadByte();
            int step = r.R.ReadByte();
            Vec3 pos = r.ReadVec3();
            var rot = new Quat(
                r.R.ReadSingle(),
                r.R.ReadSingle(),
                r.R.ReadSingle(),
                r.R.ReadSingle());
            ObjName parent = ObjName.Read(r);
            byte armed = r.R.ReadByte();
            return new NightObjectBody
            {
                Kind = kind,
                Id = id,
                Scenario = scenario,
                Event = name,
                Group = group,
                Step = step,
                Pos = pos,
                Rot = rot,
                Parent = parent,
                Armed = armed,
            };
        }
    }
}
