using DarkwoodMP.Net;

namespace DarkwoodMP.Tests.Net
{
    public class PacketReaderTests
    {
        private static PacketReader ReaderOf(PacketWriter w)
        {
            return new PacketReader(w.Buffer, w.Length);
        }

        public class Type
        {
            [Fact]
            public void Reads_the_first_byte()
            {
                var w = new PacketWriter(new RecordingLog());
                w.Begin(MsgType.Welcome);

                var r = ReaderOf(w);

                r.Type.ShouldBe(MsgType.Welcome);
            }
        }

        public class ReadVec3
        {
            [Fact]
            public void Reads_what_Write_wrote()
            {
                var w = new PacketWriter(new RecordingLog());
                w.Begin(MsgType.Hello).Write(new Vec3(1f, -2f, 0.5f));

                var v = ReaderOf(w).ReadVec3();

                v.X.ShouldBe(1f);
                v.Y.ShouldBe(-2f);
                v.Z.ShouldBe(0.5f);
            }
        }

        public class ReadStr
        {
            [Fact]
            public void Reads_what_WriteStr_wrote()
            {
                var w = new PacketWriter(new RecordingLog());
                w.Begin(MsgType.Hello).WriteStr("hä");

                var text = ReaderOf(w).ReadStr();

                text.ShouldBe("hä");
            }
        }

        public class ReadBigStr
        {
            [Fact]
            public void Reads_what_WriteBigStr_wrote()
            {
                var w = new PacketWriter(new RecordingLog());
                w.Begin(MsgType.Hello).WriteBigStr("{\"a\":1}");

                var text = ReaderOf(w).ReadBigStr();

                text.ShouldBe("{\"a\":1}");
            }

            [Fact]
            public void Refuses_a_length_over_the_largest_message()
            {
                var bytes = new byte[] { 1, 0xFF, 0xFF, 0xFF, 0xFF };
                var r = new PacketReader(bytes, bytes.Length);

                Should.Throw<System.IO.InvalidDataException>(
                    () => r.ReadBigStr());
            }
        }
    }
}
