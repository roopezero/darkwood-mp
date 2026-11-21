using System;
using DarkwoodMP.Net;
using DarkwoodMP.Tests.Net;
using DarkwoodMP.World;
using DarkwoodMP.World.Fuel;

namespace DarkwoodMP.Tests.World.Fuel
{
    public class FuelMessageTests
    {
        static readonly TankFuel Running = new TankFuel
        {
            Tank = new ObjName(ObjKind.Saved, 2),
            Kind = FuelTank.Generator,
            Value = 1f,
            On = true,
        };

        static PacketWriter Written(TankFuel fuel)
        {
            var w = new PacketWriter(new RecordingLog());
            w.Begin(MsgType.Fuel);
            FuelMessage.Write(w, fuel);
            return w;
        }

        static byte[] BytesOf(PacketWriter w)
        {
            var bytes = new byte[w.Length];
            Array.Copy(w.Buffer, bytes, w.Length);
            return bytes;
        }

        public class Write
        {
            [Fact]
            public void Writes_the_tank_what_it_is_its_fuel_and_on()
            {
                var w = Written(Running);

                BytesOf(w).ShouldBe(new byte[]
                {
                    44,
                    1,
                    2, 0, 0, 0, 0, 0, 0, 0,
                    1,
                    0x00, 0x00, 0x80, 0x3F,
                    1,
                });
            }

            [Fact]
            public void Writes_a_saw_as_2_and_off_as_0()
            {
                var saw = Running with
                {
                    Kind = FuelTank.Saw,
                    Value = -2f,
                    On = false,
                };

                var w = Written(saw);

                BytesOf(w).ShouldBe(new byte[]
                {
                    44,
                    1,
                    2, 0, 0, 0, 0, 0, 0, 0,
                    2,
                    0x00, 0x00, 0x00, 0xC0,
                    0,
                });
            }
        }

        public class Read
        {
            [Fact]
            public void Reads_back_what_was_written()
            {
                var w = Written(Running);
                var r = new PacketReader(w.Buffer, w.Length);

                TankFuel read = FuelMessage.Read(r);

                read.Tank.ShouldBe(Running.Tank);
                read.Kind.ShouldBe(FuelTank.Generator);
                read.Value.ShouldBe(1f);
                read.On.ShouldBeTrue();
            }

            [Fact]
            public void Reads_any_what_but_1_as_a_saw()
            {
                var w = new PacketWriter(new RecordingLog());
                w.Begin(MsgType.Fuel);
                new ObjName(ObjKind.Saved, 2).Write(w);
                w.W.Write((byte)7);
                w.W.Write(0f);
                w.W.Write((byte)3);
                var r = new PacketReader(w.Buffer, w.Length);

                TankFuel read = FuelMessage.Read(r);

                read.Kind.ShouldBe(FuelTank.Saw);
                read.On.ShouldBeTrue();
            }
        }
    }
}
