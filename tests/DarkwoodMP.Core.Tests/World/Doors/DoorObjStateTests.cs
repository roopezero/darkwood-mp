using System;
using DarkwoodMP.Net;
using DarkwoodMP.Tests.Net;
using DarkwoodMP.World.Doors;

namespace DarkwoodMP.Tests.World.Doors
{
    public class DoorObjStateTests
    {
        static PacketWriter Written(HostDoor door)
        {
            var w = new PacketWriter(new RecordingLog());
            w.Begin(MsgType.ObjState);
            DoorObjState.Write(w, door);
            return w;
        }

        static byte[] BytesOf(PacketWriter w)
        {
            var bytes = new byte[w.Length];
            Array.Copy(w.Buffer, bytes, w.Length);
            return bytes;
        }

        static HostDoor LockedShut() => new HostDoor
        {
            Status = new DoorStatus { Locked = true, Barricaded = true },
            Blocked = true,
            Health = 0x0102,
            BarricadeHealth = 3,
            KeyType = "k",
        };

        public class Write
        {
            [Fact]
            public void Writes_the_flags_health_and_key_type()
            {
                var w = Written(LockedShut());

                BytesOf(w).ShouldBe(new byte[]
                {
                    42,
                    2 | 8 | 16,
                    0x02, 0x01, 0, 0,
                    3, 0, 0, 0,
                    1, 0, (byte)'k',
                });
            }

            [Fact]
            public void Writes_an_open_broken_door_with_no_key_type()
            {
                var door = new HostDoor
                {
                    Status = new DoorStatus
                    {
                        Opened = true,
                        Destroyed = true,
                    },
                };

                var w = Written(door);

                BytesOf(w).ShouldBe(new byte[]
                {
                    42, 1 | 4, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
                });
            }
        }

        public class Read
        {
            [Fact]
            public void Reads_back_what_was_written()
            {
                var w = Written(LockedShut());
                var r = new PacketReader(w.Buffer, w.Length);

                var door = DoorObjState.Read(r);

                door.Status.Locked.ShouldBeTrue();
                door.Status.Barricaded.ShouldBeTrue();
                door.Status.Opened.ShouldBeFalse();
                door.Status.Destroyed.ShouldBeFalse();
                door.Blocked.ShouldBeTrue();
                door.Health.ShouldBe(0x0102);
                door.BarricadeHealth.ShouldBe(3);
                door.KeyType.ShouldBe("k");
            }
        }
    }
}
