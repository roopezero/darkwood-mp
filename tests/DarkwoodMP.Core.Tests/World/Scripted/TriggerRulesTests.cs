using DarkwoodMP.World;
using DarkwoodMP.World.Scripted;

namespace DarkwoodMP.Tests.World.Scripted
{
    public class TriggerRulesTests
    {
        /// A once-only trigger that fired, whose events have a later
        /// player's part, asked about from exactly the reach away.
        static readonly TriggerFacts Fired = new TriggerFacts
        {
            Exists = true,
            Fired = true,
            HasEvents = true,
            LaterSteps = true,
            Name = "et_cellar",
            Reach = new ReachFacts
            {
                Handling = true,
                Known = true,
                StandIn = new Vec2(0f, 0f),
                Target = new Vec2(3000f, 0f),
                What = "et_cellar",
            },
        };

        /// The same trigger before it fired.
        static readonly TriggerFacts Ready = Fired with { Fired = false };

        static readonly ReachFacts PastReach = Fired.Reach with
        {
            Target = new Vec2(3001f, 0f),
        };

        public class Limits
        {
            [Fact]
            public void Pins_the_trigger_reach()
            {
                TriggerRules.Reach.ShouldBe(3000f);
            }
        }

        public class Once
        {
            [Fact]
            public void Counts_a_trigger_that_fires_once()
            {
                TriggerRules.Once(Fired).ShouldBeTrue();
            }

            [Fact]
            public void Counts_a_repeatable_trigger_whose_events_run_once()
            {
                var t = Fired with { MultipleFire = true };

                TriggerRules.Once(t).ShouldBeTrue();
            }

            [Fact]
            public void Skips_a_repeatable_trigger_with_repeatable_events()
            {
                var t = Fired with
                {
                    MultipleFire = true,
                    EventsMultipleFire = true,
                };

                TriggerRules.Once(t).ShouldBeFalse();
            }

            [Fact]
            public void Skips_a_repeatable_trigger_with_no_events()
            {
                var t = Fired with { MultipleFire = true, HasEvents = false };

                TriggerRules.Once(t).ShouldBeFalse();
            }
        }

        public class OnceDone
        {
            [Fact]
            public void Takes_a_once_only_trigger_as_done_when_fired()
            {
                TriggerRules.OnceDone(Fired).ShouldBeTrue();
            }

            [Fact]
            public void Takes_a_once_only_trigger_as_not_done_before()
            {
                TriggerRules.OnceDone(Ready).ShouldBeFalse();
            }

            [Fact]
            public void Takes_a_repeatable_trigger_as_done_when_its_events_ran()
            {
                var t = Ready with { MultipleFire = true, EventsFired = true };

                TriggerRules.OnceDone(t).ShouldBeTrue();
            }

            [Fact]
            public void Ignores_a_repeatable_triggers_own_fired_mark()
            {
                var t = Fired with { MultipleFire = true };

                TriggerRules.OnceDone(t).ShouldBeFalse();
            }
        }

        public class Spent
        {
            [Theory]
            [InlineData(false, false, false)]
            [InlineData(true, false, true)]
            [InlineData(true, true, false)]
            [InlineData(false, true, false)]
            public void Spends_only_what_happens_once_and_happened(
                bool fired,
                bool multiple,
                bool spent)
            {
                TriggerRules.Spent(fired, multiple).ShouldBe(spent);
            }
        }

        public class IsOwed
        {
            [Fact]
            public void Owes_a_fired_trigger_the_record_owes()
            {
                TriggerRules.IsOwed(Fired, true).ShouldBeTrue();
            }

            [Fact]
            public void Owes_nothing_the_record_doesnt_owe()
            {
                TriggerRules.IsOwed(Fired, false).ShouldBeFalse();
            }

            [Fact]
            public void Owes_nothing_of_a_trigger_that_hasnt_fired()
            {
                TriggerRules.IsOwed(Ready, true).ShouldBeFalse();
            }

            [Fact]
            public void Owes_nothing_without_later_steps()
            {
                var t = Fired with { LaterSteps = false };

                TriggerRules.IsOwed(t, true).ShouldBeFalse();
            }

            [Fact]
            public void Owes_nothing_of_a_missing_trigger()
            {
                var t = Fired with { Exists = false };

                TriggerRules.IsOwed(t, true).ShouldBeFalse();
            }
        }

        public class MayGivePart
        {
            [Fact]
            public void Allows_a_part_asked_for_at_the_reach()
            {
                TriggerRules.MayGivePart(Fired).ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Refuses_a_trigger_the_host_hasnt_got()
            {
                var t = Fired with { Exists = false };

                var verdict = TriggerRules.MayGivePart(t);

                verdict.Reason.ShouldBe(Refusal.NoSuchTrigger);
            }

            [Fact]
            public void Refuses_a_switched_off_trigger()
            {
                var t = Fired with { Disabled = true };

                var verdict = TriggerRules.MayGivePart(t);

                verdict.Reason.ShouldBe(Refusal.EventSwitchedOff);
            }

            [Fact]
            public void Refuses_a_trigger_that_hasnt_fired()
            {
                var verdict = TriggerRules.MayGivePart(Ready);

                verdict.Reason.ShouldBe(Refusal.NotHappened);
            }

            [Fact]
            public void Refuses_a_trigger_that_runs_each_time()
            {
                var t = Fired with
                {
                    MultipleFire = true,
                    EventsMultipleFire = true,
                    EventsFired = true,
                };

                var verdict = TriggerRules.MayGivePart(t);

                verdict.Reason.ShouldBe(Refusal.NotHappened);
            }

            [Fact]
            public void Refuses_events_with_no_players_part()
            {
                var t = Fired with { LaterSteps = false };

                var verdict = TriggerRules.MayGivePart(t);

                verdict.Reason.ShouldBe(Refusal.NoPlayerPart);
            }

            [Fact]
            public void Refuses_a_part_they_had()
            {
                var t = Fired with { Given = true };

                var verdict = TriggerRules.MayGivePart(t);

                verdict.Reason.ShouldBe(Refusal.HadTheirPart);
            }

            [Fact]
            public void Refuses_a_player_just_past_the_reach()
            {
                var t = Fired with { Reach = PastReach };

                var verdict = TriggerRules.MayGivePart(t);

                verdict.ShouldBe(Verdict.Refuse(
                    Refusal.OutOfReach,
                    Refusals.Detail("3001", "3000", "et_cellar")));
            }

            [Fact]
            public void Uses_the_trigger_reach_whatever_the_facts_say()
            {
                var t = Fired with
                {
                    Reach = Fired.Reach with { Reach = 10f },
                };

                TriggerRules.MayGivePart(t).ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Refuses_a_requirement_not_met_here()
            {
                var t = Fired with { Unmet = "itemState" };

                var verdict = TriggerRules.MayGivePart(t);

                verdict.ShouldBe(
                    Verdict.Refuse(Refusal.RequirementNotMet, "itemState"));
            }

            [Fact]
            public void Refuses_the_reach_before_a_requirement()
            {
                var t = Fired with { Reach = PastReach, Unmet = "itemState" };

                var verdict = TriggerRules.MayGivePart(t);

                verdict.Reason.ShouldBe(Refusal.OutOfReach);
            }

            [Fact]
            public void Refuses_a_part_they_had_before_the_reach()
            {
                var t = Fired with { Given = true, Reach = PastReach };

                var verdict = TriggerRules.MayGivePart(t);

                verdict.Reason.ShouldBe(Refusal.HadTheirPart);
            }

            [Fact]
            public void Refuses_a_switched_off_trigger_before_anything_else()
            {
                var t = Ready with
                {
                    Disabled = true,
                    LaterSteps = false,
                    Given = true,
                };

                var verdict = TriggerRules.MayGivePart(t);

                verdict.Reason.ShouldBe(Refusal.EventSwitchedOff);
            }
        }

        public class MayFire
        {
            [Fact]
            public void Allows_a_trigger_set_off_at_the_reach()
            {
                TriggerRules.MayFire(Ready).ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Refuses_a_switched_off_trigger()
            {
                var t = Fired with { Disabled = true };

                var verdict = TriggerRules.MayFire(t);

                verdict.Reason.ShouldBe(Refusal.EventSwitchedOff);
            }

            [Fact]
            public void Refuses_a_once_only_trigger_that_fired()
            {
                var verdict = TriggerRules.MayFire(Fired);

                verdict.Reason.ShouldBe(Refusal.AlreadyHappened);
            }

            [Fact]
            public void Allows_a_repeatable_trigger_that_fired()
            {
                var t = Fired with
                {
                    MultipleFire = true,
                    EventsMultipleFire = true,
                    EventsFired = true,
                };

                TriggerRules.MayFire(t).ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Refuses_a_trigger_with_no_events()
            {
                var t = Ready with { HasEvents = false };

                var verdict = TriggerRules.MayFire(t);

                verdict.Reason.ShouldBe(Refusal.NothingHappens);
            }

            [Fact]
            public void Refuses_events_that_ran_once()
            {
                var t = Ready with { EventsFired = true };

                var verdict = TriggerRules.MayFire(t);

                verdict.Reason.ShouldBe(Refusal.EventsRan);
            }

            [Fact]
            public void Refuses_a_player_just_past_the_reach()
            {
                var t = Ready with { Reach = PastReach };

                var verdict = TriggerRules.MayFire(t);

                verdict.Reason.ShouldBe(Refusal.OutOfReach);
            }

            [Fact]
            public void Refuses_a_requirement_not_met_here()
            {
                var t = Ready with { Unmet = "npcStateIsDead" };

                var verdict = TriggerRules.MayFire(t);

                verdict.Reason.ShouldBe(Refusal.RequirementNotMet);
            }

            [Fact]
            public void Refuses_a_fired_trigger_before_its_events()
            {
                var t = Fired with { HasEvents = false };

                var verdict = TriggerRules.MayFire(t);

                verdict.Reason.ShouldBe(Refusal.AlreadyHappened);
            }

            [Fact]
            public void Refuses_events_that_ran_before_the_reach()
            {
                var t = Ready with { EventsFired = true, Reach = PastReach };

                var verdict = TriggerRules.MayFire(t);

                verdict.Reason.ShouldBe(Refusal.EventsRan);
            }
        }

        public class MayExit
        {
            static readonly TriggerFacts Area = Fired with { Area = true };

            [Fact]
            public void Allows_an_exit_from_an_area_at_the_reach()
            {
                TriggerRules.MayExit(Area).ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Allows_an_exit_from_a_trigger_the_host_hasnt_got()
            {
                var t = Area with { Exists = false, Reach = PastReach };

                TriggerRules.MayExit(t).ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Refuses_a_trigger_that_isnt_an_area()
            {
                var verdict = TriggerRules.MayExit(Fired);

                verdict.ShouldBe(
                    Verdict.Refuse(Refusal.NotAnArea, "et_cellar"));
            }

            [Fact]
            public void Refuses_a_player_just_past_the_reach()
            {
                var t = Area with { Reach = PastReach };

                var verdict = TriggerRules.MayExit(t);

                verdict.Reason.ShouldBe(Refusal.OutOfReach);
            }

            [Fact]
            public void Refuses_a_trigger_that_isnt_an_area_before_the_reach()
            {
                var t = Fired with { Reach = PastReach };

                var verdict = TriggerRules.MayExit(t);

                verdict.Reason.ShouldBe(Refusal.NotAnArea);
            }

            [Fact]
            public void Ignores_a_switched_off_area()
            {
                var t = Area with { Disabled = true };

                TriggerRules.MayExit(t).ShouldBe(Verdict.Allow);
            }
        }
    }
}
