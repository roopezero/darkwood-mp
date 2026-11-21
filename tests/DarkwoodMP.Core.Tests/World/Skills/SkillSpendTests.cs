using System;
using System.Collections.Generic;
using DarkwoodMP.Net;
using DarkwoodMP.Tests.Net;
using DarkwoodMP.World.Skills;

namespace DarkwoodMP.Tests.World.Skills
{
    public class SkillSpendTests
    {
        static PacketWriter Written(in SpendRequest spend)
        {
            var w = new PacketWriter(new RecordingLog());
            w.Begin(MsgType.SkillSpend);
            SkillSpend.Write(w, spend);
            return w;
        }

        static byte[] Bytes(PacketWriter w)
        {
            var bytes = new byte[w.Length];
            Array.Copy(w.Buffer, bytes, w.Length);
            return bytes;
        }

        public class Write
        {
            [Fact]
            public void Writes_the_choice_after_the_type()
            {
                var w = Written(new SpendRequest
                {
                    Level = 3,
                    Good = "a",
                    Negative = "b",
                    Had = new List<string> { "c" },
                });

                Bytes(w).ShouldBe(new byte[]
                {
                    160, 3, 1, 0, 97, 1, 0, 98, 1, 1, 0, 99,
                });
            }

            [Fact]
            public void Writes_no_negative_skill_as_an_empty_string()
            {
                var w = Written(new SpendRequest { Level = 1, Good = "a" });

                Bytes(w).ShouldBe(new byte[] { 160, 1, 1, 0, 97, 0, 0, 0 });
            }

            [Fact]
            public void Clamps_the_level_to_a_byte()
            {
                var w = Written(new SpendRequest { Level = 300, Good = "" });

                Bytes(w)[1].ShouldBe((byte)255);
            }

            [Fact]
            public void Lists_at_most_255_skills_had()
            {
                var had = new List<string>();
                for (int i = 0; i < 300; i++)
                {
                    had.Add("");
                }

                var w = Written(new SpendRequest { Good = "", Had = had });

                var bytes = Bytes(w);
                bytes[6].ShouldBe((byte)255);
                bytes.Length.ShouldBe(7 + 255 * 2);
            }
        }

        public class Read
        {
            [Fact]
            public void Reads_back_what_was_written()
            {
                var w = Written(new SpendRequest
                {
                    Level = 4,
                    Good = "good",
                    Negative = "",
                    Had = new List<string> { "x", "y" },
                });

                var r = new PacketReader(w.Buffer, w.Length);

                var spend = SkillSpend.Read(r);

                spend.Level.ShouldBe(4);
                spend.Good.ShouldBe("good");
                spend.Negative.ShouldBe("");
                spend.Had.ShouldBe(new[] { "x", "y" });
            }
        }
    }
}
