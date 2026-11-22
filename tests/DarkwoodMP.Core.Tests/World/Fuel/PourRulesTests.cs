using DarkwoodMP.World.Fuel;

namespace DarkwoodMP.Tests.World.Fuel
{
    public class PourRulesTests
    {
        public class Limits
        {
            [Fact]
            public void Pins_the_limits()
            {
                var limits = new[]
                {
                    PourRules.Smallest,
                    PourRules.FlushInterval,
                    PourRules.TookTolerance,
                    PourRules.LeastBack,
                };

                limits.ShouldBe(new[] { 0.0001f, 0.25f, 0.001f, 0.001f });
            }

            [Fact]
            public void Pins_the_cans_item_type()
            {
                PourRules.Gasoline.ShouldBe("gasoline");
            }
        }

        public class Records
        {
            [Theory]
            [InlineData(true, false, 0.0001f, true)]
            [InlineData(true, false, -0.0001f, true)]
            [InlineData(true, false, 0.00009f, false)]
            [InlineData(false, false, 5f, false)]
            [InlineData(true, true, 5f, false)]
            public void Records_a_pour_while_sharing_and_not_replaying(
                bool sharing,
                bool replaying,
                float delta,
                bool recorded)
            {
                var records = PourRules.Records(sharing, replaying, delta);

                records.ShouldBe(recorded);
            }
        }

        public class IsCut
        {
            [Theory]
            [InlineData(-1f, true)]
            [InlineData(1f, false)]
            public void Takes_fuel_going_down_as_a_cut(float delta, bool cut)
            {
                var isCut = PourRules.IsCut(delta);

                isCut.ShouldBe(cut);
            }
        }

        public class FromCan
        {
            [Theory]
            [InlineData(5f, true, "gasoline", true)]
            [InlineData(5f, true, "molotov", false)]
            [InlineData(5f, false, "gasoline", false)]
            [InlineData(-5f, true, "gasoline", false)]
            public void Counts_a_pour_from_a_gasoline_can_held(
                float delta,
                bool holds,
                string type,
                bool fromCan)
            {
                var drained = PourRules.FromCan(delta, holds, type);

                drained.ShouldBe(fromCan);
            }
        }

        public class FlushDue
        {
            [Theory]
            [InlineData(1, 10f, 10f, true)]
            [InlineData(1, 9.9f, 10f, false)]
            [InlineData(0, 20f, 10f, false)]
            public void Flushes_pending_pours_once_the_time_comes(
                int pending,
                float now,
                float next,
                bool due)
            {
                var flush = PourRules.FlushDue(
                    pending,
                    new Seconds(now),
                    new Seconds(next));

                flush.ShouldBe(due);
            }
        }

        public class NextFlush
        {
            [Fact]
            public void Flushes_again_a_quarter_second_later()
            {
                var next = PourRules.NextFlush(new Seconds(10f));

                next.Value.ShouldBe(10.25f);
            }
        }

        public class FlushesBeforeOn
        {
            [Theory]
            [InlineData(true, false, true)]
            [InlineData(false, false, false)]
            [InlineData(true, true, false)]
            public void Flushes_before_a_switch_on_while_sharing(
                bool sharing,
                bool replaying,
                bool flushes)
            {
                var flush = PourRules.FlushesBeforeOn(sharing, replaying);

                flush.ShouldBe(flushes);
            }
        }

        public class Back
        {
            /// Two pours of 10 from a can that the host took whole.
            static readonly RefundFacts Taken = new RefundFacts
            {
                Allowed = true,
                Amount = 20f,
                Drained = 2f,
                Result = "20",
            };

            [Fact]
            public void Gives_back_every_pour_when_refused()
            {
                var back = PourRules.Back(Taken with { Allowed = false });

                back.ShouldBe(2f);
            }

            [Fact]
            public void Gives_back_nothing_when_the_host_took_all()
            {
                var back = PourRules.Back(Taken);

                back.ShouldBe(0f);
            }

            [Fact]
            public void Gives_back_the_part_the_host_didnt_take()
            {
                var back = PourRules.Back(Taken with { Result = "5" });

                back.ShouldBe(1.5f);
            }

            [Fact]
            public void Gives_back_nothing_within_the_tolerance()
            {
                var back = PourRules.Back(Taken with { Result = "19.9995" });

                back.ShouldBe(0f);
            }

            [Fact]
            public void Gives_back_all_when_the_host_took_less_than_none()
            {
                var back = PourRules.Back(Taken with { Result = "-3" });

                back.ShouldBe(2f);
            }

            [Fact]
            public void Reads_the_result_in_the_invariant_culture()
            {
                var back = PourRules.Back(Taken with { Result = "10.5" });

                back.ShouldBe(0.95f, 0.0001f);
            }

            [Theory]
            [InlineData("")]
            [InlineData(null)]
            [InlineData("lots")]
            public void Gives_back_nothing_for_a_result_it_cant_read(
                string result)
            {
                var back = PourRules.Back(Taken with { Result = result });

                back.ShouldBe(0f);
            }

            [Fact]
            public void Gives_back_nothing_for_an_allowed_cut()
            {
                var cut = Taken with { Amount = -10f, Result = "-5" };

                var back = PourRules.Back(cut);

                back.ShouldBe(0f);
            }
        }

        public class Refunds
        {
            [Theory]
            [InlineData(0.002f, true)]
            [InlineData(0.001f, false)]
            [InlineData(0f, false)]
            public void Gives_back_only_more_than_the_least(
                float back,
                bool refunds)
            {
                var refund = PourRules.Refunds(back);

                refund.ShouldBe(refunds);
            }
        }

        public class RefundOf
        {
            [Theory]
            [InlineData(true, true, 3f, RefundKind.Refill)]
            [InlineData(true, true, 0f, RefundKind.NewCan)]
            [InlineData(true, false, 3f, RefundKind.Nothing)]
            [InlineData(false, true, 0f, RefundKind.Nothing)]
            public void Refills_the_can_or_gives_a_new_one(
                bool player,
                bool can,
                float amount,
                RefundKind kind)
            {
                var refund = PourRules.RefundOf(player, can, amount);

                refund.ShouldBe(kind);
            }
        }

        public class Refilled
        {
            [Theory]
            [InlineData(50f, 10f, 100f, 60f)]
            [InlineData(95f, 10f, 100f, 100f)]
            public void Refills_up_to_the_cans_most(
                float durability,
                float back,
                float max,
                float refilled)
            {
                var after = PourRules.Refilled(durability, back, max);

                after.ShouldBe(refilled);
            }
        }

        public class Shown
        {
            [Theory]
            [InlineData(50f, 10f, 100f, 60f)]
            [InlineData(95f, 10f, 100f, 100f)]
            [InlineData(5f, -10f, 100f, 0f)]
            public void Shows_the_hosts_level_with_what_is_pending(
                float level,
                float pending,
                float max,
                float shown)
            {
                var fuel = PourRules.Shown(level, pending, max);

                fuel.ShouldBe(shown);
            }
        }

        public class Switch
        {
            [Theory]
            [InlineData(true, false, true, GeneratorSwitch.TurnOnItem)]
            [InlineData(true, false, false, GeneratorSwitch.TurnOnGenerator)]
            [InlineData(false, true, true, GeneratorSwitch.PowerDown)]
            [InlineData(true, true, true, GeneratorSwitch.Leave)]
            [InlineData(false, false, true, GeneratorSwitch.Leave)]
            public void Switches_the_generator_as_the_host_has_it(
                bool on,
                bool running,
                bool item,
                GeneratorSwitch change)
            {
                var done = PourRules.Switch(on, running, item);

                done.ShouldBe(change);
            }
        }
    }
}
