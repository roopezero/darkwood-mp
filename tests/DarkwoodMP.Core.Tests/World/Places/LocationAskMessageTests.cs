using System;
using DarkwoodMP.Net;
using DarkwoodMP.Tests.Net;
using DarkwoodMP.World.Places;

namespace DarkwoodMP.Tests.World.Places
{
    public class LocationAskMessageTests
    {
        static byte[] BytesOf(LocationAsk ask)
        {
            var w = new PacketWriter(new RecordingLog());
            w.Begin(MsgType.LocationAsk);
            LocationAskMessage.Write(w, ask);
            var bytes = new byte[w.Length];
            Array.Copy(w.Buffer, bytes, w.Length);
            return bytes;
        }

        public class Write
        {
            [Fact]
            public void Writes_the_dream_flag_and_the_name()
            {
                var ask = new LocationAsk { Dream = true, Name = "ab" };

                BytesOf(ask).ShouldBe(new byte[]
                {
                    115,
                    1,
                    2, 0, (byte)'a', (byte)'b',
                });
            }

            [Fact]
            public void Writes_no_flag_for_a_location()
            {
                var ask = new LocationAsk { Dream = false, Name = "a" };

                BytesOf(ask).ShouldBe(new byte[]
                {
                    115,
                    0,
                    1, 0, (byte)'a',
                });
            }
        }

        public class Read
        {
            [Fact]
            public void Reads_back_what_was_written()
            {
                var bytes = BytesOf(
                    new LocationAsk { Dream = true, Name = "Bunker" });

                var read = LocationAskMessage.Read(
                    new PacketReader(bytes, bytes.Length));

                read.Dream.ShouldBeTrue();
                read.Name.ShouldBe("Bunker");
            }

            [Fact]
            public void Reads_only_bit_0_as_the_dream()
            {
                var bytes = new byte[] { 115, 2, 1, 0, (byte)'a' };

                var read = LocationAskMessage.Read(
                    new PacketReader(bytes, bytes.Length));

                read.Dream.ShouldBeFalse();
            }
        }
    }
}
