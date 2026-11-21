using DarkwoodMP.Net;
using DarkwoodMP.Tests.Net;
using DarkwoodMP.World;
using DarkwoodMP.World.Inventories;

namespace DarkwoodMP.Tests.World.Inventories
{
    public class StorageSortMessageTests
    {
        static readonly ObjName Storage = new ObjName(ObjKind.Saved, 0x0201);

        static PacketWriter Written()
        {
            var w = new PacketWriter(new RecordingLog());
            w.Begin(MsgType.StorageSort);
            StorageSortMessage.Write(w, Storage);
            return w;
        }

        public class Write
        {
            [Fact]
            public void Writes_the_type_and_the_storage_name()
            {
                Bytes.Of(Written()).ShouldBe(new byte[]
                {
                    0xCA,
                    1,
                    1, 2, 0, 0, 0, 0, 0, 0,
                });
            }
        }

        public class Read
        {
            [Fact]
            public void Reads_back_the_storage_name()
            {
                StorageSortMessage.Read(Bytes.Back(Written()))
                    .ShouldBe(Storage);
            }
        }
    }
}
