using DarkwoodMP.World.Traders;

namespace DarkwoodMP.Tests.World.Traders
{
    public class TraderCopyRulesTests
    {
        /// A player base whose trader spot is at the origin.
        static readonly HideoutBase Hideout = new HideoutBase
        {
            PlayerBase = true,
            HasTraderSpot = true,
        };

        public class Limits
        {
            [Fact]
            public void Pins_the_copy_limits()
            {
                var limits = new[]
                {
                    TraderCopyRules.CopyRange,
                    TraderCopyRules.CopyGrace,
                    TraderCopyRules.CheckEvery,
                };

                limits.ShouldBe(new[] { 300f, 10f, 1f });
            }

            [Fact]
            public void Pins_the_name_left_out()
            {
                TraderCopyRules.Wolf.ShouldBe("wolf");
            }
        }

        public class MayCopy
        {
            [Fact]
            public void Copies_a_living_trader()
            {
                TraderCopyRules.MayCopy("Trader", false).ShouldBeTrue();
            }

            [Theory]
            [InlineData("wolf")]
            [InlineData("The_Wolf")]
            [InlineData("WOLFMAN")]
            public void Never_copies_the_wolf(string name)
            {
                TraderCopyRules.MayCopy(name, false).ShouldBeFalse();
            }

            [Fact]
            public void Never_copies_a_trader_dead_in_the_hosts_story()
            {
                TraderCopyRules.MayCopy("Trader", true).ShouldBeFalse();
            }
        }

        public class NearestBase
        {
            [Fact]
            public void Takes_a_hideout_just_inside_the_range()
            {
                var bases = new[] { Hideout };

                var index = TraderCopyRules.NearestBase(
                    bases,
                    new Vec2(299.9f, 0f));

                index.ShouldBe(0);
            }

            [Fact]
            public void Leaves_a_hideout_exactly_at_the_range()
            {
                var bases = new[] { Hideout };

                var index = TraderCopyRules.NearestBase(
                    bases,
                    new Vec2(0f, 300f));

                index.ShouldBe(-1);
            }

            [Fact]
            public void Takes_the_nearest_hideout()
            {
                var far = Hideout with { TraderSpot = new Vec2(0f, 200f) };
                var bases = new[] { far, Hideout };

                var index = TraderCopyRules.NearestBase(
                    bases,
                    new Vec2(50f, 0f));

                index.ShouldBe(1);
            }

            [Fact]
            public void Takes_the_first_of_two_at_the_same_distance()
            {
                var bases = new[] { Hideout, Hideout };

                var index = TraderCopyRules.NearestBase(bases, new Vec2());

                index.ShouldBe(0);
            }

            [Fact]
            public void Leaves_out_a_place_that_isnt_a_player_base()
            {
                var bases = new[] { Hideout with { PlayerBase = false } };

                var index = TraderCopyRules.NearestBase(bases, new Vec2());

                index.ShouldBe(-1);
            }

            [Fact]
            public void Leaves_out_a_hideout_with_no_trader_spot()
            {
                var bases = new[] { Hideout with { HasTraderSpot = false } };

                var index = TraderCopyRules.NearestBase(bases, new Vec2());

                index.ShouldBe(-1);
            }

            [Fact]
            public void Finds_none_with_no_list()
            {
                var index = TraderCopyRules.NearestBase(null, new Vec2());

                index.ShouldBe(-1);
            }
        }

        public class AfterSpawn
        {
            [Fact]
            public void Keeps_the_trader_asked_about()
            {
                var result = TraderCopyRules.AfterSpawn(
                    "Trader",
                    "Trader",
                    true);

                result.ShouldBe(SpawnResult.Kept);
            }

            [Fact]
            public void Keeps_nothing_when_no_trader_came()
            {
                var result = TraderCopyRules.AfterSpawn(
                    "Trader",
                    null,
                    false);

                result.ShouldBe(SpawnResult.NoTrader);
            }

            [Fact]
            public void Takes_away_another_trader_spawned_just_now()
            {
                var result = TraderCopyRules.AfterSpawn(
                    "Trader",
                    "Doctor",
                    false);

                result.ShouldBe(SpawnResult.OtherGoes);
            }

            [Fact]
            public void Leaves_another_trader_who_was_there()
            {
                var result = TraderCopyRules.AfterSpawn(
                    "Trader",
                    "Doctor",
                    true);

                result.ShouldBe(SpawnResult.OtherStays);
            }
        }

        public class Due
        {
            [Fact]
            public void Looks_exactly_when_the_look_is_due()
            {
                var due = TraderCopyRules.Due(
                    1,
                    new Seconds(5f),
                    new Seconds(5f));

                due.ShouldBeTrue();
            }

            [Fact]
            public void Waits_just_before_the_look_is_due()
            {
                var due = TraderCopyRules.Due(
                    1,
                    new Seconds(4.9f),
                    new Seconds(5f));

                due.ShouldBeFalse();
            }

            [Fact]
            public void Never_looks_with_no_copies()
            {
                var due = TraderCopyRules.Due(
                    0,
                    new Seconds(9f),
                    new Seconds(5f));

                due.ShouldBeFalse();
            }
        }

        public class Next
        {
            [Fact]
            public void Looks_again_a_second_later()
            {
                var next = TraderCopyRules.Next(new Seconds(5f));

                next.Value.ShouldBe(6f);
            }
        }

        public class SomeoneInMorning
        {
            static readonly RemoteMorning Joiner = new RemoteMorning
            {
                HasState = true,
                AfterNight = true,
            };

            [Fact]
            public void Counts_a_joiner_in_a_morning()
            {
                var remotes = new[]
                {
                    Joiner with { AfterNight = false },
                    Joiner,
                };

                TraderCopyRules.SomeoneInMorning(remotes).ShouldBeTrue();
            }

            [Fact]
            public void Leaves_out_the_test_dummy()
            {
                var remotes = new[] { Joiner with { Dummy = true } };

                TraderCopyRules.SomeoneInMorning(remotes).ShouldBeFalse();
            }

            [Fact]
            public void Leaves_out_a_joiner_with_no_state_yet()
            {
                var remotes = new[] { Joiner with { HasState = false } };

                TraderCopyRules.SomeoneInMorning(remotes).ShouldBeFalse();
            }

            [Fact]
            public void Leaves_out_a_joiner_after_their_morning()
            {
                var remotes = new[] { Joiner with { AfterNight = false } };

                TraderCopyRules.SomeoneInMorning(remotes).ShouldBeFalse();
            }

            [Fact]
            public void Counts_no_one_with_no_list()
            {
                TraderCopyRules.SomeoneInMorning(null).ShouldBeFalse();
            }
        }

        public class Fate
        {
            /// A copy put there 10 s ago, with no joiner in a morning.
            static readonly TraderCopyFacts Lapsed = new TraderCopyFacts
            {
                Now = new Seconds(30f),
                Since = new Seconds(20f),
            };

            [Fact]
            public void Takes_away_a_copy_after_the_grace()
            {
                TraderCopyRules.Fate(Lapsed).ShouldBe(CopyFate.TakeAway);
            }

            [Fact]
            public void Keeps_a_copy_just_inside_the_grace()
            {
                var copy = Lapsed with { Since = new Seconds(20.5f) };

                TraderCopyRules.Fate(copy).ShouldBe(CopyFate.Keep);
            }

            [Fact]
            public void Keeps_a_copy_while_a_joiner_is_in_a_morning()
            {
                var copy = Lapsed with { Someone = true };

                TraderCopyRules.Fate(copy).ShouldBe(CopyFate.Keep);
            }

            [Fact]
            public void Forgets_a_copy_that_is_gone()
            {
                var copy = Lapsed with { Gone = true, Someone = true };

                TraderCopyRules.Fate(copy).ShouldBe(CopyFate.Forget);
            }

            [Fact]
            public void Forgets_a_dead_copy()
            {
                var copy = Lapsed with { Dead = true, Someone = true };

                TraderCopyRules.Fate(copy).ShouldBe(CopyFate.Forget);
            }

            [Fact]
            public void Leaves_a_copy_in_the_hosts_own_morning_there()
            {
                var copy = Lapsed with { OursNow = true };

                TraderCopyRules.Fate(copy).ShouldBe(CopyFate.Forget);
            }

            [Fact]
            public void Puts_the_grace_before_the_hosts_own_morning()
            {
                var copy = Lapsed with { OursNow = true, Someone = true };

                TraderCopyRules.Fate(copy).ShouldBe(CopyFate.Keep);
            }
        }
    }
}
