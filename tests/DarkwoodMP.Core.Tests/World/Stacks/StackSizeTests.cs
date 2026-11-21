using System;
using DarkwoodMP.Net;
using DarkwoodMP.Tests.Net;
using DarkwoodMP.World.Stacks;

namespace DarkwoodMP.Tests.World.Stacks
{
    public class StackSizeTests
    {
        static PacketWriter Written(int size)
        {
            var w = new PacketWriter(new RecordingLog());
            w.Begin(MsgType.StackSize);
            StackSize.Write(w, size);
            return w;
        }

        static byte[] Bytes(PacketWriter w)
        {
            var bytes = new byte[w.Length];
            Array.Copy(w.Buffer, bytes, w.Length);
            return bytes;
        }

        public class Write
        {
            [Fact]
            public void Writes_the_size_as_a_little_endian_u16()
            {
                Bytes(Written(300)).ShouldBe(new byte[] { 251, 0x2C, 0x01 });
            }

            [Fact]
            public void Writes_a_size_below_0_as_0()
            {
                Bytes(Written(-5)).ShouldBe(new byte[] { 251, 0, 0 });
            }

            [Fact]
            public void Writes_a_size_past_a_u16_as_its_most()
            {
                Bytes(Written(70000)).ShouldBe(new byte[] { 251, 0xFF, 0xFF });
            }
        }

        public class Read
        {
            [Fact]
            public void Reads_back_what_was_written()
            {
                var w = Written(50);
                var r = new PacketReader(w.Buffer, w.Length);

                StackSize.Read(r).ShouldBe(50);
            }
        }
    }
}
