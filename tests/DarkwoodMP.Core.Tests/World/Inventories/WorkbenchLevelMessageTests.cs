using DarkwoodMP.Net;
using DarkwoodMP.Tests.Net;
using DarkwoodMP.World.Inventories;

namespace DarkwoodMP.Tests.World.Inventories
{
    public class WorkbenchLevelMessageTests
    {
        static PacketWriter Written(int level)
        {
            var w = new PacketWriter(new RecordingLog());
            w.Begin(MsgType.WorkbenchLevel);
            WorkbenchLevelMessage.Write(w, level);
            return w;
        }

        public class Write
        {
            [Fact]
            public void Writes_the_level_as_an_i32()
            {
                Bytes.Of(Written(0x0103)).ShouldBe(new byte[]
                {
                    39,
                    3, 1, 0, 0,
                });
            }
        }

        public class Read
        {
            [Fact]
            public void Reads_back_the_level()
            {
                WorkbenchLevelMessage.Read(Bytes.Back(Written(4)))
                    .ShouldBe(4);
            }
        }
    }
}
