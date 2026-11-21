using System;
using DarkwoodMP.Net.Steam;

namespace DarkwoodMP.Tests.Net.Steam
{
    public class SteamPiecesTests
    {
        const byte Piece = 170;

        static ArraySegment<byte> Message(int length)
        {
            var data = new byte[length];
            for (int i = 0; i < length; i++)
            {
                data[i] = (byte)(i % 251);
            }

            return new ArraySegment<byte>(data);
        }

        public class Limits
        {
            [Fact]
            public void Pins_the_heads()
            {
                var heads = new[]
                {
                    SteamPieces.FirstHead,
                    SteamPieces.NextHead,
                };

                heads.ShouldBe(new[] { 5, 1 });
            }
        }

        public class Split
        {
            [Fact]
            public void Makes_a_piece_per_256_kib_begun()
            {
                var pieces = SteamPieces.Split(Message(524289), Piece);

                pieces.ConvertAll(p => p.Length)
                    .ShouldBe(new[] { 262149, 262145, 2 });
            }

            [Fact]
            public void Heads_the_first_piece_with_the_length()
            {
                var pieces = SteamPieces.Split(Message(524289), Piece);

                pieces[0][..5].ShouldBe(new byte[] { 170, 1, 0, 8, 0 });
            }

            [Fact]
            public void Heads_the_later_pieces_with_the_type_alone()
            {
                var pieces = SteamPieces.Split(Message(524289), Piece);

                pieces[2].ShouldBe(new byte[] { 170, 524288 % 251 });
            }

            [Fact]
            public void Takes_the_data_from_the_segments_offset()
            {
                var data = new byte[] { 9, 9, 1, 2, 3 };
                var message = new ArraySegment<byte>(data, 2, 3);

                var pieces = SteamPieces.Split(message, Piece);

                pieces[0].ShouldBe(new byte[] { 170, 3, 0, 0, 0, 1, 2, 3 });
            }
        }

        public class LengthOf
        {
            [Fact]
            public void Reads_the_length_little_endian()
            {
                var piece = new byte[] { 170, 1, 2, 3, 4 };

                var total = SteamPieces.LengthOf(new ArraySegment<byte>(piece));

                total.ShouldBe(0x04030201);
            }

            [Fact]
            public void Reads_minus_one_from_a_piece_short_of_its_head()
            {
                var piece = new byte[] { 170, 1, 2, 3 };

                var total = SteamPieces.LengthOf(new ArraySegment<byte>(piece));

                total.ShouldBe(-1);
            }
        }

        public class MayStart
        {
            [Theory]
            [InlineData(1)]
            [InlineData(33554432)]
            public void Allows_a_length_in_range(int total)
            {
                var verdict = SteamPieces.MayStart(total, 33554432);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Theory]
            [InlineData(0)]
            [InlineData(-1)]
            [InlineData(33554433)]
            public void Refuses_a_length_out_of_range(int total)
            {
                var verdict = SteamPieces.MayStart(total, 33554432);

                verdict.Reason.ShouldBe(Refusal.BadMessageLength);
            }

            [Fact]
            public void Names_the_length()
            {
                var verdict = SteamPieces.MayStart(-1, 33554432);

                verdict.Detail.ShouldBe("-1");
            }
        }

        public class MayAppend
        {
            [Fact]
            public void Allows_pieces_exactly_at_the_length()
            {
                var verdict = SteamPieces.MayAppend(6, 4, 10);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Refuses_pieces_just_past_the_length()
            {
                var verdict = SteamPieces.MayAppend(6, 5, 10);

                verdict.Reason.ShouldBe(Refusal.PiecesTooLong);
            }
        }
    }
}
