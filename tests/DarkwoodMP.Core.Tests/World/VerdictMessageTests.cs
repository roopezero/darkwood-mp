using System;
using DarkwoodMP.Net;
using DarkwoodMP.Tests.Net;
using DarkwoodMP.World;

namespace DarkwoodMP.Tests.World
{
    public class VerdictMessageTests
    {
        static PacketWriter Written(uint seq, RequestAnswer answer)
        {
            var w = new PacketWriter(new RecordingLog());
            w.Begin(MsgType.Verdict);
            VerdictMessage.Write(w, seq, answer);
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
            public void Writes_an_accepted_request_with_its_result()
            {
                var asked = AuthorityRules.Begin(2);
                var answer = AuthorityRules.Result(asked, "ok");

                var w = Written(0x0A0B0C0D, answer);

                BytesOf(w).ShouldBe(new byte[]
                {
                    41,
                    0x0D, 0x0C, 0x0B, 0x0A,
                    1,
                    0, 0,
                    2, 0, (byte)'o', (byte)'k',
                });
            }

            [Fact]
            public void Writes_a_refusal_with_its_words()
            {
                var answer = AuthorityRules.Refuse(
                    AuthorityRules.Begin(2),
                    Verdict.Refuse(Refusal.EmptyRequest));

                var w = Written(1, answer);

                var words = "empty request";
                var bytes = BytesOf(w);
                bytes.Length.ShouldBe(1 + 4 + 1 + 2 + words.Length + 2);
                bytes[5].ShouldBe((byte)0);
                bytes[6].ShouldBe((byte)words.Length);
                bytes[7].ShouldBe((byte)0);
                bytes[8].ShouldBe((byte)'e');
            }
        }

        public class Read
        {
            [Fact]
            public void Reads_back_what_was_written()
            {
                var answer = AuthorityRules.Refuse(
                    AuthorityRules.Begin(2),
                    Verdict.Refuse(Refusal.HandlerFailed, "boom"));
                var w = Written(9, answer);
                var r = new PacketReader(w.Buffer, w.Length);

                var reply = VerdictMessage.Read(r);

                reply.Seq.ShouldBe(9u);
                reply.Ok.ShouldBeFalse();
                reply.Reason.ShouldBe("error: boom");
                reply.Result.ShouldBe("");
            }
        }
    }
}
