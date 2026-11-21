using DarkwoodMP.Net;

namespace DarkwoodMP.World.Rolls
{
    /// The bytes of MsgType.NightEvent (host to client): u8 kind, then 1
    /// (a night event fired): str scenario, str event, str the host's big
    /// location; or 2 (the shadows' lights): ObjRef generator, ObjRef the
    /// lamp that stays on.
    public static class NightEventMessage
    {
        public const byte Fired = 1;
        public const byte ShadowLights = 2;

        /// The payload of a fired night event, after
        /// Begin(MsgType.NightEvent).
        public static void WriteFired(PacketWriter w, in NightFired fired)
        {
            w.W.Write(Fired);
            w.WriteStr(fired.Scenario);
            w.WriteStr(fired.Event);
            w.WriteStr(fired.Where);
        }

        /// The payload of the shadows' lights, after
        /// Begin(MsgType.NightEvent).
        public static void WriteShadowLights(
            PacketWriter w, ObjName generator, ObjName lamp)
        {
            w.W.Write(ShadowLights);
            generator.Write(w);
            lamp.Write(w);
        }

        public static NightEventBody Read(PacketReader r)
        {
            byte kind = r.R.ReadByte();
            if (kind == Fired)
            {
                string scenario = r.ReadStr();
                string name = r.ReadStr();
                string where = r.ReadStr();
                var fired = new NightFired
                {
                    Scenario = scenario,
                    Event = name,
                    Where = where,
                };

                return new NightEventBody { Kind = kind, Fired = fired };
            }

            if (kind == ShadowLights)
            {
                ObjName generator = ObjName.Read(r);
                ObjName lamp = ObjName.Read(r);
                return new NightEventBody
                {
                    Kind = kind,
                    Generator = generator,
                    Lamp = lamp,
                };
            }

            return new NightEventBody { Kind = kind };
        }
    }
}
