using DarkwoodMP.World.Traps;

namespace DarkwoodMP.Tests.World.Traps
{
    public class DisarmRulesTests
    {
        public class Answers
        {
            [Fact]
            public void Pins_the_answers_of_the_roll()
            {
                var answers = new[] { DisarmRules.Worked, DisarmRules.Failed };

                answers.ShouldBe(new[] { "1", "0" });
            }
        }

        public class Works
        {
            [Fact]
            public void Works_for_a_roll_under_the_chance()
            {
                DisarmRules.Works(true, 0.5f, 0.49f).ShouldBeTrue();
            }

            [Fact]
            public void Fails_for_a_roll_exactly_at_the_chance()
            {
                DisarmRules.Works(true, 0.5f, 0.5f).ShouldBeFalse();
            }

            [Fact]
            public void Fails_a_roll_of_one_at_a_certain_chance()
            {
                DisarmRules.Works(true, 1f, 1f).ShouldBeFalse();
            }

            [Fact]
            public void Fails_a_trap_with_no_item()
            {
                DisarmRules.Works(false, 1f, 0f).ShouldBeFalse();
            }
        }

        public class AsksHost
        {
            /// A client's player finishing the disarm of a shared trap.
            static readonly DisarmFacts Shared = new DisarmFacts
            {
                Disarming = true,
                ClientMode = true,
                Sharing = true,
                HasTrap = true,
                Named = true,
            };

            [Fact]
            public void Asks_the_host_for_a_shared_trap()
            {
                DisarmRules.AsksHost(Shared).ShouldBeTrue();
            }

            [Fact]
            public void Leaves_a_bar_that_isnt_a_disarm()
            {
                var disarm = Shared with { Disarming = false };

                DisarmRules.AsksHost(disarm).ShouldBeFalse();
            }

            [Fact]
            public void Leaves_it_outside_client_mode()
            {
                var disarm = Shared with { ClientMode = false };

                DisarmRules.AsksHost(disarm).ShouldBeFalse();
            }

            [Fact]
            public void Leaves_it_when_not_sharing()
            {
                var disarm = Shared with { Sharing = false };

                DisarmRules.AsksHost(disarm).ShouldBeFalse();
            }

            [Fact]
            public void Leaves_it_with_no_trap()
            {
                var disarm = Shared with { HasTrap = false };

                DisarmRules.AsksHost(disarm).ShouldBeFalse();
            }

            [Fact]
            public void Leaves_a_trap_that_isnt_shared()
            {
                var disarm = Shared with { Named = false };

                DisarmRules.AsksHost(disarm).ShouldBeFalse();
            }

            [Fact]
            public void Leaves_a_trap_ours_alone()
            {
                var disarm = Shared with { OursAlone = true };

                DisarmRules.AsksHost(disarm).ShouldBeFalse();
            }
        }

        public class Outcome
        {
            [Theory]
            [InlineData(true, "1", DisarmOutcome.Disarmed)]
            [InlineData(true, "0", DisarmOutcome.Failed)]
            [InlineData(true, "", DisarmOutcome.Failed)]
            [InlineData(false, "1", DisarmOutcome.Nothing)]
            [InlineData(false, "", DisarmOutcome.Nothing)]
            public void Follows_the_host_answer(
                bool ok,
                string result,
                DisarmOutcome outcome)
            {
                var answer = DisarmRules.Outcome(ok, result);

                answer.ShouldBe(outcome);
            }
        }

        public class Crouches
        {
            /// A trap the game wouldn't crouch for, disarmed in co-op.
            static readonly CrouchFacts Standing = new CrouchFacts
            {
                InCoop = true,
                HasItem = true,
                Disarming = true,
                HasTrigger = true,
            };

            [Fact]
            public void Crouches_for_a_trap_the_game_doesnt()
            {
                DisarmRules.Crouches(Standing).ShouldBeTrue();
            }

            [Fact]
            public void Leaves_a_trap_the_game_crouches_for()
            {
                var disarm = Standing with { BendsDown = true };

                DisarmRules.Crouches(disarm).ShouldBeFalse();
            }

            [Fact]
            public void Leaves_it_outside_co_op()
            {
                var disarm = Standing with { InCoop = false };

                DisarmRules.Crouches(disarm).ShouldBeFalse();
            }

            [Fact]
            public void Leaves_it_with_no_item()
            {
                var disarm = Standing with { HasItem = false };

                DisarmRules.Crouches(disarm).ShouldBeFalse();
            }

            [Fact]
            public void Leaves_a_disarm_the_game_didnt_start()
            {
                var disarm = Standing with { Disarming = false };

                DisarmRules.Crouches(disarm).ShouldBeFalse();
            }

            [Fact]
            public void Leaves_an_item_with_no_trigger()
            {
                var disarm = Standing with { HasTrigger = false };

                DisarmRules.Crouches(disarm).ShouldBeFalse();
            }
        }
    }
}
