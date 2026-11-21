using DarkwoodMP.World.Joining;

namespace DarkwoodMP.Tests.World.Joining
{
    public class OfferRulesTests
    {
        const uint Piece = 256 << 10;

        /// A Deflate-packed world of two files, slot 1.
        static readonly OfferFacts Offer = new OfferFacts
        {
            Files = new[]
            {
                new OfferedFile { Name = "sav.dat", Size = 600 },
                new OfferedFile { Name = "savs.dat", Size = 400 },
            },
            Packing = 1,
            Raw = 1000,
            Size = 300,
            Profile = true,
            ProfileId = 1,
        };

        /// The second piece of a world of three and a bit.
        static readonly PieceFacts Second = new PieceFacts
        {
            Offset = Piece,
            Length = (int)Piece,
            Total = (int)(3 * Piece + 10),
        };

        public class Check
        {
            [Fact]
            public void Allows_a_good_offer()
            {
                OfferRules.Check(Offer).Refused.ShouldBeFalse();
            }

            [Fact]
            public void Pins_the_limits()
            {
                OfferRules.MaxSize.ShouldBe(134217728L);
                OfferRules.PieceSize.ShouldBe(262144);
            }

            [Fact]
            public void Refuses_a_file_a_world_hasnt()
            {
                var verdict = OfferRules.Check(Offer with
                {
                    Files = new[]
                    {
                        new OfferedFile { Name = "sav.exe", Size = 1000 },
                    },
                });

                verdict.ShouldBe(
                    Verdict.Refuse(Refusal.UnexpectedFile, "sav.exe"));
            }

            [Fact]
            public void Refuses_a_file_named_twice()
            {
                var verdict = OfferRules.Check(Offer with
                {
                    Files = new[]
                    {
                        new OfferedFile { Name = "sav.dat", Size = 500 },
                        new OfferedFile { Name = "sav.dat", Size = 500 },
                    },
                });

                verdict.Reason.ShouldBe(Refusal.UnexpectedFile);
            }

            [Fact]
            public void Puts_the_files_before_the_packing()
            {
                var verdict = OfferRules.Check(Offer with
                {
                    Files = new[] { new OfferedFile { Name = null } },
                    Packing = 9,
                });

                verdict.Reason.ShouldBe(Refusal.UnexpectedFile);
            }

            [Fact]
            public void Refuses_an_unknown_packing()
            {
                var verdict = OfferRules.Check(Offer with
                {
                    Packing = 2,
                    Raw = 0,
                });

                verdict.ShouldBe(Verdict.Refuse(Refusal.UnknownPacking, "2"));
            }

            [Theory]
            [InlineData(1, 1000u, 300u, false)]
            [InlineData(1, 999u, 300u, true)]
            [InlineData(1, 1000u, 0u, true)]
            [InlineData(0, 1000u, 1000u, false)]
            [InlineData(0, 1000u, 999u, true)]
            public void Refuses_sizes_that_dont_add_up(
                byte packing,
                uint raw,
                uint size,
                bool refused)
            {
                var verdict = OfferRules.Check(Offer with
                {
                    Packing = packing,
                    Raw = raw,
                    Size = size,
                });

                verdict.Refused.ShouldBe(refused);
            }

            [Fact]
            public void Refuses_an_empty_world()
            {
                var verdict = OfferRules.Check(Offer with
                {
                    Files = new OfferedFile[0],
                    Raw = 0,
                });

                verdict.Reason.ShouldBe(Refusal.BadWorldSize);
            }

            [Theory]
            [InlineData(134217728u, false)]
            [InlineData(134217729u, true)]
            public void Refuses_a_world_over_the_most(uint size, bool refused)
            {
                var verdict = OfferRules.Check(Offer with
                {
                    Files = new[]
                    {
                        new OfferedFile { Name = "sav.dat", Size = size },
                    },
                    Packing = 0,
                    Raw = size,
                    Size = size,
                });

                verdict.Refused.ShouldBe(refused);
            }

            [Fact]
            public void Refuses_a_sent_size_over_the_largest()
            {
                var verdict = OfferRules.Check(Offer with
                {
                    Size = 134217729u,
                });

                verdict.Reason.ShouldBe(Refusal.BadWorldSize);
            }

            [Fact]
            public void Puts_the_sizes_before_the_profile()
            {
                var verdict = OfferRules.Check(Offer with
                {
                    Size = 0,
                    ProfileError = "bad",
                });

                verdict.Reason.ShouldBe(Refusal.BadWorldSize);
            }

            [Fact]
            public void Refuses_a_profile_it_cant_read()
            {
                var verdict = OfferRules.Check(Offer with
                {
                    ProfileError = "Unexpected character",
                    Profile = false,
                });

                verdict.ShouldBe(Verdict.Refuse(
                    Refusal.UnreadableProfile,
                    "Unexpected character"));
            }

            [Fact]
            public void Refuses_a_null_profile()
            {
                var verdict = OfferRules.Check(Offer with { Profile = false });

                verdict.Reason.ShouldBe(Refusal.InvalidProfile);
            }

            [Theory]
            [InlineData(0, true)]
            [InlineData(1, false)]
            [InlineData(99, false)]
            [InlineData(100, true)]
            public void Refuses_a_slot_outside_1_to_99(int id, bool refused)
            {
                var verdict = OfferRules.Check(Offer with { ProfileId = id });

                verdict.Refused.ShouldBe(refused);
            }
        }

        public class CheckPiece
        {
            [Fact]
            public void Allows_a_whole_piece()
            {
                OfferRules.CheckPiece(Second).Refused.ShouldBeFalse();
            }

            [Theory]
            [InlineData(0)]
            [InlineData(-1)]
            public void Refuses_an_empty_piece(int length)
            {
                var verdict = OfferRules.CheckPiece(Second with
                {
                    Length = length,
                });

                verdict.Reason.ShouldBe(Refusal.BadPiece);
            }

            [Fact]
            public void Refuses_a_piece_off_a_boundary()
            {
                var verdict = OfferRules.CheckPiece(Second with
                {
                    Offset = Piece + 1,
                    Length = 1,
                });

                verdict.Reason.ShouldBe(Refusal.BadPiece);
            }

            [Theory]
            [InlineData(10, false)]
            [InlineData(11, true)]
            public void Refuses_a_piece_past_the_end(int length, bool refused)
            {
                var verdict = OfferRules.CheckPiece(Second with
                {
                    Offset = 3 * Piece,
                    Length = length,
                });

                verdict.Refused.ShouldBe(refused);
            }

            [Fact]
            public void Allows_a_short_piece_that_isnt_the_last()
            {
                var verdict = OfferRules.CheckPiece(Second with
                {
                    Length = 5,
                });

                verdict.Refused.ShouldBeFalse();
            }
        }

        public class Serves
        {
            [Theory]
            [InlineData(0u, 1000u, 5000, 1000)]
            [InlineData(0u, 300000u, 400000, 262144)]
            [InlineData(262144u, 262144u, 300000, 37856)]
            [InlineData(300000u, 262144u, 300000, -1)]
            [InlineData(300001u, 262144u, 300000, -1)]
            public void Serves_at_most_a_piece_of_what_is_left(
                uint offset,
                uint asked,
                int size,
                int sent)
            {
                OfferRules.Serves(offset, asked, size).ShouldBe(sent);
            }
        }

        public class Pieces
        {
            [Theory]
            [InlineData(1u, 1)]
            [InlineData(262144u, 1)]
            [InlineData(262145u, 2)]
            public void Counts_pieces_rounding_up(uint size, int pieces)
            {
                OfferRules.Pieces(size).ShouldBe(pieces);
            }
        }

        public class Stalled
        {
            [Theory]
            [InlineData(109.9f, false)]
            [InlineData(110f, true)]
            public void Stalls_after_10_seconds(float now, bool stalled)
            {
                var result = OfferRules.Stalled(
                    new Seconds(100f),
                    new Seconds(now));

                result.ShouldBe(stalled);
            }
        }

        public class AfterStall
        {
            [Fact]
            public void Goes_on_after_3_stalls()
            {
                OfferRules.AfterStall(3, 0, 1).Refused.ShouldBeFalse();
            }

            [Fact]
            public void Gives_up_after_the_fourth()
            {
                var verdict = OfferRules.AfterStall(4, 524288, 9437184);

                verdict.ShouldBe(Verdict.Refuse(
                    Refusal.HostStoppedSending,
                    Refusals.Detail("512", "9216")));
            }
        }
    }
}
