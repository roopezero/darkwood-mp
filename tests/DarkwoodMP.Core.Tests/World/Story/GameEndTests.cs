using System;
using DarkwoodMP.Net;
using DarkwoodMP.Tests.Net;
using DarkwoodMP.World.Story;

namespace DarkwoodMP.Tests.World.Story
{
    public class GameEndTests
    {
        static PacketWriter Written(EndWord word)
        {
            var w = new PacketWriter(new RecordingLog());
            w.Begin(MsgType.GameEnd);
            GameEnd.Write(w, word);
            return w;
        }

        static EndWord EndScreen()
        {
            return new EndWord { Kind = EndKind.EndScreen, Phase = 1 };
        }

        public class Write
        {
            [Fact]
            public void Writes_the_kind_and_the_phase()
            {
                var w = Written(EndScreen());

                var bytes = new byte[w.Length];
                Array.Copy(w.Buffer, bytes, w.Length);
                bytes.ShouldBe(new byte[] { 87, 2, 1 });
            }
        }

        public class Read
        {
            [Fact]
            public void Reads_back_what_was_written()
            {
                var w = Written(EndScreen());
                var r = new PacketReader(w.Buffer, w.Length);

                var word = GameEnd.Read(r);

                word.Kind.ShouldBe(EndKind.EndScreen);
                word.Phase.ShouldBe((byte)1);
            }

            [Fact]
            public void Keeps_an_unknown_kind()
            {
                var w = Written(new EndWord { Kind = (EndKind)9 });
                var r = new PacketReader(w.Buffer, w.Length);

                GameEnd.Read(r).Kind.ShouldBe((EndKind)9);
            }
        }
    }
}
