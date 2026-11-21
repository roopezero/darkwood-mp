using System;
using DarkwoodMP.Net.Steam;

namespace DarkwoodMP.Tests.Net.Steam
{
    public class PieceJoinerTests
    {
        const ulong From = 76561198;

        /// The first piece of a 4-byte message, with two bytes of it.
        static ArraySegment<byte> First => Bytes(170, 4, 0, 0, 0, 1, 2);

        /// The rest of that message.
        static ArraySegment<byte> Rest => Bytes(170, 3, 4);

        static ArraySegment<byte> Bytes(params byte[] data) =>
            new ArraySegment<byte>(data);

        public class Add
        {
            [Fact]
            public void Waits_for_the_rest_of_a_message()
            {
                var joiner = new PieceJoiner(32 << 20);

                var answer = joiner.Add(From, 0, First);

                (answer.Whole, answer.Why).ShouldBe((null, Verdict.Allow));
            }

            [Fact]
            public void Gives_the_whole_message_with_its_last_piece()
            {
                var joiner = new PieceJoiner(32 << 20);
                joiner.Add(From, 0, First);

                var answer = joiner.Add(From, 0, Rest);

                answer.Whole.ShouldBe(new byte[] { 1, 2, 3, 4 });
            }

            [Fact]
            public void Forgets_a_message_once_whole()
            {
                var joiner = new PieceJoiner(32 << 20);
                joiner.Add(From, 0, First);

                joiner.Add(From, 0, Rest);

                joiner.Count.ShouldBe(0);
            }

            [Fact]
            public void Gives_a_one_piece_message_at_once()
            {
                var joiner = new PieceJoiner(32 << 20);

                var answer = joiner.Add(From, 0, Bytes(170, 1, 0, 0, 0, 9));

                answer.Whole.ShouldBe(new byte[] { 9 });
            }

            [Fact]
            public void Joins_each_channel_apart()
            {
                var joiner = new PieceJoiner(32 << 20);
                joiner.Add(From, 0, First);

                var answer = joiner.Add(From, 1, Rest);

                answer.Why.Reason.ShouldBe(Refusal.BadMessageLength);
            }

            [Fact]
            public void Refuses_a_first_piece_short_of_its_head()
            {
                var joiner = new PieceJoiner(32 << 20);

                var answer = joiner.Add(From, 0, Bytes(170, 4, 0));

                answer.Why.ShouldBe(
                    Verdict.Refuse(Refusal.BadMessageLength, "-1"));
            }

            [Fact]
            public void Refuses_a_length_past_the_limit()
            {
                var joiner = new PieceJoiner(3);

                var answer = joiner.Add(From, 0, First);

                answer.Why.Reason.ShouldBe(Refusal.BadMessageLength);
            }

            [Fact]
            public void Refuses_pieces_past_the_length()
            {
                var joiner = new PieceJoiner(32 << 20);
                joiner.Add(From, 0, First);

                var answer = joiner.Add(From, 0, Bytes(170, 3, 4, 5));

                answer.Why.Reason.ShouldBe(Refusal.PiecesTooLong);
            }

            [Fact]
            public void Forgets_a_message_whose_pieces_ran_past()
            {
                var joiner = new PieceJoiner(32 << 20);
                joiner.Add(From, 0, First);

                joiner.Add(From, 0, Bytes(170, 3, 4, 5));

                joiner.Count.ShouldBe(0);
            }

            [Fact]
            public void Takes_a_new_first_piece_as_more_of_a_short_message()
            {
                // A piece that never came leaves the message short: the next
                // message's first piece is joined to it, length bytes and all.
                var joiner = new PieceJoiner(32 << 20);
                joiner.Add(From, 0, Bytes(170, 6, 0, 0, 0, 1));

                var answer = joiner.Add(From, 0, Bytes(170, 2, 0, 0, 0, 7));

                answer.Whole.ShouldBe(new byte[] { 1, 2, 0, 0, 0, 7 });
            }
        }

        public class Forget
        {
            [Fact]
            public void Forgets_one_channel_of_a_sender()
            {
                var joiner = new PieceJoiner(32 << 20);
                joiner.Add(From, 0, First);
                joiner.Add(From, 1, First);

                joiner.Forget(From, 0);

                joiner.Count.ShouldBe(1);
            }
        }

        public class Clear
        {
            [Fact]
            public void Forgets_everything()
            {
                var joiner = new PieceJoiner(32 << 20);
                joiner.Add(From, 0, First);
                joiner.Add(From + 1, 1, First);

                joiner.Clear();

                joiner.Count.ShouldBe(0);
            }
        }
    }
}
