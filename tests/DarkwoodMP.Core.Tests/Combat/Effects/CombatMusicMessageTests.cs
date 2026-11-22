using System;
using DarkwoodMP.Combat.Effects;
using DarkwoodMP.Net;
using DarkwoodMP.Tests.Net;

namespace DarkwoodMP.Tests.Combat.Effects
{
    public class CombatMusicMessageTests
    {
        static byte[] BytesOf(bool fight)
        {
            var w = new PacketWriter(new RecordingLog());
            w.Begin(MsgType.CombatMusic);
            CombatMusicMessage.Write(w, fight);
            var bytes = new byte[w.Length];
            Array.Copy(w.Buffer, bytes, w.Length);
            return bytes;
        }

        public class Write
        {
            [Fact]
            public void Writes_a_fight_as_1()
            {
                BytesOf(true).ShouldBe(new byte[] { 78, 1 });
            }

            [Fact]
            public void Writes_no_fight_as_0()
            {
                BytesOf(false).ShouldBe(new byte[] { 78, 0 });
            }
        }

        public class Read
        {
            [Fact]
            public void Reads_any_byte_but_0_as_a_fight()
            {
                var bytes = new byte[] { 78, 2 };

                CombatMusicMessage.Read(new PacketReader(bytes, 2))
                    .ShouldBeTrue();
            }
        }
    }
}
