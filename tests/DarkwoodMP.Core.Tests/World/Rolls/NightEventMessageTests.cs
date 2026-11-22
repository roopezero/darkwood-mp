using DarkwoodMP.Net;
using DarkwoodMP.World;
using DarkwoodMP.World.Rolls;

namespace DarkwoodMP.Tests.World.Rolls
{
    public class NightEventMessageTests
    {
        static readonly NightFired Howl = new NightFired
        {
            Scenario = "s",
            Event = "e",
            Where = "",
        };

        static readonly ObjName Generator = new ObjName(ObjKind.Saved, 2);

        static readonly ObjName Lamp = new ObjName(ObjKind.Spawned, 3);

        public class WriteFired
        {
            [Fact]
            public void Writes_the_kind_scenario_event_and_place()
            {
                var w = RollBytes.Begin(MsgType.NightEvent);
                NightEventMessage.WriteFired(w, Howl);

                RollBytes.Of(w).ShouldBe(new byte[]
                {
                    63,
                    1,
                    1, 0, (byte)'s',
                    1, 0, (byte)'e',
                    0, 0,
                });
            }
        }

        public class WriteShadowLights
        {
            [Fact]
            public void Writes_the_kind_generator_and_lamp()
            {
                var w = RollBytes.Begin(MsgType.NightEvent);
                NightEventMessage.WriteShadowLights(w, Generator, Lamp);

                RollBytes.Of(w).ShouldBe(new byte[]
                {
                    63,
                    2,
                    1, 2, 0, 0, 0, 0, 0, 0, 0,
                    2, 3, 0, 0, 0, 0, 0, 0, 0,
                });
            }
        }

        public class Read
        {
            [Fact]
            public void Reads_back_a_fired_night_event()
            {
                var w = RollBytes.Begin(MsgType.NightEvent);
                NightEventMessage.WriteFired(w, Howl);

                var body = NightEventMessage.Read(RollBytes.Reader(w));

                body.Kind.ShouldBe(NightEventMessage.Fired);
                body.Fired.Scenario.ShouldBe("s");
                body.Fired.Event.ShouldBe("e");
                body.Fired.Where.ShouldBe("");
            }

            [Fact]
            public void Reads_back_the_shadows_lights()
            {
                var w = RollBytes.Begin(MsgType.NightEvent);
                NightEventMessage.WriteShadowLights(w, Generator, Lamp);

                var body = NightEventMessage.Read(RollBytes.Reader(w));

                body.Kind.ShouldBe(NightEventMessage.ShadowLights);
                body.Generator.ShouldBe(Generator);
                body.Lamp.ShouldBe(Lamp);
            }

            [Fact]
            public void Keeps_an_unknown_kind()
            {
                var w = RollBytes.Begin(MsgType.NightEvent);
                w.W.Write((byte)9);

                NightEventMessage.Read(RollBytes.Reader(w)).Kind
                    .ShouldBe((byte)9);
            }
        }
    }
}
