using System.Globalization;
using System.Threading;
using DarkwoodMP.World;
using DarkwoodMP.World.Fuel;

namespace DarkwoodMP.Tests.World.Fuel
{
    public class FuelRulesTests
    {
        /// A client pours 20 into a generator at 50 of 100, exactly at the
        /// reach, with cans of 100.
        static readonly FuelFacts Pour = new FuelFacts
        {
            Found = true,
            Tank = FuelTank.Generator,
            Ref = new ObjName(ObjKind.Saved, 1234),
            Value = 20f,
            Have = 50f,
            Max = 100f,
            CanHolds = 100f,
            Reach = new ReachFacts
            {
                Known = true,
                StandIn = new Vec2(0f, 0f),
                Target = new Vec2(600f, 0f),
            },
        };

        /// A client cuts two logs at a saw with 30, at 10 a log.
        static readonly FuelFacts Cut = Pour with
        {
            Tank = FuelTank.Saw,
            Value = -20f,
            Have = 30f,
            FuelToConvert = 10,
        };

        /// Cuts at a saw of 100 a log, with plenty in it.
        static readonly FuelFacts Logs = Cut with
        {
            Have = 500f,
            Max = 500f,
            FuelToConvert = 100,
        };

        public class Limits
        {
            [Fact]
            public void Pins_the_limits()
            {
                var limits = new[]
                {
                    FuelRules.Reach,
                    FuelRules.FallbackCan,
                    FuelRules.CanMargin,
                    FuelRules.FullMargin,
                    FuelRules.FewestLogs,
                    FuelRules.LogTolerance,
                    FuelRules.SawSlack,
                    FuelRules.LevelChange,
                    FuelRules.CheckInterval,
                };

                limits.ShouldBe(new[]
                {
                    600f, 100f, 0.01f, 0.01f, 0.99f, 0.02f, 0.5f, 0.5f, 5f,
                });
            }

            [Fact]
            public void Pins_the_tanks_on_the_wire()
            {
                var tanks = new[]
                {
                    (byte)FuelTank.Generator,
                    (byte)FuelTank.Saw,
                };

                tanks.ShouldBe(new byte[] { 1, 2 });
            }
        }

        public class TankOf
        {
            [Theory]
            [InlineData(1, FuelTank.Generator)]
            [InlineData(2, FuelTank.Saw)]
            [InlineData(0, FuelTank.Saw)]
            [InlineData(7, FuelTank.Saw)]
            public void Reads_any_byte_but_1_as_a_saw(byte what, FuelTank tank)
            {
                var read = FuelRules.TankOf(what);

                read.ShouldBe(tank);
            }
        }

        public class CanHolds
        {
            [Theory]
            [InlineData(80f, 80f)]
            [InlineData(0f, 100f)]
            [InlineData(-5f, 100f)]
            [InlineData(float.NaN, 100f)]
            public void Takes_the_items_size_or_100(float item, float holds)
            {
                var can = FuelRules.CanHolds(item);

                can.ShouldBe(holds);
            }
        }

        public class MayAdd
        {
            [Fact]
            public void Takes_a_pour_that_fits()
            {
                var answer = FuelRules.MayAdd(Pour);

                answer.ShouldBe(new FuelAnswer { Took = 20f, Level = 70f });
            }

            [Fact]
            public void Takes_only_what_fits_of_a_pour()
            {
                var pour = Pour with { Have = 90f };

                var answer = FuelRules.MayAdd(pour);

                answer.ShouldBe(new FuelAnswer { Took = 10f, Level = 100f });
            }

            [Fact]
            public void Takes_a_cut_whole()
            {
                var answer = FuelRules.MayAdd(Cut);

                answer.ShouldBe(new FuelAnswer { Took = -20f, Level = 10f });
            }

            [Fact]
            public void Keeps_the_level_at_0_after_a_cut()
            {
                var cut = Cut with { Have = 19.6f };

                var answer = FuelRules.MayAdd(cut);

                answer.Level.ShouldBe(0f);
            }

            [Fact]
            public void Refuses_a_generator_that_isnt_here()
            {
                var pour = Pour with { Found = false };

                var answer = FuelRules.MayAdd(pour);

                answer.Verdict.ShouldBe(
                    Verdict.Refuse(Refusal.NoSuchGenerator, "Saved:1234"));
            }

            [Fact]
            public void Refuses_a_saw_that_isnt_here()
            {
                var cut = Cut with { Found = false };

                var answer = FuelRules.MayAdd(cut);

                answer.Verdict.ShouldBe(
                    Verdict.Refuse(Refusal.NoSuchSaw, "Saved:1234"));
            }

            [Fact]
            public void Sends_no_level_back_for_a_tank_that_isnt_here()
            {
                var pour = Pour with { Found = false };

                var answer = FuelRules.MayAdd(pour);

                answer.SendsBack.ShouldBeFalse();
            }

            [Fact]
            public void Refuses_a_pour_just_past_the_reach()
            {
                var pour = Pour with
                {
                    Reach = Pour.Reach with { Target = new Vec2(600.5f, 0f) },
                };

                var answer = FuelRules.MayAdd(pour);

                answer.ShouldBe(new FuelAnswer
                {
                    Verdict = Verdict.Refuse(Refusal.TooFar),
                    SendsBack = true,
                });
            }

            [Fact]
            public void Takes_a_pour_from_a_player_with_no_stand_in()
            {
                var pour = Pour with
                {
                    Reach = new ReachFacts { Target = new Vec2(4041f, 0f) },
                };

                var answer = FuelRules.MayAdd(pour);

                answer.Verdict.ShouldBe(Verdict.Allow);
            }

            [Theory]
            [InlineData(float.NaN)]
            [InlineData(float.PositiveInfinity)]
            [InlineData(float.NegativeInfinity)]
            public void Refuses_an_amount_that_isnt_a_number(float value)
            {
                var answer = FuelRules.MayAdd(Pour with { Value = value });

                answer.Verdict.Reason.ShouldBe(Refusal.NotANumber);
            }

            [Fact]
            public void Takes_a_pour_exactly_a_can_and_its_margin()
            {
                var pour = Pour with { Value = 100.01f, Have = 0f, Max = 200f };

                var answer = FuelRules.MayAdd(pour);

                answer.Verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Refuses_a_pour_just_past_a_can()
            {
                var pour = Pour with { Value = 100.02f, Have = 0f, Max = 200f };

                var answer = FuelRules.MayAdd(pour);

                answer.Verdict.Reason.ShouldBe(Refusal.MoreThanACan);
            }

            [Fact]
            public void Refuses_a_pour_into_a_tank_full_within_its_margin()
            {
                var pour = Pour with { Have = 99.99f };

                var answer = FuelRules.MayAdd(pour);

                answer.Verdict.Reason.ShouldBe(Refusal.TankFull);
            }

            [Fact]
            public void Takes_a_pour_into_a_tank_just_short_of_full()
            {
                var pour = Pour with { Have = 99.98f };

                var answer = FuelRules.MayAdd(pour);

                answer.Verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Refuses_a_generators_fuel_going_down()
            {
                var cut = Cut with { Tank = FuelTank.Generator };

                var answer = FuelRules.MayAdd(cut);

                answer.Verdict.Reason.ShouldBe(Refusal.GeneratorFuelTaken);
            }

            [Fact]
            public void Takes_an_amount_of_0_as_a_cut()
            {
                var pour = Pour with { Value = 0f };

                var answer = FuelRules.MayAdd(pour);

                answer.Verdict.Reason.ShouldBe(Refusal.GeneratorFuelTaken);
            }

            [Theory]
            [InlineData(-99f)]
            [InlineData(-102f)]
            [InlineData(-198f)]
            [InlineData(-202f)]
            public void Takes_a_cut_within_the_log_tolerance(float value)
            {
                var cut = Logs with { Value = value };

                var answer = FuelRules.MayAdd(cut);

                answer.Verdict.ShouldBe(Verdict.Allow);
            }

            [Theory]
            [InlineData(-98f)]
            [InlineData(-103f)]
            [InlineData(-150f)]
            [InlineData(0f)]
            public void Refuses_a_cut_that_isnt_whole_logs(float value)
            {
                var cut = Logs with { Value = value };

                var answer = FuelRules.MayAdd(cut);

                answer.Verdict.Reason.ShouldBe(Refusal.NotWholeLogs);
            }

            [Fact]
            public void Counts_a_log_as_at_least_1_fuel()
            {
                var cut = Cut with { Value = -2f, FuelToConvert = 0 };

                var answer = FuelRules.MayAdd(cut);

                answer.Verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Takes_a_cut_of_what_the_saw_has_and_its_slack()
            {
                var cut = Cut with { Have = 19.5f };

                var answer = FuelRules.MayAdd(cut);

                answer.Verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Refuses_a_cut_just_past_what_the_saw_has()
            {
                var cut = Cut with { Have = 19.4f };

                var answer = FuelRules.MayAdd(cut);

                answer.Verdict.Reason.ShouldBe(Refusal.NotEnoughFuel);
            }

            [Fact]
            public void Refuses_a_missing_tank_before_its_reach()
            {
                var pour = Pour with
                {
                    Found = false,
                    Reach = Pour.Reach with { Target = new Vec2(4041f, 0f) },
                };

                var answer = FuelRules.MayAdd(pour);

                answer.Verdict.Reason.ShouldBe(Refusal.NoSuchGenerator);
            }

            [Fact]
            public void Refuses_the_reach_before_the_number()
            {
                var pour = Pour with
                {
                    Value = float.NaN,
                    Reach = Pour.Reach with { Target = new Vec2(4041f, 0f) },
                };

                var answer = FuelRules.MayAdd(pour);

                answer.Verdict.Reason.ShouldBe(Refusal.TooFar);
            }

            [Fact]
            public void Refuses_more_than_a_can_before_a_full_tank()
            {
                var pour = Pour with { Value = 150f, Have = 100f };

                var answer = FuelRules.MayAdd(pour);

                answer.Verdict.Reason.ShouldBe(Refusal.MoreThanACan);
            }

            [Fact]
            public void Refuses_a_generator_before_counting_logs()
            {
                var cut = Cut with { Tank = FuelTank.Generator, Value = -15f };

                var answer = FuelRules.MayAdd(cut);

                answer.Verdict.Reason.ShouldBe(Refusal.GeneratorFuelTaken);
            }

            [Fact]
            public void Refuses_a_part_log_before_what_the_saw_has()
            {
                var cut = Cut with { Value = -15f, Have = 0f };

                var answer = FuelRules.MayAdd(cut);

                answer.Verdict.Reason.ShouldBe(Refusal.NotWholeLogs);
            }

            [Fact]
            public void Sends_the_level_back_with_a_full_tank()
            {
                var answer = FuelRules.MayAdd(Pour with { Have = 100f });

                answer.SendsBack.ShouldBeTrue();
            }

            [Fact]
            public void Sends_the_level_back_with_a_cut_past_the_saw()
            {
                var answer = FuelRules.MayAdd(Cut with { Have = 0f });

                answer.SendsBack.ShouldBeTrue();
            }
        }

        /// The refusals' numbers, in the host's own culture as today: set
        /// here to the invariant one, so the words are the same anywhere.
        public class Words
        {
            static string Text(FuelFacts fuel)
            {
                var culture = Thread.CurrentThread.CurrentCulture;
                Thread.CurrentThread.CurrentCulture =
                    CultureInfo.InvariantCulture;
                try
                {
                    return Refusals.Text(FuelRules.MayAdd(fuel).Verdict);
                }
                finally
                {
                    Thread.CurrentThread.CurrentCulture = culture;
                }
            }

            [Fact]
            public void Words_a_pour_more_than_a_can()
            {
                var text = Text(Pour with { Value = 150.26f });

                text.ShouldBe("150.3 is more than a can holds (100)");
            }

            [Fact]
            public void Words_a_full_tank()
            {
                var text = Text(Pour with { Have = 99.99f });

                text.ShouldBe("the tank is full (100.0 of 100)");
            }

            [Fact]
            public void Words_a_cut_that_isnt_whole_logs()
            {
                var text = Text(Cut with { Value = -15f });

                text.ShouldBe("15.0 isn't a whole number of logs (10 each)");
            }

            [Fact]
            public void Words_a_cut_past_what_the_saw_has()
            {
                var text = Text(Cut with { Have = 12.34f });

                text.ShouldBe("the saw has only 12.3");
            }

            [Fact]
            public void Words_a_saw_that_isnt_here()
            {
                var text = Text(Cut with { Found = false });

                text.ShouldBe("no saw Saved:1234 here");
            }
        }

        public class ResultOf
        {
            [Theory]
            [InlineData(20f, "20")]
            [InlineData(10.5f, "10.5")]
            [InlineData(0.12345f, "0.123")]
            [InlineData(-20f, "-20")]
            public void Writes_the_amount_taken_in_the_invariant_culture(
                float took,
                string result)
            {
                var text = FuelRules.ResultOf(took);

                text.ShouldBe(result);
            }
        }

        public class Changed
        {
            /// A generator sent at 50, running, and so now.
            static readonly SentFacts Same = new SentFacts
            {
                Sent = true,
                SentFuel = 50f,
                SentOn = true,
                Fuel = 50f,
                On = true,
            };

            [Fact]
            public void Leaves_a_generator_as_it_was_sent()
            {
                var changed = FuelRules.Changed(Same);

                changed.ShouldBeFalse();
            }

            [Fact]
            public void Sends_a_generator_never_sent()
            {
                var changed = FuelRules.Changed(Same with { Sent = false });

                changed.ShouldBeTrue();
            }

            [Fact]
            public void Leaves_a_level_moved_just_less_than_the_change()
            {
                var changed = FuelRules.Changed(Same with { Fuel = 49.6f });

                changed.ShouldBeFalse();
            }

            [Fact]
            public void Sends_a_level_moved_exactly_the_change()
            {
                var changed = FuelRules.Changed(Same with { Fuel = 49.5f });

                changed.ShouldBeTrue();
            }

            [Fact]
            public void Sends_a_generator_switched_off()
            {
                var changed = FuelRules.Changed(Same with { On = false });

                changed.ShouldBeTrue();
            }
        }

        public class CheckDue
        {
            [Theory]
            [InlineData(true, 10f, 10f, true)]
            [InlineData(true, 9.9f, 10f, false)]
            [InlineData(false, 20f, 10f, false)]
            public void Checks_on_the_host_once_the_time_comes(
                bool host,
                float now,
                float next,
                bool due)
            {
                var check = FuelRules.CheckDue(
                    host,
                    new Seconds(now),
                    new Seconds(next));

                check.ShouldBe(due);
            }
        }

        public class NextCheck
        {
            [Fact]
            public void Checks_again_5_seconds_later()
            {
                var next = FuelRules.NextCheck(new Seconds(10f));

                next.Value.ShouldBe(15f);
            }
        }
    }
}
