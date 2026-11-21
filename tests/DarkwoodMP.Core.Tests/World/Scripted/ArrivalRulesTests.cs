using DarkwoodMP.World;
using DarkwoodMP.World.Scripted;

namespace DarkwoodMP.Tests.World.Scripted
{
    public class ArrivalRulesTests
    {
        /// A trigger that hasn't fired, set off beside it.
        static readonly TriggerFacts Ready = new TriggerFacts
        {
            Exists = true,
            HasEvents = true,
            Reach = new ReachFacts { Handling = true, Known = true },
        };

        /// A world event that can still happen, active.
        static readonly EventsFacts Event = new EventsFacts
        {
            Exists = true,
            Active = true,
        };

        public class Limits
        {
            [Fact]
            public void Pins_the_messages_distance_and_the_words()
            {
                ArrivalRules.NearMessages.ShouldBe(2000f);
                ArrivalRules.Alone.ShouldBe("alone");
                ArrivalRules.ReachWords.ShouldBe("(reach");
            }
        }

        public class HostTakes
        {
            [Theory]
            [InlineData(EventKind.WorldTrigger)]
            [InlineData(EventKind.WorldTriggerCaused)]
            public void Refuses_the_worlds_own_triggers(EventKind kind)
            {
                var verdict = ArrivalRules.HostTakes(kind);

                verdict.Reason.ShouldBe(Refusal.HostRunsWorldEvents);
            }

            [Theory]
            [InlineData(EventKind.Trigger)]
            [InlineData(EventKind.WorldEvent)]
            [InlineData(EventKind.TriggerExit)]
            [InlineData(EventKind.PlayerPart)]
            public void Takes_a_players_own_kinds(EventKind kind)
            {
                ArrivalRules.HostTakes(kind).ShouldBe(Verdict.Allow);
            }
        }

        public class AnswerTrigger
        {
            static readonly TriggerFacts Missing = Ready with
            {
                Exists = false,
            };

            [Fact]
            public void Allows_a_trigger_that_can_fire()
            {
                var answer = ArrivalRules.AnswerTrigger(Ready, false, false);

                answer.ShouldBe(default(EventAnswer));
            }

            [Fact]
            public void Gives_a_missing_trigger_to_a_player_away()
            {
                var answer = ArrivalRules.AnswerTrigger(Missing, true, false);

                answer.Alone.ShouldBeTrue();
            }

            [Fact]
            public void Refuses_a_missing_trigger_with_unknown_world_events()
            {
                var answer =
                    ArrivalRules.AnswerTrigger(Missing, false, false);

                answer.Verdict.Reason.ShouldBe(Refusal.NoSuchEvent);
            }

            [Fact]
            public void Allows_a_missing_trigger_whose_world_events_are_known()
            {
                var answer = ArrivalRules.AnswerTrigger(Missing, false, true);

                answer.ShouldBe(default(EventAnswer));
            }

            [Fact]
            public void Checks_a_trigger_the_host_has_even_when_away()
            {
                var t = Ready with { Fired = true };

                var answer = ArrivalRules.AnswerTrigger(t, true, true);

                answer.Verdict.Reason.ShouldBe(Refusal.AlreadyHappened);
            }

            [Fact]
            public void Gives_it_to_a_player_away_before_the_name_check()
            {
                var answer = ArrivalRules.AnswerTrigger(Missing, true, false);

                answer.Verdict.ShouldBe(Verdict.Allow);
            }
        }

        public class AnswerWorldEvent
        {
            static readonly EventsFacts Missing = new EventsFacts();

            static readonly EventsFacts Happened = Event with { Fired = true };

            [Fact]
            public void Allows_a_world_event_that_can_happen()
            {
                var answer =
                    ArrivalRules.AnswerWorldEvent(Event, "open", false);

                answer.ShouldBe(default(EventAnswer));
            }

            [Fact]
            public void Gives_an_unknown_world_event_to_a_player_away()
            {
                var answer =
                    ArrivalRules.AnswerWorldEvent(Missing, "open", true);

                answer.Alone.ShouldBeTrue();
            }

            [Fact]
            public void Refuses_an_unknown_world_event()
            {
                var answer =
                    ArrivalRules.AnswerWorldEvent(Missing, "open", false);

                answer.Verdict.ShouldBe(
                    Verdict.Refuse(Refusal.NoSuchWorldEvent, "open"));
            }

            [Fact]
            public void Refuses_a_world_event_that_happened()
            {
                var answer =
                    ArrivalRules.AnswerWorldEvent(Happened, "open", false);

                answer.Verdict.ShouldBe(
                    Verdict.Refuse(Refusal.WorldEventHappened, "open"));
            }

            [Fact]
            public void Refuses_a_known_one_that_happened_also_when_away()
            {
                var answer =
                    ArrivalRules.AnswerWorldEvent(Happened, "open", true);

                answer.Verdict.Reason.ShouldBe(Refusal.WorldEventHappened);
            }

            [Fact]
            public void Allows_a_repeatable_world_event_that_happened()
            {
                var events = Happened with { MultipleFire = true };

                var answer =
                    ArrivalRules.AnswerWorldEvent(events, "open", false);

                answer.ShouldBe(default(EventAnswer));
            }
        }

        public class WorldTriggerPart
        {
            [Theory]
            [InlineData(true, true, EventPart.PlayerAndShared)]
            [InlineData(true, false, EventPart.PlayerAndShared)]
            [InlineData(false, true, EventPart.SharedAndMessages)]
            [InlineData(false, false, EventPart.SharedOnly)]
            public void Picks_the_part_by_cause_then_nearness(
                bool ours,
                bool near,
                EventPart part)
            {
                ArrivalRules.WorldTriggerPart(ours, near).ShouldBe(part);
            }
        }

        public class NearTrigger
        {
            [Fact]
            public void Counts_a_player_just_inside_the_distance()
            {
                var at = new Vec3(1999f, 0f, 0f);

                var near = ArrivalRules.NearTrigger(true, default, at);

                near.ShouldBeTrue();
            }

            [Fact]
            public void Skips_a_player_exactly_at_the_distance()
            {
                var at = new Vec3(0f, 0f, 2000f);

                var near = ArrivalRules.NearTrigger(true, default, at);

                near.ShouldBeFalse();
            }

            [Fact]
            public void Measures_the_height_too()
            {
                var at = new Vec3(1200f, 1200f, 1200f);

                var near = ArrivalRules.NearTrigger(true, default, at);

                near.ShouldBeFalse();
            }

            [Fact]
            public void Skips_a_game_with_no_player()
            {
                var near = ArrivalRules.NearTrigger(false, default, default);

                near.ShouldBeFalse();
            }
        }

        public class CanRun
        {
            [Fact]
            public void Runs_active_events_that_can_happen()
            {
                ArrivalRules.CanRun(Event).ShouldBeTrue();
            }

            [Fact]
            public void Skips_missing_events()
            {
                ArrivalRules.CanRun(default).ShouldBeFalse();
            }

            [Fact]
            public void Skips_events_that_ran_once()
            {
                var events = Event with { Fired = true };

                ArrivalRules.CanRun(events).ShouldBeFalse();
            }

            [Fact]
            public void Runs_repeatable_events_that_ran()
            {
                var events = Event with { Fired = true, MultipleFire = true };

                ArrivalRules.CanRun(events).ShouldBeTrue();
            }

            [Fact]
            public void Skips_inactive_events_that_dont_run_inactive()
            {
                var events = Event with { Active = false };

                ArrivalRules.CanRun(events).ShouldBeFalse();
            }

            [Fact]
            public void Runs_inactive_events_that_may()
            {
                var events = Event with
                {
                    Active = false,
                    RunsInactive = true,
                };

                ArrivalRules.CanRun(events).ShouldBeTrue();
            }
        }

        public class OnExit
        {
            static readonly TriggerFacts Area = Ready with { Area = true };

            [Fact]
            public void Runs_the_exit_of_an_area()
            {
                ArrivalRules.OnExit(Area, false).ShouldBe(ExitArrival.Run);
            }

            [Fact]
            public void Rearms_a_repeatable_area()
            {
                var t = Area with { MultipleFire = true };

                var exit = ArrivalRules.OnExit(t, false);

                exit.ShouldBe(ExitArrival.RunAndRearm);
            }

            [Fact]
            public void Waits_while_our_player_is_inside()
            {
                var exit = ArrivalRules.OnExit(Area, true);

                exit.ShouldBe(ExitArrival.StillInside);
            }

            [Fact]
            public void Runs_an_exit_once_per_entry()
            {
                var t = Area with { FiredExit = true };

                var exit = ArrivalRules.OnExit(t, true);

                exit.ShouldBe(ExitArrival.RanAlready);
            }

            [Theory]
            [InlineData(false, false, true)]
            [InlineData(true, true, true)]
            [InlineData(true, false, false)]
            public void Ignores_a_missing_switched_off_or_non_area_trigger(
                bool exists,
                bool disabled,
                bool area)
            {
                var t = Area with
                {
                    Exists = exists,
                    Disabled = disabled,
                    Area = area,
                    FiredExit = true,
                };

                ArrivalRules.OnExit(t, true).ShouldBe(ExitArrival.Ignored);
            }
        }

        public class Entered
        {
            [Theory]
            [InlineData(1, 0, true)]
            [InlineData(1, 1, false)]
            [InlineData(0, 1, false)]
            public void Counts_a_player_in_until_they_leave_as_often(
                int entered,
                int exited,
                bool inside)
            {
                ArrivalRules.Entered(entered, exited).ShouldBe(inside);
            }
        }

        public class PartAfterYes
        {
            [Fact]
            public void Runs_all_of_it_when_its_ours_alone()
            {
                var part = ArrivalRules.PartAfterYes("alone");

                part.ShouldBe(EventPart.All);
            }

            [Theory]
            [InlineData("")]
            [InlineData("Alone")]
            [InlineData(null)]
            public void Runs_our_part_and_the_shared_part_otherwise(
                string result)
            {
                var part = ArrivalRules.PartAfterYes(result);

                part.ShouldBe(EventPart.PlayerAndShared);
            }
        }

        public class AskAgain
        {
            [Fact]
            public void Asks_again_after_a_reach_refusal()
            {
                var reason = "et_cellar is 3500 away from them (reach 3000)";

                ArrivalRules.AskAgain(reason).ShouldBeTrue();
            }

            [Theory]
            [InlineData("they have had their part of it")]
            [InlineData("the session ended")]
            [InlineData(null)]
            public void Stops_asking_after_any_other_refusal(string reason)
            {
                ArrivalRules.AskAgain(reason).ShouldBeFalse();
            }
        }
    }
}
