using System;
using DarkwoodMP.Net;
using DarkwoodMP.Tests.Net;
using DarkwoodMP.World;

namespace DarkwoodMP.Tests.World
{
    public class RequestMessageTests
    {
        /// A WorldAction message (type 3) with two bytes of payload.
        static readonly byte[] Inner = { 3, 0xAA, 0xBB };

        static PacketWriter Written(uint seq)
        {
            var w = new PacketWriter(new RecordingLog());
            w.Begin(MsgType.Request);
            RequestMessage.Write(w, seq, new ArraySegment<byte>(Inner));
            return w;
        }

        public class Write
        {
            [Fact]
            public void Writes_the_number_then_the_whole_message()
            {
                var w = Written(0x01020304);

                var bytes = new byte[w.Length];
                Array.Copy(w.Buffer, bytes, w.Length);
                bytes.ShouldBe(new byte[]
                {
                    40,
                    0x04, 0x03, 0x02, 0x01,
                    3, 0xAA, 0xBB,
                });
            }

            [Fact]
            public void Writes_only_the_given_part_of_the_buffer()
            {
                var w = new PacketWriter(new RecordingLog());
                w.Begin(MsgType.Request);
                var buffer = new byte[] { 9, 3, 7, 9 };
                var part = new ArraySegment<byte>(buffer, 1, 2);

                RequestMessage.Write(w, 1, part);

                w.Length.ShouldBe(7);
                w.Buffer[5].ShouldBe((byte)3);
                w.Buffer[6].ShouldBe((byte)7);
            }
        }

        public class Read
        {
            [Fact]
            public void Reads_back_what_was_written()
            {
                var w = Written(77);
                var r = new PacketReader(w.Buffer, w.Length);

                var message = RequestMessage.Read(r, out uint seq);

                seq.ShouldBe(77u);
                message.ShouldBe(Inner);
            }

            [Fact]
            public void Reads_an_empty_message()
            {
                var w = new PacketWriter(new RecordingLog());
                w.Begin(MsgType.Request);
                var none = new ArraySegment<byte>(new byte[0]);
                RequestMessage.Write(w, 5, none);
                var r = new PacketReader(w.Buffer, w.Length);

                var message = RequestMessage.Read(r, out _);

                message.Length.ShouldBe(0);
            }
        }
    }
}
