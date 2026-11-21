using System;
using DarkwoodMP.Net;
using DarkwoodMP.Tests.Net;
using DarkwoodMP.World.Windows;

namespace DarkwoodMP.Tests.World.Windows
{
    public class WindowObjStateTests
    {
        static PacketWriter Written(HostWindow window)
        {
            var w = new PacketWriter(new RecordingLog());
            w.Begin(MsgType.ObjState);
            WindowObjState.Write(w, window);
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
            public void Writes_the_flag_and_the_barricade_health()
            {
                var window = new HostWindow
                {
                    Barricaded = true,
                    BarricadeHealth = 0x0201,
                };

                var w = Written(window);

                BytesOf(w).ShouldBe(new byte[] { 42, 1, 0x01, 0x02, 0, 0 });
            }
        }

        public class Read
        {
            [Fact]
            public void Reads_back_what_was_written()
            {
                var w = Written(new HostWindow { BarricadeHealth = 7 });
                var r = new PacketReader(w.Buffer, w.Length);

                var window = WindowObjState.Read(r);

                window.Barricaded.ShouldBeFalse();
                window.BarricadeHealth.ShouldBe(7);
            }
        }
    }
}
