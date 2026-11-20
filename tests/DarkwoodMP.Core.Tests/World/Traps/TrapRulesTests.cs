using DarkwoodMP.World.Traps;

namespace DarkwoodMP.Tests.World.Traps
{
    public class TrapRulesTests
    {
        /// An active trap that can be disarmed, exactly at the reach.
        static readonly TrapFacts Armed = new TrapFacts
        {
            Exists = true,
            Active = true,
            CanDisarm = true,
            Requester = Requesters.AtReach,
        };

        static readonly TrapFacts Gone = Armed with { Exists = false };

        static readonly TrapFacts Far = Armed with
        {
            Requester = Requesters.PastReach,
        };

        public class MayFire
        {
            [Fact]
            public void Allows_an_active_trap_exactly_at_the_reach()
            {
                var verdict = TrapRules.MayFire(Armed);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Refuses_a_trap_that_is_gone()
            {
                var verdict = TrapRules.MayFire(Gone);

                verdict.Reason.ShouldBe(Refusal.TrapGone);
            }

            [Fact]
            public void Refuses_a_trap_just_past_the_reach()
            {
                var verdict = TrapRules.MayFire(Far);

                verdict.Reason.ShouldBe(Refusal.OutOfReach);
            }

            [Fact]
            public void Refuses_a_trap_that_went_off_already()
            {
                var trap = Armed with { Active = false };

                var verdict = TrapRules.MayFire(trap);

                verdict.Reason.ShouldBe(Refusal.TrapWentOff);
            }

            [Theory]
            [InlineData(TrapTest.Race, Refusal.TestTrapWentOff)]
            [InlineData(TrapTest.Gone, Refusal.TestTrapGone)]
            public void Refuses_as_the_map_test_asks(
                TrapTest test,
                Refusal reason)
            {
                var trap = Armed with { Test = test };

                var verdict = TrapRules.MayFire(trap);

                verdict.Reason.ShouldBe(reason);
            }

            [Fact]
            public void Gives_the_real_refusal_for_a_spent_trap_in_the_test()
            {
                var trap = Armed with
                {
                    Active = false,
                    Test = TrapTest.Race,
                };

                var verdict = TrapRules.MayFire(trap);

                verdict.Reason.ShouldBe(Refusal.TrapWentOff);
            }

            [Fact]
            public void Puts_the_reach_before_the_map_test()
            {
                var trap = Far with { Test = TrapTest.Gone };

                var verdict = TrapRules.MayFire(trap);

                verdict.Reason.ShouldBe(Refusal.OutOfReach);
            }

            [Fact]
            public void Puts_a_missing_trap_before_the_reach()
            {
                var trap = Far with { Exists = false };

                var verdict = TrapRules.MayFire(trap);

                verdict.Reason.ShouldBe(Refusal.TrapGone);
            }

            [Fact]
            public void Puts_the_reach_before_a_spent_trap()
            {
                var trap = Far with { Active = false };

                var verdict = TrapRules.MayFire(trap);

                verdict.Reason.ShouldBe(Refusal.OutOfReach);
            }
        }

        public class MaySwitch
        {
            [Fact]
            public void Allows_a_spent_trap_that_cannot_be_disarmed()
            {
                var trap = Armed with { Active = false, CanDisarm = false };

                var verdict = TrapRules.MaySwitch(trap);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Refuses_a_trap_that_is_gone()
            {
                var verdict = TrapRules.MaySwitch(Gone);

                verdict.Reason.ShouldBe(Refusal.TrapGone);
            }

            [Fact]
            public void Refuses_a_trap_just_past_the_reach()
            {
                var verdict = TrapRules.MaySwitch(Far);

                verdict.Reason.ShouldBe(Refusal.OutOfReach);
            }
        }

        public class MayDisarm
        {
            [Fact]
            public void Allows_an_active_trap_exactly_at_the_reach()
            {
                var verdict = TrapRules.MayDisarm(Armed);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Refuses_a_trap_that_is_gone()
            {
                var verdict = TrapRules.MayDisarm(Gone);

                verdict.Reason.ShouldBe(Refusal.TrapGone);
            }

            [Fact]
            public void Refuses_a_trap_just_past_the_reach()
            {
                var verdict = TrapRules.MayDisarm(Far);

                verdict.Reason.ShouldBe(Refusal.OutOfReach);
            }

            [Fact]
            public void Refuses_a_trap_that_went_off()
            {
                var trap = Armed with { Active = false };

                var verdict = TrapRules.MayDisarm(trap);

                verdict.Reason.ShouldBe(Refusal.CantDisarm);
            }

            [Fact]
            public void Refuses_a_trap_that_cannot_be_disarmed()
            {
                var trap = Armed with { CanDisarm = false };

                var verdict = TrapRules.MayDisarm(trap);

                verdict.Reason.ShouldBe(Refusal.CantDisarm);
            }

            [Fact]
            public void Puts_the_reach_before_the_state()
            {
                var trap = Far with { CanDisarm = false };

                var verdict = TrapRules.MayDisarm(trap);

                verdict.Reason.ShouldBe(Refusal.OutOfReach);
            }

            [Fact]
            public void Ignores_the_map_test()
            {
                var trap = Armed with { Test = TrapTest.Gone };

                var verdict = TrapRules.MayDisarm(trap);

                verdict.ShouldBe(Verdict.Allow);
            }
        }

        public class IsOurs
        {
            /// A trap our own player stepped on, in a client's game.
            static readonly CatchFacts Stepped = new CatchFacts
            {
                HasItem = true,
                Client = true,
                ByPlayer = true,
            };

            [Fact]
            public void Counts_a_trap_our_player_stepped_on()
            {
                TrapRules.IsOurs(Stepped).ShouldBeTrue();
            }

            [Fact]
            public void Counts_a_trap_a_thrown_item_set_off()
            {
                var trap = Stepped with { ByPlayer = false, ByThrown = true };

                TrapRules.IsOurs(trap).ShouldBeTrue();
            }

            [Fact]
            public void Counts_a_trap_a_player_set_down_with_no_item()
            {
                var trap = Stepped with { HasItem = false, SetByPlayer = true };

                TrapRules.IsOurs(trap).ShouldBeTrue();
            }

            [Fact]
            public void Leaves_out_a_level_trigger_zone()
            {
                var trap = Stepped with { HasItem = false, Client = false };

                TrapRules.IsOurs(trap).ShouldBeFalse();
            }

            [Fact]
            public void Counts_every_trap_on_the_host()
            {
                var trap = new CatchFacts { HasItem = true, OursAlone = true };

                TrapRules.IsOurs(trap).ShouldBeTrue();
            }

            [Fact]
            public void Leaves_out_a_trap_ours_alone()
            {
                var trap = Stepped with { OursAlone = true };

                TrapRules.IsOurs(trap).ShouldBeFalse();
            }

            [Fact]
            public void Leaves_out_a_trap_something_else_set_off()
            {
                var trap = Stepped with { ByPlayer = false };

                TrapRules.IsOurs(trap).ShouldBeFalse();
            }
        }

        public class HoldsSpawn
        {
            /// A client's own trap that spawns a vine, in the outermost call.
            static readonly HoldFacts Vine = new HoldFacts
            {
                Depth = 1,
                Sharing = true,
                Client = true,
                Spawns = true,
                Ours = true,
            };

            [Fact]
            public void Holds_the_spawn_of_our_own_trap()
            {
                TrapRules.HoldsSpawn(Vine).ShouldBeTrue();
            }

            [Theory]
            [InlineData(0)]
            [InlineData(2)]
            public void Leaves_a_call_that_isnt_the_outermost(int depth)
            {
                var trap = Vine with { Depth = depth };

                TrapRules.HoldsSpawn(trap).ShouldBeFalse();
            }

            [Fact]
            public void Leaves_it_when_not_sharing()
            {
                var trap = Vine with { Sharing = false };

                TrapRules.HoldsSpawn(trap).ShouldBeFalse();
            }

            [Fact]
            public void Leaves_it_on_the_host()
            {
                var trap = Vine with { Client = false };

                TrapRules.HoldsSpawn(trap).ShouldBeFalse();
            }

            [Fact]
            public void Leaves_a_trap_loaded_from_the_save()
            {
                var trap = Vine with { LoadedFromSave = true };

                TrapRules.HoldsSpawn(trap).ShouldBeFalse();
            }

            [Fact]
            public void Leaves_a_trap_that_spawns_nothing()
            {
                var trap = Vine with { Spawns = false };

                TrapRules.HoldsSpawn(trap).ShouldBeFalse();
            }

            [Fact]
            public void Leaves_a_trap_we_didnt_set_off()
            {
                var trap = Vine with { Ours = false };

                TrapRules.HoldsSpawn(trap).ShouldBeFalse();
            }
        }

        public class TargetsCatch
        {
            [Theory]
            [InlineData(true, true, true, true)]
            [InlineData(false, true, true, false)]
            [InlineData(true, false, true, false)]
            [InlineData(true, true, false, false)]
            public void Targets_only_a_connecting_chain_trap_that_caught_one(
                bool chain,
                bool connects,
                bool hit,
                bool targets)
            {
                var answer = TrapRules.TargetsCatch(chain, connects, hit);

                answer.ShouldBe(targets);
            }
        }

        public class HealthLost
        {
            [Fact]
            public void Measures_what_the_bite_took()
            {
                var lost = TrapRules.HealthLost(true, 100f, 70f);

                lost.ShouldBe(30f);
            }

            [Fact]
            public void Measures_nothing_without_a_player()
            {
                var lost = TrapRules.HealthLost(false, 100f, 70f);

                lost.ShouldBe(0f);
            }

            [Theory]
            [InlineData(0f)]
            [InlineData(-1f)]
            public void Measures_nothing_without_a_noted_health(float before)
            {
                var lost = TrapRules.HealthLost(true, before, 70f);

                lost.ShouldBe(0f);
            }
        }

        public class HealthGivenBack
        {
            [Fact]
            public void Gives_back_what_the_bite_took()
            {
                var health = TrapRules.HealthGivenBack(50f, 30f, 100f);

                health.ShouldBe(80f);
            }

            [Fact]
            public void Caps_the_health_at_the_most_it_can_be()
            {
                var health = TrapRules.HealthGivenBack(90f, 30f, 100f);

                health.ShouldBe(100f);
            }

            [Fact]
            public void Gives_nothing_back_to_a_dead_player()
            {
                var health = TrapRules.HealthGivenBack(0f, 30f, 100f);

                health.ShouldBe(0f);
            }

            [Fact]
            public void Leaves_the_health_when_the_bite_took_none()
            {
                var health = TrapRules.HealthGivenBack(70f, 0f, 100f);

                health.ShouldBe(70f);
            }

            [Fact]
            public void Leaves_the_health_when_the_bite_took_less_than_none()
            {
                var health = TrapRules.HealthGivenBack(70f, -5f, 100f);

                health.ShouldBe(70f);
            }
        }

        public class Ignores
        {
            /// A host's enemy walking onto a trap in a client's game.
            static readonly TouchFacts Puppet = new TouchFacts
            {
                ClientMode = true,
                HasCollider = true,
                Character = true,
                Puppet = true,
            };

            [Fact]
            public void Ignores_a_puppet_of_the_host()
            {
                TrapRules.Ignores(Puppet).ShouldBeTrue();
            }

            [Fact]
            public void Ignores_a_frozen_copy()
            {
                var touch = Puppet with { Puppet = false, Frozen = true };

                TrapRules.Ignores(touch).ShouldBeTrue();
            }

            [Fact]
            public void Lets_a_character_of_our_own_set_it_off()
            {
                var touch = Puppet with { Puppet = false };

                TrapRules.Ignores(touch).ShouldBeFalse();
            }

            [Fact]
            public void Lets_anything_set_it_off_outside_client_mode()
            {
                var touch = Puppet with { ClientMode = false };

                TrapRules.Ignores(touch).ShouldBeFalse();
            }

            [Fact]
            public void Lets_a_touch_with_no_collider_through()
            {
                var touch = Puppet with { HasCollider = false };

                TrapRules.Ignores(touch).ShouldBeFalse();
            }

            [Fact]
            public void Lets_what_isnt_a_character_set_it_off()
            {
                var touch = Puppet with { Character = false };

                TrapRules.Ignores(touch).ShouldBeFalse();
            }
        }
    }
}
