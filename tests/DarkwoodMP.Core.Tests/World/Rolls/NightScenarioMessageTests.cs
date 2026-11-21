using DarkwoodMP.Net;
using DarkwoodMP.World.Rolls;

namespace DarkwoodMP.Tests.World.Rolls
{
    public class NightScenarioMessageTests
    {
        public class Write
        {
            [Fact]
            public void Writes_the_scenarios_name()
            {
                var w = RollBytes.Begin(MsgType.NightScenario);
                NightScenarioMessage.Write(w, "ab");

                RollBytes.Of(w).ShouldBe(
                    new byte[] { 62, 2, 0, (byte)'a', (byte)'b' });
            }

            [Fact]
            public void Writes_none_as_an_empty_name()
            {
                var w = RollBytes.Begin(MsgType.NightScenario);
                NightScenarioMessage.Write(w, "");

                RollBytes.Of(w).ShouldBe(new byte[] { 62, 0, 0 });
            }
        }

        public class Read
        {
            [Fact]
            public void Reads_back_what_was_written()
            {
                var w = RollBytes.Begin(MsgType.NightScenario);
                NightScenarioMessage.Write(w, "night");

                NightScenarioMessage.Read(RollBytes.Reader(w))
                    .ShouldBe("night");
            }
        }
    }
}
