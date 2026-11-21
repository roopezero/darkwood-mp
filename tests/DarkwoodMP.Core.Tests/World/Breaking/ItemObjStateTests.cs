using System;
using DarkwoodMP.Net;
using DarkwoodMP.Tests.Net;
using DarkwoodMP.World.Breaking;

namespace DarkwoodMP.Tests.World.Breaking
{
    public class ItemObjStateTests
    {
        static PacketWriter Written(HostItem item)
        {
            var w = new PacketWriter(new RecordingLog());
            w.Begin(MsgType.ObjState);
            ItemObjState.Write(w, item);
            return w;
        }

        static byte[] BytesOf(PacketWriter w)
        {
            var bytes = new byte[w.Length];
            Array.Copy(w.Buffer, bytes, w.Length);
            return bytes;
        }

        static HostItem LockedFeeder() => new HostItem
        {
            Locked = true,
            Broken = true,
            FeederFull = true,
            Health = 0x0102,
        };

        static HostItem SwitchedOn() => new HostItem
        {
            On = true,
            Searched = true,
            Switched = true,
            Health = -1,
        };

        public class Write
        {
            [Fact]
            public void Writes_the_lock_break_and_feeder_flags_and_health()
            {
                var w = Written(LockedFeeder());

                BytesOf(w).ShouldBe(new byte[]
                {
                    42, 2 | 8 | 32, 0x02, 0x01, 0, 0,
                });
            }

            [Fact]
            public void Writes_the_switch_and_search_flags_and_health()
            {
                var w = Written(SwitchedOn());

                BytesOf(w).ShouldBe(new byte[]
                {
                    42, 1 | 4 | 16, 0xFF, 0xFF, 0xFF, 0xFF,
                });
            }

            [Fact]
            public void Writes_no_flags_for_a_plain_item()
            {
                var w = Written(new HostItem());

                BytesOf(w).ShouldBe(new byte[] { 42, 0, 0, 0, 0, 0 });
            }
        }

        public class Read
        {
            [Fact]
            public void Reads_back_a_locked_feeder()
            {
                var w = Written(LockedFeeder());
                var r = new PacketReader(w.Buffer, w.Length);

                ItemObjState.Read(r).ShouldBe(LockedFeeder());
            }

            [Fact]
            public void Reads_back_a_switched_item()
            {
                var w = Written(SwitchedOn());
                var r = new PacketReader(w.Buffer, w.Length);

                ItemObjState.Read(r).ShouldBe(SwitchedOn());
            }
        }
    }
}
