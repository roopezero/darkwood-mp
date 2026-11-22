using System;
using DarkwoodMP.Net;

namespace DarkwoodMP.Tests.Net
{
    public class PacketWriterTests
    {
        private static byte[] BytesOf(PacketWriter w)
        {
            var bytes = new byte[w.Length];
            Array.Copy(w.Buffer, bytes, w.Length);
            return bytes;
        }

        public class Begin
        {
            [Fact]
            public void Starts_with_the_type_byte()
            {
                var w = new PacketWriter(new RecordingLog());

                w.Begin(MsgType.Hello);

                BytesOf(w).ShouldBe(new[] { (byte)MsgType.Hello });
            }

            [Fact]
            public void Forgets_the_last_message()
            {
                var w = new PacketWriter(new RecordingLog());
                w.Begin(MsgType.Hello).WriteStr("old");

                w.Begin(MsgType.Welcome);

                BytesOf(w).ShouldBe(new[] { (byte)MsgType.Welcome });
            }
        }

        public class Write
        {
            [Fact]
            public void Writes_a_vec3_as_three_little_endian_floats()
            {
                var w = new PacketWriter(new RecordingLog());
                w.Begin(MsgType.Hello);

                w.Write(new Vec3(1f, -2f, 0.5f));

                BytesOf(w).ShouldBe(new byte[]
                {
                    (byte)MsgType.Hello,
                    0x00, 0x00, 0x80, 0x3F,
                    0x00, 0x00, 0x00, 0xC0,
                    0x00, 0x00, 0x00, 0x3F,
                });
            }
        }

        public class WriteStr
        {
            [Fact]
            public void Writes_a_u16_length_then_utf8()
            {
                var w = new PacketWriter(new RecordingLog());
                w.Begin(MsgType.Hello);

                w.WriteStr("hä");

                BytesOf(w).ShouldBe(new byte[]
                {
                    (byte)MsgType.Hello, 3, 0, (byte)'h', 0xC3, 0xA4,
                });
            }

            [Fact]
            public void Writes_null_as_empty()
            {
                var w = new PacketWriter(new RecordingLog());
                w.Begin(MsgType.Hello);

                w.WriteStr(null);

                BytesOf(w).ShouldBe(new byte[] { (byte)MsgType.Hello, 0, 0 });
            }

            [Fact]
            public void Cuts_a_longer_string_and_logs_it()
            {
                var log = new RecordingLog();
                var w = new PacketWriter(log);
                w.Begin(MsgType.Hello);

                w.WriteStr(new string('a', ushort.MaxValue + 1));

                w.Length.ShouldBe(1 + 2 + ushort.MaxValue);
                log.Lines.Count.ShouldBe(1);
                log.Lines[0].ShouldStartWith("error: A 65536-byte string");
            }
        }

        public class WriteBigStr
        {
            [Fact]
            public void Writes_a_u32_length_then_utf8()
            {
                var w = new PacketWriter(new RecordingLog());
                w.Begin(MsgType.Hello);

                w.WriteBigStr("ab");

                BytesOf(w).ShouldBe(new byte[]
                {
                    (byte)MsgType.Hello, 2, 0, 0, 0, (byte)'a', (byte)'b',
                });
            }
        }
    }
}
