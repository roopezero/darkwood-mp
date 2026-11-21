using System;
using DarkwoodMP.Net;
using DarkwoodMP.Tests.Net;
using DarkwoodMP.World.KeptCharacters;

namespace DarkwoodMP.Tests.World.KeptCharacters
{
    public class CharacterSaveTests
    {
        static PacketWriter Written(in CharacterSaveBody body)
        {
            var w = new PacketWriter(new RecordingLog());
            w.Begin(MsgType.CharacterSave);
            CharacterSave.Write(w, body);
            return w;
        }

        public class Write
        {
            [Fact]
            public void Writes_the_strings_and_chapter_after_the_type()
            {
                var w = Written(new CharacterSaveBody
                {
                    Player = "p",
                    Inventory = "i",
                    Hotbar = null,
                    Chapter = 2,
                });

                var bytes = new byte[w.Length];
                Array.Copy(w.Buffer, bytes, w.Length);
                bytes.ShouldBe(new byte[]
                {
                    25,
                    1, 0, 0, 0, 112,
                    1, 0, 0, 0, 105,
                    0, 0, 0, 0,
                    2, 0, 0, 0,
                });
            }
        }

        public class Read
        {
            [Fact]
            public void Reads_back_what_was_written()
            {
                var w = Written(new CharacterSaveBody
                {
                    Player = "{}",
                    Inventory = "inv",
                    Hotbar = "hot",
                    Chapter = -1,
                });

                var r = new PacketReader(w.Buffer, w.Length);

                var body = CharacterSave.Read(r);

                body.Player.ShouldBe("{}");
                body.Inventory.ShouldBe("inv");
                body.Hotbar.ShouldBe("hot");
                body.Chapter.ShouldBe(-1);
            }
        }
    }
}
