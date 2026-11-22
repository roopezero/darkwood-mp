using DarkwoodMP.World;

namespace DarkwoodMP.Tests.World
{
    public class HostIdRulesTests
    {
        static Seconds T(float s) => new Seconds(s);

        static Candidate At(float squared, bool same) =>
            new Candidate { Squared = squared, SamePlace = same };

        public class Limits
        {
            [Fact]
            public void Pins_the_host_id_limits()
            {
                var limits = new[]
                {
                    HostIdRules.Window,
                    HostIdRules.Settle,
                    HostIdRules.PruneEvery,
                    HostIdRules.ReplayNaming,
                    HostIdRules.SameDistance,
                };

                limits.ShouldBe(new[] { 30f, 1f, 10f, 0.5f, 1f });
            }

            [Fact]
            public void Pins_the_made_before_flag()
            {
                HostIdRules.MadeBefore.ShouldBe((byte)1);
            }
        }

        public class NewId
        {
            [Theory]
            [InlineData(0, 0, 0L)]
            [InlineData(1, 0, 1L << 31)]
            [InlineData(0, int.MaxValue, 0x7FFFFFFFL)]
            [InlineData(int.MaxValue, int.MaxValue, 0x3FFFFFFFFFFFFFFFL)]
            public void Joins_two_draws_into_62_bits(int high, int low, long id)
            {
                var made = HostIdRules.NewId(high, low);

                made.ShouldBe(id);
            }
        }

        public class Settled
        {
            [Theory]
            [InlineData(0.99f, false)]
            [InlineData(1f, true)]
            public void Holds_from_a_second_on(float age, bool settled)
            {
                var result = HostIdRules.Settled(T(age));

                result.ShouldBe(settled);
            }
        }

        public class NeedsId
        {
            [Theory]
            [InlineData(true, true, true)]
            [InlineData(false, true, false)]
            [InlineData(true, false, false)]
            public void Holds_only_for_a_live_object_nothing_names(
                bool alive,
                bool unnamed,
                bool needs)
            {
                var result = HostIdRules.NeedsId(alive, unnamed);

                result.ShouldBe(needs);
            }
        }

        public class Forgotten
        {
            [Theory]
            [InlineData(30f, false)]
            [InlineData(30.5f, true)]
            public void Holds_past_the_window(float age, bool forgotten)
            {
                var result = HostIdRules.Forgotten(T(age));

                result.ShouldBe(forgotten);
            }
        }

        public class Expired
        {
            [Theory]
            [InlineData(30f, false)]
            [InlineData(30.5f, true)]
            public void Holds_past_the_deadline(float now, bool expired)
            {
                var result = HostIdRules.Expired(T(now), T(30f));

                result.ShouldBe(expired);
            }
        }

        public class GoesLate
        {
            [Theory]
            [InlineData((byte)0, false)]
            [InlineData((byte)1, true)]
            [InlineData((byte)2, false)]
            [InlineData((byte)3, true)]
            public void Holds_for_ids_flagged_made_before(byte flags, bool late)
            {
                var result = HostIdRules.GoesLate(flags);

                result.ShouldBe(late);
            }
        }

        public class SquaredDistance
        {
            [Fact]
            public void Squares_the_distance_on_the_ground()
            {
                var d = HostIdRules.SquaredDistance(
                    new Vec2(4f, 6f),
                    new Vec2(1f, 2f));

                d.ShouldBe(25f, 0.001f);
            }
        }

        public class InSlack
        {
            [Theory]
            [InlineData(2500f, true)]
            [InlineData(2500.5f, false)]
            public void Holds_up_to_the_slack_squared(
                float squared,
                bool within)
            {
                var result = HostIdRules.InSlack(squared);

                result.ShouldBe(within);
            }
        }

        public class MayBeCopy
        {
            /// A live object of the announced name, started a second ago,
            /// that nothing names yet.
            static readonly CopyFacts Fresh = new CopyFacts
            {
                Alive = true,
                Age = new Seconds(1f),
                SameName = true,
                Unnamed = true,
            };

            [Fact]
            public void Takes_a_fresh_unnamed_object_of_the_name()
            {
                var copy = HostIdRules.MayBeCopy(Fresh);

                copy.ShouldBeTrue();
            }

            [Theory]
            [InlineData(0.49f, false)]
            [InlineData(0.5f, true)]
            [InlineData(30f, true)]
            [InlineData(30.5f, false)]
            public void Takes_an_object_from_half_a_second_to_the_window(
                float age,
                bool expected)
            {
                var copy = HostIdRules.MayBeCopy(Fresh with { Age = T(age) });

                copy.ShouldBe(expected);
            }

            [Fact]
            public void Leaves_an_object_that_is_gone()
            {
                var copy = HostIdRules.MayBeCopy(Fresh with { Alive = false });

                copy.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_an_object_of_another_name()
            {
                var facts = Fresh with { SameName = false };

                var copy = HostIdRules.MayBeCopy(facts);

                copy.ShouldBeFalse();
            }

            [Theory]
            [InlineData(false, false)]
            [InlineData(true, true)]
            public void Takes_a_named_object_only_if_tied_late(
                bool lateTied,
                bool expected)
            {
                var facts = Fresh with { Unnamed = false, LateTied = lateTied };

                var copy = HostIdRules.MayBeCopy(facts);

                copy.ShouldBe(expected);
            }
        }

        public class IsLateCandidate
        {
            [Theory]
            [InlineData(2500f, true)]
            [InlineData(2500.5f, false)]
            public void Takes_a_free_id_up_to_the_slack_squared(
                float squared,
                bool candidate)
            {
                bool tied = false;

                var result = HostIdRules.IsLateCandidate(tied, squared);

                result.ShouldBe(candidate);
            }

            [Fact]
            public void Leaves_an_id_tied_to_a_live_object()
            {
                bool tied = true;

                var result = HostIdRules.IsLateCandidate(tied, 0f);

                result.ShouldBeFalse();
            }
        }

        public class Better
        {
            [Theory]
            [InlineData(10.9f, false, true)]
            [InlineData(11f, false, false)]
            [InlineData(11f, true, true)]
            [InlineData(13f, true, true)]
            [InlineData(13.5f, true, false)]
            public void Takes_the_nearer_or_the_same_place_within_1(
                float squared,
                bool same,
                bool better)
            {
                var best = At(12f, false);

                var result = HostIdRules.Better(At(squared, same), best);

                result.ShouldBe(better);
            }

            [Fact]
            public void Keeps_a_best_in_the_same_place_within_1()
            {
                var best = At(12f, true);

                var result = HostIdRules.Better(At(11.5f, true), best);

                result.ShouldBeFalse();
            }
        }

        public class Pick
        {
            [Fact]
            public void Picks_nothing_from_no_candidates()
            {
                var best = HostIdRules.Pick(new Candidate[0]);

                best.ShouldBe(-1);
            }

            [Fact]
            public void Takes_the_first_before_comparing()
            {
                var best = HostIdRules.Pick(new[] { At(9000f, false) });

                best.ShouldBe(0);
            }

            [Theory]
            [InlineData(9.5f, false, 0)]
            [InlineData(8f, false, 1)]
            [InlineData(10.5f, true, 1)]
            public void Takes_a_later_one_only_if_better(
                float squared,
                bool same,
                int expected)
            {
                var candidates = new[] { At(10f, false), At(squared, same) };

                var best = HostIdRules.Pick(candidates);

                best.ShouldBe(expected);
            }

            [Fact]
            public void Compares_each_with_the_best_so_far()
            {
                var candidates = new[]
                {
                    At(10f, false),
                    At(8f, false),
                    At(7.5f, false),
                };

                var best = HostIdRules.Pick(candidates);

                best.ShouldBe(1);
            }
        }

        public class PlacePath
        {
            [Fact]
            public void Names_each_level_up_to_the_root_without_its_index()
            {
                var levels = new[]
                {
                    new PlaceLevel { Name = "Lamp", Index = 3 },
                    new PlaceLevel { Name = "House", Index = 1 },
                    new PlaceLevel { Name = "Root", Index = 7 },
                };

                var path = HostIdRules.PlacePath(levels);

                path.ShouldBe("Lamp:3/House:1/Root/");
            }

            [Fact]
            public void Names_a_root_alone()
            {
                var levels = new[] { new PlaceLevel { Name = "Root" } };

                var path = HostIdRules.PlacePath(levels);

                path.ShouldBe("Root/");
            }
        }
    }
}
