using System;
using DarkwoodMP.Net;
using DarkwoodMP.Tests.Net;
using DarkwoodMP.World.Clock;

namespace DarkwoodMP.Tests.World.Clock
{
    public class TimeSyncMessageTests
    {
        static PacketWriter Written(int day, int minute)
        {
            var w = new PacketWriter(new RecordingLog());
            w.Begin(MsgType.TimeSync);
            TimeSyncMessage.Write(
                w,
                new HostClock { Day = day, Minute = minute });
            return w;
        }

        public class Write
        {
            [Fact]
            public void Writes_the_day_as_u32_and_the_minute_as_u16()
            {
                var w = Written(3, 1439);

                var bytes = new byte[w.Length];
                Array.Copy(w.Buffer, bytes, w.Length);
                bytes.ShouldBe(new byte[]
                {
                    20,
                    0x03, 0x00, 0x00, 0x00,
                    0x9F, 0x05,
                });
            }
        }

        public class Read
        {
            [Fact]
            public void Reads_back_what_was_written()
            {
                var w = Written(41, 720);
                var r = new PacketReader(w.Buffer, w.Length);

                var clock = TimeSyncMessage.Read(r);

                clock.Day.ShouldBe(41);
                clock.Minute.ShouldBe(720);
            }
        }
    }
}
