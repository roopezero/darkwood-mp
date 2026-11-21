using System;
using System.Linq;
using System.Text;
using DarkwoodMP.Net;
using DarkwoodMP.Tests.Net;
using DarkwoodMP.World.Story;

namespace DarkwoodMP.Tests.World.Story
{
    public class EpilogueStepMessageTests
    {
        static PacketWriter Begun()
        {
            var w = new PacketWriter(new RecordingLog());
            w.Begin(MsgType.EpilogueStep);
            return w;
        }

        static byte[] Bytes(PacketWriter w)
        {
            var bytes = new byte[w.Length];
            Array.Copy(w.Buffer, bytes, w.Length);
            return bytes;
        }

        static byte[] Expected(byte step, string value)
        {
            var text = Encoding.UTF8.GetBytes(value);
            var head = new byte[] { 89, step, (byte)text.Length, 0 };
            return head.Concat(text).ToArray();
        }

        static EpilogueNote ReadBack(PacketWriter w)
        {
            var r = new PacketReader(w.Buffer, w.Length);
            return EpilogueStepMessage.Read(r);
        }

        public class WritePan
        {
            [Fact]
            public void Writes_step_1_and_the_pan_event()
            {
                var w = Begun();

                EpilogueStepMessage.WritePan(w);

                Bytes(w).ShouldBe(
                    Expected(1, "epilogue_cameraPanOverBurningForest"));
            }
        }

        public class WriteOutcomes
        {
            [Fact]
            public void Writes_step_2_and_1_when_burned()
            {
                var w = Begun();

                EpilogueStepMessage.WriteOutcomes(w, true);

                Bytes(w).ShouldBe(new byte[] { 89, 2, 1, 0, 49 });
            }

            [Fact]
            public void Writes_step_2_and_0_when_not_burned()
            {
                var w = Begun();

                EpilogueStepMessage.WriteOutcomes(w, false);

                Bytes(w).ShouldBe(new byte[] { 89, 2, 1, 0, 48 });
            }
        }

        public class WriteCrawled
        {
            [Fact]
            public void Writes_step_3_and_an_empty_value()
            {
                var w = Begun();

                EpilogueStepMessage.WriteCrawled(w);

                Bytes(w).ShouldBe(new byte[] { 89, 3, 0, 0 });
            }
        }

        public class WriteBurn
        {
            [Fact]
            public void Writes_step_4_and_the_crater_events()
            {
                var w = Begun();

                EpilogueStepMessage.WriteBurn(w);

                Bytes(w).ShouldBe(Expected(4, "Istota_lv1_events"));
            }
        }

        public class WriteBurnBy
        {
            [Fact]
            public void Adds_the_player_as_a_little_endian_u64()
            {
                var w = Begun();

                EpilogueStepMessage.WriteBurnBy(w, 0x0102UL);

                var who = new byte[] { 2, 1, 0, 0, 0, 0, 0, 0 };
                var expected = Expected(4, "Istota_lv1_events").Concat(who);
                Bytes(w).ShouldBe(expected.ToArray());
            }
        }

        public class Read
        {
            [Fact]
            public void Reads_back_the_step_and_the_value()
            {
                var w = Begun();
                EpilogueStepMessage.WriteOutcomes(w, true);

                EpilogueNote note = ReadBack(w);

                note.Step.ShouldBe(EpilogueStep.Outcomes);
                note.Value.ShouldBe("1");
            }
        }

        public class ReadWho
        {
            [Fact]
            public void Reads_the_player_after_a_hosts_burn()
            {
                var w = Begun();
                EpilogueStepMessage.WriteBurnBy(w, 77UL);
                var r = new PacketReader(w.Buffer, w.Length);
                EpilogueStepMessage.Read(r);

                EpilogueStepMessage.ReadWho(r).ShouldBe(77UL);
            }
        }
    }
}
