using System.Linq;
using DarkwoodMP.Net;
using DarkwoodMP.Tests.Net;
using DarkwoodMP.World.Joining;

namespace DarkwoodMP.Tests.World.Joining
{
    public class SaveOfferTests
    {
        static PacketWriter Written()
        {
            var w = new PacketWriter(new RecordingLog());
            w.Begin(MsgType.SaveOffer);
            SaveOffer.Write(w, new SaveOfferBody
            {
                Hash = Bytes.Hash(2),
                Size = 7,
                ProfileJson = "{}",
                Files = new[]
                {
                    new OfferedFile { Name = "a", Size = 3 },
                    new OfferedFile { Name = "b", Size = 5 },
                },
                Packing = 1,
                Raw = 8,
            });

            return w;
        }

        public class Write
        {
            [Fact]
            public void Writes_the_offer_after_the_type()
            {
                var expected = new byte[] { 4 }
                    .Concat(Bytes.Hash(2))
                    .Concat(new byte[] { 7, 0, 0, 0 })
                    .Concat(new byte[] { 2, 0, 0, 0, 123, 125 })
                    .Concat(new byte[] { 2 })
                    .Concat(new byte[] { 1, 0, 97, 3, 0, 0, 0 })
                    .Concat(new byte[] { 1, 0, 98, 5, 0, 0, 0 })
                    .Concat(new byte[] { 1, 8, 0, 0, 0 });
                Bytes.Of(Written()).ShouldBe(expected.ToArray());
            }
        }

        public class Read
        {
            [Fact]
            public void Reads_back_what_was_written()
            {
                var w = Written();
                var r = new PacketReader(w.Buffer, w.Length);

                var body = SaveOffer.Read(r);

                body.Hash.ShouldBe(Bytes.Hash(2));
                body.Size.ShouldBe(7u);
                body.ProfileJson.ShouldBe("{}");
                body.Files.Count.ShouldBe(2);
                body.Files[1].Name.ShouldBe("b");
                body.Files[1].Size.ShouldBe(5u);
                body.Packing.ShouldBe((byte)1);
                body.Raw.ShouldBe(8u);
            }
        }
    }
}
