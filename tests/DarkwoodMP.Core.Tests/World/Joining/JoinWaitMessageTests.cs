using DarkwoodMP.Net;
using DarkwoodMP.Tests.Net;
using DarkwoodMP.World.Joining;

namespace DarkwoodMP.Tests.World.Joining
{
    public class JoinWaitMessageTests
    {
        static PacketWriter Written(string why)
        {
            var w = new PacketWriter(new RecordingLog());
            w.Begin(MsgType.JoinWait);
            JoinWaitMessage.Write(w, why);
            return w;
        }

        public class Write
        {
            [Fact]
            public void Writes_why_after_the_type()
            {
                Bytes.Of(Written("ab"))
                    .ShouldBe(new byte[] { 34, 2, 0, 97, 98 });
            }
        }

        public class Read
        {
            [Fact]
            public void Reads_back_what_was_written()
            {
                var w = Written("The host is saving its world");
                var r = new PacketReader(w.Buffer, w.Length);

                JoinWaitMessage.Read(r)
                    .ShouldBe("The host is saving its world");
            }
        }
    }
}
