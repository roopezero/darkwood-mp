using System;
using DarkwoodMP.Combat.Joiners;
using DarkwoodMP.Net;
using DarkwoodMP.Tests.Net;

namespace DarkwoodMP.Tests.Combat.Joiners
{
    public class ViewRectMessageTests
    {
        static PacketWriter Written(ViewRect rect)
        {
            var w = new PacketWriter(new RecordingLog());
            w.Begin(MsgType.ViewRect);
            ViewRectMessage.Write(w, rect);
            return w;
        }

        static byte[] BytesOf(PacketWriter w)
        {
            var bytes = new byte[w.Length];
            Array.Copy(w.Buffer, bytes, w.Length);
            return bytes;
        }

        public class Write
        {
            [Fact]
            public void Writes_both_halves_as_u16()
            {
                var w = Written(new ViewRect(1200, 0x0304));

                BytesOf(w).ShouldBe(new byte[]
                {
                    250,
                    0xB0, 0x04,
                    0x04, 0x03,
                });
            }

            [Fact]
            public void Clamps_each_half_to_fit_a_u16()
            {
                var w = Written(new ViewRect(-5, 70000));

                BytesOf(w).ShouldBe(new byte[]
                {
                    250,
                    0, 0,
                    0xFF, 0xFF,
                });
            }
        }

        public class Read
        {
            [Fact]
            public void Reads_back_what_was_written()
            {
                var w = Written(new ViewRect(1200, 9000));
                var r = new PacketReader(w.Buffer, w.Length);

                ViewRectMessage.Read(r).ShouldBe(new ViewRect(1200, 9000));
            }
        }
    }
}
