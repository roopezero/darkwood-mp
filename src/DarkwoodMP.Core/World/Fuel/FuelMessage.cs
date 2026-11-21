using DarkwoodMP.Net;

namespace DarkwoodMP.World.Fuel
{
    /// The bytes of MsgType.Fuel (either way): ObjRef tank, u8 what (1
    /// generator, 2 saw), f32 fuel, u8 on. Not named Fuel like the MsgType:
    /// the plugin's subsystem in this namespace is Fuel.
    public static class FuelMessage
    {
        /// The payload, after Begin(MsgType.Fuel).
        public static void Write(PacketWriter w, in TankFuel fuel)
        {
            fuel.Tank.Write(w);
            w.W.Write((byte)fuel.Kind);
            w.W.Write(fuel.Value);
            byte on = 0;
            if (fuel.On)
            {
                on = 1;
            }

            w.W.Write(on);
        }

        public static TankFuel Read(PacketReader r)
        {
            ObjName tank = ObjName.Read(r);
            FuelTank kind = FuelRules.TankOf(r.R.ReadByte());
            float value = r.R.ReadSingle();
            bool on = r.R.ReadByte() != 0;
            return new TankFuel
            {
                Tank = tank,
                Kind = kind,
                Value = value,
                On = on,
            };
        }
    }
}
