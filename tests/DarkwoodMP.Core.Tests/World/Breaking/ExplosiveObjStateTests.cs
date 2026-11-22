using System;
using DarkwoodMP.Net;
using DarkwoodMP.Tests.Net;
using DarkwoodMP.World.Breaking;

namespace DarkwoodMP.Tests.World.Breaking
{
    public class ExplosiveObjStateTests
    {
        static PacketWriter Written(bool wentOff)
        {
            var w = new PacketWriter(new RecordingLog());
            w.Begin(MsgType.ObjState);
            ExplosiveObjState.Write(w, wentOff);
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
            public void Writes_1_for_one_that_went_off()
            {
                BytesOf(Written(true)).ShouldBe(new byte[] { 42, 1 });
            }

            [Fact]
            public void Writes_0_for_one_that_did_not()
            {
                BytesOf(Written(false)).ShouldBe(new byte[] { 42, 0 });
            }
        }

        public class Read
        {
            [Fact]
            public void Reads_back_one_that_went_off()
            {
                var w = Written(true);
                var r = new PacketReader(w.Buffer, w.Length);

                ExplosiveObjState.Read(r).ShouldBeTrue();
            }

            [Fact]
            public void Reads_back_one_that_did_not()
            {
                var w = Written(false);
                var r = new PacketReader(w.Buffer, w.Length);

                ExplosiveObjState.Read(r).ShouldBeFalse();
            }
        }
    }
}
