using System;
using DarkwoodMP.Combat.Cues;
using DarkwoodMP.Net;
using DarkwoodMP.Tests.Net;

namespace DarkwoodMP.Tests.Combat.Cues
{
    public class PlayerCueTests
    {
        static PacketWriter Written(byte kind)
        {
            var w = new PacketWriter(new RecordingLog());
            w.Begin(MsgType.PlayerCue);
            PlayerCue.Write(w, kind);
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
            public void Writes_a_swing_as_kind_0()
            {
                BytesOf(Written(CueRules.Dodge))
                    .ShouldBe(new byte[] { 70, 0 });
            }

            [Fact]
            public void Writes_an_aim_as_kind_1()
            {
                BytesOf(Written(CueRules.Scare))
                    .ShouldBe(new byte[] { 70, 1 });
            }
        }

        public class Read
        {
            [Fact]
            public void Reads_back_what_was_written()
            {
                var w = Written(CueRules.Scare);
                var r = new PacketReader(w.Buffer, w.Length);

                PlayerCue.Read(r).ShouldBe(CueRules.Scare);
            }
        }
    }
}
