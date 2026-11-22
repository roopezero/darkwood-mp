using DarkwoodMP.World.Story;

namespace DarkwoodMP.Tests.World.Story
{
    public partial class StoryRequestRulesTests
    {
        static readonly StoryStepFacts ChapterStep = new StoryStepFacts
        {
            Action = StepAction.Transport,
            ActiveModifier = true,
            Chapter = 2,
        };

        public class Limits
        {
            [Fact]
            public void Pins_the_story_limits()
            {
                var limits = new[]
                {
                    StoryRequestRules.LastChapter,
                    (float)StoryRequestRules.Reach,
                    StoryRequestRules.RequestGap,
                    StoryRequestRules.NeverAsked,
                };

                limits.ShouldBe(new[] { 2f, 3000f, 5f, -999f });
            }

            [Fact]
            public void Pins_the_epilogue_dream()
            {
                StoryRequestRules.EpilogueDream.ShouldBe("epilog_part1a_dream");
            }
        }

        public class KindOf
        {
            [Fact]
            public void Takes_a_transport_with_its_modifier_for_a_chapter()
            {
                StoryRequestRules.KindOf(ChapterStep)
                    .ShouldBe(StoryKind.Chapter);
            }

            [Fact]
            public void Takes_a_plain_transport_for_no_story_step()
            {
                var step = ChapterStep with { ActiveModifier = false };

                StoryRequestRules.KindOf(step).ShouldBe(StoryKind.None);
            }

            [Fact]
            public void Takes_the_epilogue_dream_for_the_epilogue()
            {
                var step = new StoryStepFacts
                {
                    Action = StepAction.StartDream,
                    Value = "epilog_part1a_dream",
                };

                StoryRequestRules.KindOf(step).ShouldBe(StoryKind.Epilogue);
            }

            [Fact]
            public void Takes_another_dream_for_no_story_step()
            {
                var step = new StoryStepFacts
                {
                    Action = StepAction.StartDream,
                    Value = "dream_tutorial_00",
                };

                StoryRequestRules.KindOf(step).ShouldBe(StoryKind.None);
            }

            [Fact]
            public void Takes_an_end_game_for_the_end_screen()
            {
                var step = new StoryStepFacts { Action = StepAction.EndGame };

                StoryRequestRules.KindOf(step).ShouldBe(StoryKind.EndScreen);
            }

            [Fact]
            public void Takes_no_step_for_no_story_step()
            {
                StoryRequestRules.KindOf(default).ShouldBe(StoryKind.None);
            }
        }

        public class KeyOf
        {
            [Theory]
            [InlineData(StoryKind.Chapter, 2, "chapter 2")]
            [InlineData(StoryKind.Epilogue, 0, "end")]
            [InlineData(StoryKind.EndScreen, 5, "end")]
            [InlineData(StoryKind.None, 1, "end")]
            public void Gives_one_key_per_chapter_and_one_for_the_end(
                StoryKind kind,
                int chapter,
                string key)
            {
                StoryRequestRules.KeyOf(kind, chapter).ShouldBe(key);
            }
        }

        public class What
        {
            [Theory]
            [InlineData(StoryKind.Chapter, 2, "chapter 2")]
            [InlineData(StoryKind.Epilogue, 0, "the game's end (Epilogue)")]
            [InlineData(StoryKind.EndScreen, 0, "the game's end (EndScreen)")]
            public void Names_the_step_as_the_notices_do(
                StoryKind kind,
                int chapter,
                string what)
            {
                StoryRequestRules.What(kind, chapter).ShouldBe(what);
            }
        }

        public class StepRuns
        {
            static readonly StepRunFacts Host = new StepRunFacts
            {
                InSession = true,
                AllParts = true,
                Kind = StoryKind.Epilogue,
            };

            [Fact]
            public void Runs_a_step_with_no_session()
            {
                var facts = Host with { InSession = false, IsClient = true };

                StoryRequestRules.StepRuns(facts).ShouldBe(StepRun.Run);
            }

            [Fact]
            public void Runs_a_step_of_every_part_on_the_host()
            {
                StoryRequestRules.StepRuns(Host).ShouldBe(StepRun.Run);
            }

            [Fact]
            public void Tells_the_joiners_first_about_a_chapter()
            {
                var facts = Host with { Kind = StoryKind.Chapter };

                StoryRequestRules.StepRuns(facts)
                    .ShouldBe(StepRun.StartChapter);
            }

            [Fact]
            public void Skips_a_part_of_a_joiners_event_on_the_host()
            {
                var facts = Host with { AllParts = false };

                StoryRequestRules.StepRuns(facts).ShouldBe(StepRun.Skip);
            }

            [Fact]
            public void Asks_the_host_for_a_joiners_own_part()
            {
                var facts = Host with { IsClient = true, OwnPart = true };

                StoryRequestRules.StepRuns(facts).ShouldBe(StepRun.AskHost);
            }

            [Fact]
            public void Skips_another_part_on_a_joiner()
            {
                var facts = Host with { IsClient = true };

                StoryRequestRules.StepRuns(facts).ShouldBe(StepRun.Skip);
            }
        }

        public class MaySend
        {
            [Fact]
            public void Allows_a_request_exactly_the_gap_after_the_last()
            {
                StoryRequestRules.MaySend(new Seconds(105f), new Seconds(100f))
                    .ShouldBeTrue();
            }

            [Fact]
            public void Drops_a_request_just_inside_the_gap()
            {
                StoryRequestRules.MaySend(
                        new Seconds(104.9f),
                        new Seconds(100f))
                    .ShouldBeFalse();
            }

            [Fact]
            public void Allows_the_first_request_at_the_start()
            {
                StoryRequestRules.MaySend(new Seconds(0f), new Seconds(-999f))
                    .ShouldBeTrue();
            }
        }

        public class NoticeFor
        {
            static readonly ReplyFacts Pending = new ReplyFacts
            {
                Ok = true,
                Result = "pending: the host is in a dream",
                What = "chapter 2",
            };

            [Fact]
            public void Says_why_the_host_refused()
            {
                var reply = Pending with { Ok = false, Reason = "too far" };

                StoryRequestRules.NoticeFor(reply)
                    .ShouldBe("The host didn't start chapter 2: too far");
            }

            [Fact]
            public void Says_a_held_step_runs_later()
            {
                StoryRequestRules.NoticeFor(Pending).ShouldBe(
                    "The host will start chapter 2 as soon as it can (the"
                    + " host is in a dream)");
            }

            [Theory]
            [InlineData("done: the host's game has already started it")]
            [InlineData(null)]
            [InlineData("")]
            public void Says_nothing_otherwise(string result)
            {
                var reply = Pending with { Result = result };

                StoryRequestRules.NoticeFor(reply).ShouldBeNull();
            }
        }

        public class HostDreaming
        {
            static readonly DreamFacts Awake = new DreamFacts
            {
                Exists = true,
            };

            [Fact]
            public void Says_an_awake_host_isnt_dreaming()
            {
                StoryRequestRules.HostDreaming(Awake).ShouldBeFalse();
            }

            [Fact]
            public void Counts_entering_a_dream_even_with_no_dreams()
            {
                var dreams = new DreamFacts { Entering = true };

                StoryRequestRules.HostDreaming(dreams).ShouldBeTrue();
            }

            [Fact]
            public void Ignores_the_flags_with_no_dreams()
            {
                var dreams = new DreamFacts { Dreaming = true };

                StoryRequestRules.HostDreaming(dreams).ShouldBeFalse();
            }

            [Theory]
            [InlineData(true, false, false, false)]
            [InlineData(false, true, false, false)]
            [InlineData(false, false, true, false)]
            [InlineData(false, false, false, true)]
            public void Counts_each_dream_flag(
                bool dreaming,
                bool prepared,
                bool switching,
                bool transition)
            {
                var dreams = Awake with
                {
                    Dreaming = dreaming,
                    Prepared = prepared,
                    Switching = switching,
                    Transition = transition,
                };

                StoryRequestRules.HostDreaming(dreams).ShouldBeTrue();
            }

            [Fact]
            public void Ignores_a_dream_only_wanted()
            {
                var dreams = Awake with { Wanted = true };

                StoryRequestRules.HostDreaming(dreams).ShouldBeFalse();
            }
        }
    }
}
