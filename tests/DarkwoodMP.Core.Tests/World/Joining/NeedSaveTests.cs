using System.Linq;
using DarkwoodMP.Net;
using DarkwoodMP.Tests.Net;
using DarkwoodMP.World.Joining;

namespace DarkwoodMP.Tests.World.Joining
{
    public class NeedSaveTests
    {
        static PacketWriter Written(in WorldPiece piece)
        {
            var w = new PacketWriter(new RecordingLog());
            w.Begin(MsgType.NeedSave);
            NeedSave.Write(w, piece);
            return w;
        }

        public class Write
        {
            [Fact]
            public void Writes_the_hash_offset_and_length_after_the_type()
            {
                var w = Written(new WorldPiece
                {
                    Hash = Bytes.Hash(0),
                    Offset = 0x40000,
                    Length = 0x40000,
                });

                var expected = new byte[] { 5 }
                    .Concat(Bytes.Hash(0))
                    .Concat(new byte[] { 0, 0, 4, 0, 0, 0, 4, 0 });
                Bytes.Of(w).ShouldBe(expected.ToArray());
            }
        }

        public class Read
        {
            [Fact]
            public void Reads_back_what_was_written()
            {
                var w = Written(new WorldPiece
                {
                    Hash = Bytes.Hash(7),
                    Offset = 12,
                    Length = 34,
                });

                var r = new PacketReader(w.Buffer, w.Length);

                var piece = NeedSave.Read(r);

                piece.Hash.ShouldBe(Bytes.Hash(7));
                piece.Offset.ShouldBe(12u);
                piece.Length.ShouldBe(34u);
            }
        }
    }
}
