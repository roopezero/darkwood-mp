using System;
using DarkwoodMP.Combat.Enemies;
using DarkwoodMP.Net;
using DarkwoodMP.Tests.Net;

namespace DarkwoodMP.Tests.Combat.Enemies
{
    public class EnemyMarkMessageTests
    {
        static PacketWriter Written(MsgType type, EnemyMark m)
        {
            var w = new PacketWriter(new RecordingLog());
            w.Begin(type);
            EnemyMarkMessage.Write(w, m);
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
            public void Writes_the_id_and_1_for_armour_on()
            {
                var m = new EnemyMark { Id = 258, On = true };

                BytesOf(Written(MsgType.EnemyArmor, m)).ShouldBe(new byte[]
                {
                    101, 2, 1, 0, 0, 1,
                });
            }

            [Fact]
            public void Writes_0_for_a_corpse_not_kept()
            {
                var m = new EnemyMark { Id = 7, On = false };

                BytesOf(Written(MsgType.EnemyDespawn, m)).ShouldBe(new byte[]
                {
                    16, 7, 0, 0, 0, 0,
                });
            }
        }

        public class Read
        {
            [Fact]
            public void Reads_back_what_was_written()
            {
                var w = Written(
                    MsgType.EnemyDespawn, new EnemyMark { Id = 7, On = true });
                var m = EnemyMarkMessage.Read(
                    new PacketReader(w.Buffer, w.Length));

                m.Id.ShouldBe(7u);
                m.On.ShouldBeTrue();
            }
        }
    }
}
