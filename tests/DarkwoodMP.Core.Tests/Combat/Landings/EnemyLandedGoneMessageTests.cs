using System;
using DarkwoodMP.Combat.Landings;
using DarkwoodMP.Net;
using DarkwoodMP.Tests.Net;

namespace DarkwoodMP.Tests.Combat.Landings
{
    public class EnemyLandedGoneMessageTests
    {
        static byte[] BytesOf(long id)
        {
            var w = new PacketWriter(new RecordingLog());
            w.Begin(MsgType.EnemyLandedGone);
            EnemyLandedGoneMessage.Write(w, id);
            var bytes = new byte[w.Length];
            Array.Copy(w.Buffer, bytes, w.Length);
            return bytes;
        }

        public class Write
        {
            [Fact]
            public void Writes_the_spawned_id()
            {
                BytesOf(258).ShouldBe(new byte[]
                {
                    237,
                    2, 1, 0, 0, 0, 0, 0, 0,
                });
            }
        }

        public class Read
        {
            [Fact]
            public void Reads_back_what_was_written()
            {
                var bytes = BytesOf(258);
                var r = new PacketReader(bytes, bytes.Length);

                EnemyLandedGoneMessage.Read(r).ShouldBe(258L);
            }
        }
    }
}
