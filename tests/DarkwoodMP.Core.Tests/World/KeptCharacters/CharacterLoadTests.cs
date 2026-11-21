using System;
using DarkwoodMP.Net;
using DarkwoodMP.Tests.Net;
using DarkwoodMP.World.KeptCharacters;

namespace DarkwoodMP.Tests.World.KeptCharacters
{
    public class CharacterLoadTests
    {
        static PacketWriter Written(in CharacterLoadBody body)
        {
            var w = new PacketWriter(new RecordingLog());
            w.Begin(MsgType.CharacterLoad);
            CharacterLoad.Write(w, body);
            return w;
        }

        public class Write
        {
            [Fact]
            public void Writes_the_kind_strings_and_place_after_the_type()
            {
                var w = Written(new CharacterLoadBody
                {
                    Kind = CharacterKind.OwnBesideHost,
                    Player = "p",
                    Inventory = null,
                    Hotbar = "hb",
                    At = new Vec3(1f, 0f, -2f),
                });

                var bytes = new byte[w.Length];
                Array.Copy(w.Buffer, bytes, w.Length);
                bytes.ShouldBe(new byte[]
                {
                    26, 3,
                    1, 0, 0, 0, 112,
                    0, 0, 0, 0,
                    2, 0, 0, 0, 104, 98,
                    0, 0, 128, 63, 0, 0, 0, 0, 0, 0, 0, 192,
                });
            }
        }

        public class Read
        {
            [Fact]
            public void Reads_back_what_was_written()
            {
                var w = Written(new CharacterLoadBody
                {
                    Kind = CharacterKind.Dead,
                    Player = "{}",
                    Inventory = "inv",
                    Hotbar = "",
                    At = new Vec3(3f, 4f, 5f),
                });

                var r = new PacketReader(w.Buffer, w.Length);

                var body = CharacterLoad.Read(r);

                body.Kind.ShouldBe(CharacterKind.Dead);
                body.Player.ShouldBe("{}");
                body.Inventory.ShouldBe("inv");
                body.Hotbar.ShouldBe("");
                body.At.ShouldBe(new Vec3(3f, 4f, 5f));
            }

            [Fact]
            public void Keeps_a_kind_with_no_name()
            {
                var w = Written(new CharacterLoadBody
                {
                    Kind = (CharacterKind)9,
                });

                var r = new PacketReader(w.Buffer, w.Length);

                ((byte)CharacterLoad.Read(r).Kind).ShouldBe((byte)9);
            }
        }
    }
}
