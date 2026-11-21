using System;
using DarkwoodMP.Combat.Enemies;
using DarkwoodMP.Net;
using DarkwoodMP.Tests.Net;

namespace DarkwoodMP.Tests.Combat.Enemies
{
    public class EnemySpawnMessageTests
    {
        static readonly EnemySpawned Dog = new EnemySpawned
        {
            Id = 3,
            SavedId = 2,
            Prefab = "C/d",
            Kind = "d",
            At = new Vec3(1f, 2f, 0.5f),
            Rot = new Quat(0f, 0f, 0f, 1f),
            Armor = true,
        };

        static PacketWriter Written(EnemySpawned e)
        {
            var w = new PacketWriter(new RecordingLog());
            w.Begin(MsgType.EnemySpawn);
            EnemySpawnMessage.Write(w, e);
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
            public void Writes_id_save_id_prefab_kind_place_and_armour()
            {
                BytesOf(Written(Dog)).ShouldBe(new byte[]
                {
                    14,
                    3, 0, 0, 0,
                    2, 0, 0, 0,
                    3, 0, (byte)'C', (byte)'/', (byte)'d',
                    1, 0, (byte)'d',
                    0x00, 0x00, 0x80, 0x3F,
                    0x00, 0x00, 0x00, 0x40,
                    0x00, 0x00, 0x00, 0x3F,
                    0x00, 0x00, 0x00, 0x00,
                    0x00, 0x00, 0x00, 0x00,
                    0x00, 0x00, 0x00, 0x00,
                    0x00, 0x00, 0x80, 0x3F,
                    1,
                });
            }

            [Fact]
            public void Writes_0_with_no_armour()
            {
                var bytes = BytesOf(Written(Dog with { Armor = false }));

                bytes[bytes.Length - 1].ShouldBe((byte)0);
            }
        }

        public class Read
        {
            [Fact]
            public void Reads_back_what_was_written()
            {
                var w = Written(Dog);
                var e = EnemySpawnMessage.Read(
                    new PacketReader(w.Buffer, w.Length));

                e.Id.ShouldBe(3u);
                e.SavedId.ShouldBe(2);
                e.Prefab.ShouldBe("C/d");
                e.Kind.ShouldBe("d");
                e.At.ShouldBe(Dog.At);
                e.Rot.W.ShouldBe(1f);
                e.Armor.ShouldBeTrue();
            }
        }
    }
}
