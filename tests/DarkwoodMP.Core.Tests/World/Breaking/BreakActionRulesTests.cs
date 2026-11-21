using DarkwoodMP.World;
using DarkwoodMP.World.Breaking;

namespace DarkwoodMP.Tests.World.Breaking
{
    public class BreakActionRulesTests
    {
        /// A whole destructible chair exactly at the reach.
        static readonly BreakFacts Chair = new BreakFacts
        {
            Exists = true,
            Destructible = true,
            Requester = Requesters.AtReach,
        };

        static readonly BreakFacts Broken = Chair with { Destroyed = true };

        static readonly ReachFacts Far = Requesters.PastReach;

        public class Limits
        {
            [Fact]
            public void Pins_the_hit_cap()
            {
                BreakActionRules.HitCap.ShouldBe(1000f);
            }
        }

        public class MayHit
        {
            /// The biggest hit, exactly at a shot's reach.
            static readonly BreakFacts Shot = Chair with
            {
                Damage = 1000f,
                Requester = Requesters.AtShotReach,
            };

            [Fact]
            public void Allows_the_biggest_hit_at_a_shot_reach()
            {
                var verdict = BreakActionRules.MayHit(Shot);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Allows_a_hit_of_zero()
            {
                var item = Shot with { Damage = 0f };

                var verdict = BreakActionRules.MayHit(item);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Refuses_an_item_that_isnt_here()
            {
                var item = Shot with { Exists = false };

                var verdict = BreakActionRules.MayHit(item);

                verdict.Reason.ShouldBe(Refusal.NotHere);
            }

            [Fact]
            public void Refuses_an_item_just_past_a_shot_reach()
            {
                var item = Shot with { Requester = Requesters.PastShotReach };

                var verdict = BreakActionRules.MayHit(item);

                verdict.Reason.ShouldBe(Refusal.OutOfReach);
            }

            [Fact]
            public void Refuses_an_item_broken_already()
            {
                var item = Shot with { Destroyed = true };

                var verdict = BreakActionRules.MayHit(item);

                verdict.Reason.ShouldBe(Refusal.AlreadyBroken);
            }

            [Fact]
            public void Refuses_a_hit_just_past_the_cap()
            {
                var item = Shot with { Damage = 1000.5f };

                var verdict = BreakActionRules.MayHit(item);

                verdict.Reason.ShouldBe(Refusal.HitTooLarge);
            }

            [Fact]
            public void Refuses_a_negative_hit()
            {
                var item = Shot with { Damage = -1f };

                var verdict = BreakActionRules.MayHit(item);

                verdict.Reason.ShouldBe(Refusal.HitTooLarge);
            }

            [Fact]
            public void Allows_a_hit_that_isnt_a_number()
            {
                var item = Shot with { Damage = float.NaN };

                var verdict = BreakActionRules.MayHit(item);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Puts_the_reach_before_the_state()
            {
                var item = Shot with
                {
                    Destroyed = true,
                    Requester = Requesters.At(5000f),
                };

                var verdict = BreakActionRules.MayHit(item);

                verdict.Reason.ShouldBe(Refusal.OutOfReach);
            }

            [Fact]
            public void Puts_a_broken_item_before_the_damage()
            {
                var item = Shot with { Destroyed = true, Damage = 5000f };

                var verdict = BreakActionRules.MayHit(item);

                verdict.Reason.ShouldBe(Refusal.AlreadyBroken);
            }
        }

        public class MayBreak
        {
            [Fact]
            public void Allows_a_whole_destructible_item()
            {
                var verdict = BreakActionRules.MayBreak(Chair);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Refuses_an_item_that_isnt_here()
            {
                var item = Chair with { Exists = false };

                var verdict = BreakActionRules.MayBreak(item);

                verdict.Reason.ShouldBe(Refusal.NotHere);
            }

            [Fact]
            public void Refuses_an_item_just_past_the_reach()
            {
                var item = Chair with { Requester = Far };

                var verdict = BreakActionRules.MayBreak(item);

                verdict.Reason.ShouldBe(Refusal.OutOfReach);
            }

            [Fact]
            public void Refuses_an_item_broken_already()
            {
                var verdict = BreakActionRules.MayBreak(Broken);

                verdict.Reason.ShouldBe(Refusal.AlreadyBroken);
            }

            [Fact]
            public void Refuses_an_item_that_cant_be_broken()
            {
                var item = Chair with { Destructible = false };

                var verdict = BreakActionRules.MayBreak(item);

                verdict.Reason.ShouldBe(Refusal.Unbreakable);
            }

            [Fact]
            public void Puts_a_broken_item_before_an_unbreakable_one()
            {
                var item = Broken with { Destructible = false };

                var verdict = BreakActionRules.MayBreak(item);

                verdict.Reason.ShouldBe(Refusal.AlreadyBroken);
            }
        }

        public class MayRepair
        {
            [Fact]
            public void Allows_a_broken_item()
            {
                var verdict = BreakActionRules.MayRepair(Broken);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Refuses_an_item_that_isnt_here()
            {
                var item = Broken with { Exists = false };

                var verdict = BreakActionRules.MayRepair(item);

                verdict.Reason.ShouldBe(Refusal.NotHere);
            }

            [Fact]
            public void Refuses_an_item_just_past_the_reach()
            {
                var item = Broken with { Requester = Far };

                var verdict = BreakActionRules.MayRepair(item);

                verdict.Reason.ShouldBe(Refusal.OutOfReach);
            }

            [Fact]
            public void Refuses_an_item_that_isnt_broken()
            {
                var verdict = BreakActionRules.MayRepair(Chair);

                verdict.Reason.ShouldBe(Refusal.NotBroken);
            }
        }

        public class MayDoUnknown
        {
            [Fact]
            public void Allows_a_broken_item_in_reach()
            {
                var verdict = BreakActionRules.MayDoUnknown(Broken);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Refuses_an_item_that_isnt_here()
            {
                var item = Chair with { Exists = false };

                var verdict = BreakActionRules.MayDoUnknown(item);

                verdict.Reason.ShouldBe(Refusal.NotHere);
            }

            [Fact]
            public void Refuses_an_item_just_past_the_reach()
            {
                var item = Chair with { Requester = Far };

                var verdict = BreakActionRules.MayDoUnknown(item);

                verdict.Reason.ShouldBe(Refusal.OutOfReach);
            }
        }

        public class MayExplode
        {
            /// A barrel that hasn't gone off, exactly at a shot's reach.
            static readonly ExplodeRequestFacts Barrel = new ExplodeRequestFacts
            {
                Exists = true,
                Requester = Requesters.AtShotReach,
            };

            [Fact]
            public void Allows_a_barrel_at_a_shot_reach()
            {
                var verdict = BreakActionRules.MayExplode(Barrel);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Refuses_an_explosive_that_isnt_here()
            {
                var thing = Barrel with { Exists = false };

                var verdict = BreakActionRules.MayExplode(thing);

                verdict.Reason.ShouldBe(Refusal.ExplosiveGone);
            }

            [Fact]
            public void Refuses_an_enemy_explosive()
            {
                var thing = Barrel with { OnCharacter = true };

                var verdict = BreakActionRules.MayExplode(thing);

                verdict.Reason.ShouldBe(Refusal.ExplosiveGone);
            }

            [Fact]
            public void Refuses_an_explosive_that_went_off()
            {
                var thing = Barrel with
                {
                    WentOff = true,
                    Requester = Requesters.At(5000f),
                };

                var verdict = BreakActionRules.MayExplode(thing);

                verdict.Reason.ShouldBe(Refusal.AlreadyWentOff);
            }

            [Fact]
            public void Refuses_a_barrel_just_past_a_shot_reach()
            {
                var far = Requesters.PastShotReach;
                var thing = Barrel with { Requester = far };

                var verdict = BreakActionRules.MayExplode(thing);

                verdict.Reason.ShouldBe(Refusal.OutOfReach);
            }
        }

        public class HealthBeforeHit
        {
            [Fact]
            public void Adds_the_hit_back_to_the_health_it_left()
            {
                var health = BreakActionRules.HealthBeforeHit(40f, 25f);

                health.ShouldBe(65);
            }

            [Fact]
            public void Cuts_each_to_a_whole_number_first()
            {
                var health = BreakActionRules.HealthBeforeHit(40.9f, 25.9f);

                health.ShouldBe(65);
            }
        }
    }
}
