using System;
using System.Collections.Generic;
using DarkwoodMP.Net;
using DarkwoodMP.Tests.Net;
using DarkwoodMP.World.Npcs;

namespace DarkwoodMP.Tests.World.Npcs
{
    public class NpcPosMessageTests
    {
        static List<NpcPlace> Places(int count)
        {
            var places = new List<NpcPlace>();
            for (int i = 0; i < count; i++)
            {
                places.Add(
                    new NpcPlace { Id = i + 1, At = new Vec2(i, -i) });
            }

            return places;
        }

        public class Write
        {
            [Fact]
            public void Writes_the_count_then_each_id_x_and_z()
            {
                var w = new PacketWriter(new RecordingLog());
                w.Begin(MsgType.NpcPos);
                var places = new List<NpcPlace>
                {
                    new NpcPlace { Id = 7, At = new Vec2(1f, -2f) },
                };

                NpcPosMessage.Write(w, places, 0);

                var bytes = new byte[w.Length];
                Array.Copy(w.Buffer, bytes, w.Length);
                bytes.ShouldBe(new byte[]
                {
                    230,
                    0x01, 0x00,
                    0x07, 0x00, 0x00, 0x00,
                    0x00, 0x00, 0x80, 0x3F,
                    0x00, 0x00, 0x00, 0xC0,
                });
            }

            [Fact]
            public void Writes_at_most_200_from_start()
            {
                var w = new PacketWriter(new RecordingLog());
                w.Begin(MsgType.NpcPos);

                int n = NpcPosMessage.Write(w, Places(250), 0);

                n.ShouldBe(200);
                w.Length.ShouldBe(1 + 2 + 200 * 12);
            }

            [Fact]
            public void Writes_the_rest_after_start()
            {
                var w = new PacketWriter(new RecordingLog());
                w.Begin(MsgType.NpcPos);

                int n = NpcPosMessage.Write(w, Places(250), 200);

                n.ShouldBe(50);
                var r = new PacketReader(w.Buffer, w.Length);
                NpcPosMessage.Read(r)[0].Id.ShouldBe(201);
            }
        }

        public class Read
        {
            [Fact]
            public void Reads_back_what_was_written()
            {
                var w = new PacketWriter(new RecordingLog());
                w.Begin(MsgType.NpcPos);
                NpcPosMessage.Write(w, Places(3), 0);
                var r = new PacketReader(w.Buffer, w.Length);

                var places = NpcPosMessage.Read(r);

                places.Count.ShouldBe(3);
                places[2].Id.ShouldBe(3);
                places[2].At.ShouldBe(new Vec2(2f, -2f));
            }
        }
    }
}
