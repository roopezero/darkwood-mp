using System.Linq;
using DarkwoodMP.Net;
using DarkwoodMP.Tests.Net;
using DarkwoodMP.World.Joining;

namespace DarkwoodMP.Tests.World.Joining
{
    public class SaveDataTests
    {
        static readonly byte[] World = { 10, 11, 12, 13, 14 };

        static PacketWriter Written(uint offset, uint length)
        {
            var w = new PacketWriter(new RecordingLog());
            w.Begin(MsgType.SaveData);
            var piece = new WorldPiece
            {
                Hash = Bytes.Hash(1),
                Offset = offset,
                Length = length,
            };

            SaveData.Write(w, piece, World);
            return w;
        }

        public class Write
        {
            [Fact]
            public void Writes_the_head_then_the_bytes_at_the_offset()
            {
                var w = Written(1, 3);

                var expected = new byte[] { 6 }
                    .Concat(Bytes.Hash(1))
                    .Concat(new byte[] { 1, 0, 0, 0, 3, 0, 0, 0 })
                    .Concat(new byte[] { 11, 12, 13 });
                Bytes.Of(w).ShouldBe(expected.ToArray());
            }
        }

        public class ReadHead
        {
            [Fact]
            public void Reads_the_hash_offset_and_length()
            {
                var w = Written(2, 2);
                var r = new PacketReader(w.Buffer, w.Length);

                var piece = SaveData.ReadHead(r);

                piece.Hash.ShouldBe(Bytes.Hash(1));
                piece.Offset.ShouldBe(2u);
                piece.Length.ShouldBe(2u);
            }
        }

        public class ReadInto
        {
            [Fact]
            public void Puts_the_bytes_at_the_offset()
            {
                var w = Written(2, 2);
                var r = new PacketReader(w.Buffer, w.Length);
                var piece = SaveData.ReadHead(r);
                var blob = new byte[5];

                SaveData.ReadInto(r, blob, piece).ShouldBeTrue();

                blob.ShouldBe(new byte[] { 0, 0, 12, 13, 0 });
            }

            [Fact]
            public void Says_when_fewer_bytes_came()
            {
                var w = Written(2, 2);
                var r = new PacketReader(w.Buffer, w.Length - 1);
                var piece = SaveData.ReadHead(r);

                SaveData.ReadInto(r, new byte[5], piece).ShouldBeFalse();
            }
        }
    }
}
