using System;
using DarkwoodMP.Combat.Hostiles;
using DarkwoodMP.Net;
using DarkwoodMP.Tests.Net;
using DarkwoodMP.World;

namespace DarkwoodMP.Tests.Combat.Hostiles
{
    public class NpcAttackMessageTests
    {
        static readonly ObjName Trader = new ObjName(ObjKind.Saved, 2);

        static PacketWriter Written(ObjName npc)
        {
            var w = new PacketWriter(new RecordingLog());
            w.Begin(MsgType.NpcAttack);
            NpcAttackMessage.Write(w, npc);
            return w;
        }

        public class Write
        {
            [Fact]
            public void Writes_the_npc()
            {
                var w = Written(Trader);
                var bytes = new byte[w.Length];
                Array.Copy(w.Buffer, bytes, w.Length);

                bytes.ShouldBe(new byte[]
                {
                    241,
                    1,
                    2, 0, 0, 0, 0, 0, 0, 0,
                });
            }
        }

        public class Read
        {
            [Fact]
            public void Reads_back_what_was_written()
            {
                var w = Written(Trader);
                var r = new PacketReader(w.Buffer, w.Length);

                NpcAttackMessage.Read(r).ShouldBe(Trader);
            }
        }
    }
}
