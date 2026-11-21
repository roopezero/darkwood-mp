using System;
using DarkwoodMP.Net;
using DarkwoodMP.Tests.Net;
using DarkwoodMP.World;
using DarkwoodMP.World.Dragging;

namespace DarkwoodMP.Tests.World.Dragging
{
    public class DragOwnerTests
    {
        static PacketWriter Written(OwnerWord word)
        {
            var w = new PacketWriter(new RecordingLog());
            w.Begin(MsgType.DragOwner);
            DragOwner.Write(w, word);
            return w;
        }

        static OwnerWord Sample(bool letGo)
        {
            return new OwnerWord
            {
                Name = new ObjName(ObjKind.Saved, 7),
                Owner = 3,
                LetGo = letGo,
            };
        }

        public class Write
        {
            [Fact]
            public void Writes_the_name_owner_and_let_go_flag()
            {
                var w = Written(Sample(true));

                var bytes = new byte[w.Length];
                Array.Copy(w.Buffer, bytes, w.Length);
                bytes.ShouldBe(new byte[]
                {
                    49,
                    1,
                    7, 0, 0, 0, 0, 0, 0, 0,
                    3, 0, 0, 0, 0, 0, 0, 0,
                    1,
                });
            }

            [Fact]
            public void Writes_a_zero_flag_when_nobody_lets_go()
            {
                var w = Written(Sample(false));

                w.Buffer[w.Length - 1].ShouldBe((byte)0);
            }
        }

        public class Read
        {
            [Fact]
            public void Reads_back_what_was_written()
            {
                var w = Written(Sample(true));
                var r = new PacketReader(w.Buffer, w.Length);

                var word = DragOwner.Read(r);

                word.Name.ShouldBe(new ObjName(ObjKind.Saved, 7));
                word.Owner.ShouldBe(3UL);
                word.LetGo.ShouldBeTrue();
            }
        }
    }
}
