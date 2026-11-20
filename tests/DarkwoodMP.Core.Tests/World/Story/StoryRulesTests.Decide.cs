using DarkwoodMP.World.Story;

namespace DarkwoodMP.Tests.World.Story
{
    public partial class StoryRulesTests
    {
        /// A joiner asks for chapter 2 from the host's chapter 1, at an
        /// event in the host's world whose step is that chapter, standing
        /// exactly at the reach.
        static readonly StoryFacts Next = new StoryFacts
        {
            Kind = StoryKind.Chapter,
            Chapter = 2,
            HostInWorld = true,
            HostChapter = 1,
            EventFound = true,
            EventName = "Chapter_end_events",
            Step = ChapterStep,
            Known = true,
            StandIn = new Vec2(0f, 0f),
            EventAt = new Vec2(3000f, 0f),
        };

        /// The same, at an event the host doesn't have, from a joiner away
        /// in a place of their own when they asked.
        static readonly StoryFacts Elsewhere = Next with
        {
            EventFound = false,
            WasAway = true,
        };

        public class Decide
        {
            [Fact]
            public void Runs_the_next_chapter_exactly_at_the_reach()
            {
                var answer = StoryRules.Decide(Next);

                answer.Outcome.ShouldBe(StoryOutcome.Run);
                answer.ReachChecked.ShouldBeTrue();
            }

            [Fact]
            public void Refuses_an_event_just_past_the_reach()
            {
                var facts = Next with { EventAt = new Vec2(3000.5f, 0f) };

                var answer = StoryRules.Decide(facts);

                answer.Verdict.Reason.ShouldBe(Refusal.EventTooFar);
                answer.ReachChecked.ShouldBeTrue();
            }

            [Fact]
            public void Words_the_far_refusal_with_the_events_name()
            {
                var facts = Next with { EventAt = new Vec2(4000f, 0f) };

                var answer = StoryRules.Decide(facts);

                answer.Words.ShouldBe(
                    "Chapter_end_events is more than 3000 away from them");
            }

            [Fact]
            public void Measures_the_reach_on_the_ground()
            {
                var facts = Next with { EventAt = new Vec2(0f, 3000f) };

                StoryRules.Decide(facts).Outcome.ShouldBe(StoryOutcome.Run);
            }

            [Fact]
            public void Counts_a_requester_with_no_stand_in_as_near()
            {
                var facts = Next with
                {
                    Known = false,
                    EventAt = new Vec2(9000f, 0f),
                };

                StoryRules.Decide(facts).Outcome.ShouldBe(StoryOutcome.Run);
            }

            [Fact]
            public void Skips_the_reach_once_it_was_checked()
            {
                var facts = Next with
                {
                    ReachChecked = true,
                    EventAt = new Vec2(9000f, 0f),
                };

                StoryRules.Decide(facts).Outcome.ShouldBe(StoryOutcome.Run);
            }

            [Theory]
            [InlineData(StoryKind.None)]
            [InlineData((StoryKind)4)]
            public void Refuses_a_kind_that_isnt_a_story_step(StoryKind kind)
            {
                var facts = Next with { Kind = kind, Ran = true };

                var answer = StoryRules.Decide(facts);

                answer.Verdict.Reason.ShouldBe(Refusal.NoSuchStoryStep);
            }

            [Fact]
            public void Says_a_step_that_ran_is_done_before_any_wait()
            {
                var facts = Next with { Ran = true, OnTheWay = true };

                var answer = StoryRules.Decide(facts);

                answer.Outcome.ShouldBe(StoryOutcome.Done);
                answer.Reason.ShouldBe(
                    "the host's game has already started it");
            }

            [Fact]
            public void Holds_a_step_while_the_host_is_on_its_way()
            {
                var facts = Next with { OnTheWay = true, HostInWorld = false };

                var answer = StoryRules.Decide(facts);

                answer.Outcome.ShouldBe(StoryOutcome.Wait);
                answer.Reason.ShouldBe(
                    "the host's game is on its way to another part of the"
                    + " story");
            }

            [Fact]
            public void Holds_a_step_while_the_host_isnt_in_its_world()
            {
                var facts = Next with
                {
                    HostInWorld = false,
                    Dreams = new DreamFacts { Entering = true },
                };

                var answer = StoryRules.Decide(facts);

                answer.Outcome.ShouldBe(StoryOutcome.Wait);
                answer.Reason.ShouldBe("the host isn't in its world yet");
            }

            [Fact]
            public void Holds_a_wrong_chapter_while_the_host_dreams()
            {
                var facts = Next with
                {
                    Chapter = 3,
                    Dreams = new DreamFacts { Entering = true },
                };

                var answer = StoryRules.Decide(facts);

                answer.Outcome.ShouldBe(StoryOutcome.Wait);
                answer.Reason.ShouldBe("the host is in a dream");
            }

            [Theory]
            [InlineData(1)]
            [InlineData(3)]
            public void Refuses_a_chapter_that_isnt_the_next(int chapter)
            {
                var facts = Next with { Chapter = chapter };

                var answer = StoryRules.Decide(facts);

                answer.Verdict.Reason.ShouldBe(Refusal.NotNextChapter);
            }

            [Fact]
            public void Words_the_chapter_refusal_with_both_chapters()
            {
                var facts = Next with { Chapter = 3 };

                StoryRules.Decide(facts).Words.ShouldBe(
                    "chapter 3 isn't the next one (this is chapter 1)");
            }

            [Fact]
            public void Refuses_the_epilogue_before_the_last_chapter()
            {
                var facts = Elsewhere with { Kind = StoryKind.Epilogue };

                var answer = StoryRules.Decide(facts);

                answer.Verdict.Reason.ShouldBe(Refusal.EpilogueTooEarly);
                answer.Words.ShouldBe(
                    "the epilogue comes after chapter 2 (this is chapter 1)");
            }

            [Fact]
            public void Runs_the_epilogue_in_the_last_chapter()
            {
                var facts = Elsewhere with
                {
                    Kind = StoryKind.Epilogue,
                    Chapter = 0,
                    HostChapter = 2,
                };

                StoryRules.Decide(facts).Outcome.ShouldBe(StoryOutcome.Run);
            }

            [Fact]
            public void Runs_the_end_screen_in_any_chapter()
            {
                var facts = Elsewhere with
                {
                    Kind = StoryKind.EndScreen,
                    HostChapter = 0,
                };

                StoryRules.Decide(facts).Outcome.ShouldBe(StoryOutcome.Run);
            }

            [Fact]
            public void Refuses_an_event_whose_step_is_another_kind()
            {
                var facts = Next with { Step = default };

                var answer = StoryRules.Decide(facts);

                answer.Verdict.Reason.ShouldBe(Refusal.WrongStep);
                answer.Words.ShouldBe("Chapter_end_events doesn't do that");
            }

            [Fact]
            public void Refuses_an_event_whose_step_is_another_chapter()
            {
                var step = ChapterStep with { Chapter = 3 };
                var facts = Next with { Step = step };

                var answer = StoryRules.Decide(facts);

                answer.Verdict.Reason.ShouldBe(Refusal.WrongStep);
            }

            [Fact]
            public void Refuses_the_wrong_step_before_the_reach()
            {
                var facts = Next with
                {
                    Step = default,
                    EventAt = new Vec2(9000f, 0f),
                };

                var answer = StoryRules.Decide(facts);

                answer.Verdict.Reason.ShouldBe(Refusal.WrongStep);
                answer.ReachChecked.ShouldBeFalse();
            }

            [Fact]
            public void Runs_a_missing_event_for_a_joiner_away_when_asking()
            {
                StoryRules.Decide(Elsewhere).Outcome
                    .ShouldBe(StoryOutcome.Run);
            }

            [Fact]
            public void Runs_a_missing_event_for_a_joiner_away_now()
            {
                var facts = Elsewhere with { WasAway = false, IsAway = true };

                StoryRules.Decide(facts).Outcome.ShouldBe(StoryOutcome.Run);
            }

            [Fact]
            public void Refuses_a_missing_event_for_a_joiner_in_the_world()
            {
                var facts = Elsewhere with { WasAway = false };

                var answer = StoryRules.Decide(facts);

                answer.Verdict.Reason.ShouldBe(Refusal.EventNotInWorld);
            }

            [Fact]
            public void Keeps_the_reach_check_unchanged_on_a_wait()
            {
                var facts = Next with { ReachChecked = true, OnTheWay = true };

                StoryRules.Decide(facts).ReachChecked.ShouldBeTrue();
            }

            [Fact]
            public void Leaves_the_reach_unchecked_while_waiting()
            {
                var facts = Next with { OnTheWay = true };

                StoryRules.Decide(facts).ReachChecked.ShouldBeFalse();
            }
        }
    }
}
