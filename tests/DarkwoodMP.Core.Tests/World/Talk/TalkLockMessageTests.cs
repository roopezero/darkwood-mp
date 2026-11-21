using System;
using DarkwoodMP.Net;
using DarkwoodMP.Tests.Net;
using DarkwoodMP.World;
using DarkwoodMP.World.Talk;

namespace DarkwoodMP.Tests.World.Talk
{
    public class TalkLockMessageTests
    {
        static PacketWriter Written(TalkHold hold)
        {
            var w = new PacketWriter(new RecordingLog());
            w.Begin(MsgType.TalkLock);
            TalkLockMessage.Write(w, hold);
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
            public void Writes_the_ref_the_talking_byte_and_the_name()
            {
                var w = Written(new TalkHold
                {
                    Npc = new ObjName(ObjKind.Spawned, 0x0102),
                    Talking = true,
                    Name = "wolf",
                });

                Bytes(w).ShouldBe(new byte[]
                {
                    68,
                    2,
                    0x02, 0x01, 0, 0, 0, 0, 0, 0,
                    1,
                    4, 0,
                    (byte)'w', (byte)'o', (byte)'l', (byte)'f',
                });
            }

            [Fact]
            public void Writes_a_close_as_0_and_no_name_as_empty()
            {
                var w = Written(new TalkHold
                {
                    Npc = new ObjName(ObjKind.Saved, 7),
                    Talking = false,
                    Name = null,
                });

                Bytes(w).ShouldBe(new byte[]
                {
                    68,
                    1,
                    7, 0, 0, 0, 0, 0, 0, 0,
                    0,
                    0, 0,
                });
            }
        }

        public class Read
        {
            [Fact]
            public void Reads_back_what_was_written()
            {
                var w = Written(new TalkHold
                {
                    Npc = new ObjName(ObjKind.Placed, -5),
                    Talking = true,
                    Name = "trader",
                });

                var r = new PacketReader(w.Buffer, w.Length);

                var hold = TalkLockMessage.Read(r);

                hold.Npc.ShouldBe(new ObjName(ObjKind.Placed, -5));
                hold.Talking.ShouldBeTrue();
                hold.Name.ShouldBe("trader");
            }
        }
    }
}
