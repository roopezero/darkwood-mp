using DarkwoodMP.World.Story;

namespace DarkwoodMP.Tests.World.Story
{
    public class HeldRulesTests
    {
        static readonly HeldStory Chapter2 = new HeldStory
        {
            From = 7,
            Kind = StoryKind.Chapter,
            Chapter = 2,
        };

        /// A held step that could run since 100 s, checked exactly the
        /// settle time later.
        static readonly HeldFacts Settled = new HeldFacts
        {
            PlayerHere = true,
            Outcome = StoryOutcome.Run,
            Ready = true,
            ReadySince = new Seconds(100f),
            Now = new Seconds(103f),
        };

        public class Limits
        {
            [Fact]
            public void Pins_the_settle_time()
            {
                HeldRules.Settle.ShouldBe(3f);
            }
        }

        public class Repeats
        {
            [Fact]
            public void Takes_the_same_step_from_the_same_player_as_a_repeat()
            {
                HeldRules.Repeats(Chapter2, Chapter2).ShouldBeTrue();
            }

            [Fact]
            public void Takes_another_player_as_new()
            {
                var asked = Chapter2 with { From = 8 };

                HeldRules.Repeats(Chapter2, asked).ShouldBeFalse();
            }

            [Fact]
            public void Takes_another_chapter_as_new()
            {
                var asked = Chapter2 with { Chapter = 3 };

                HeldRules.Repeats(Chapter2, asked).ShouldBeFalse();
            }

            [Fact]
            public void Takes_both_endings_as_one_step()
            {
                var held = Chapter2 with { Kind = StoryKind.Epilogue };
                var asked = Chapter2 with { Kind = StoryKind.EndScreen };

                HeldRules.Repeats(held, asked).ShouldBeTrue();
            }

            [Fact]
            public void Takes_any_step_as_new_when_none_is_held()
            {
                var asked = Chapter2 with { Kind = StoryKind.Epilogue };

                HeldRules.Repeats(default, asked with { From = 0 })
                    .ShouldBeFalse();
            }
        }

        public class Next
        {
            [Fact]
            public void Runs_a_step_exactly_the_settle_time_after_it_could()
            {
                var step = HeldRules.Next(Settled);

                step.Action.ShouldBe(HeldAction.Run);
                step.Ready.ShouldBeFalse();
            }

            [Fact]
            public void Keeps_a_step_just_inside_the_settle_time()
            {
                var facts = Settled with { Now = new Seconds(102.9f) };

                var step = HeldRules.Next(facts);

                step.Action.ShouldBe(HeldAction.Keep);
                step.Ready.ShouldBeTrue();
                step.ReadySince.Value.ShouldBe(100f);
            }

            [Fact]
            public void Starts_the_settle_time_on_the_first_frame_it_could()
            {
                var facts = Settled with { Ready = false };

                var step = HeldRules.Next(facts);

                step.Action.ShouldBe(HeldAction.Keep);
                step.Ready.ShouldBeTrue();
                step.ReadySince.Value.ShouldBe(103f);
            }

            [Fact]
            public void Resets_the_settle_time_on_a_wait()
            {
                var facts = Settled with { Outcome = StoryOutcome.Wait };

                var step = HeldRules.Next(facts);

                step.Action.ShouldBe(HeldAction.Keep);
                step.Ready.ShouldBeFalse();
            }

            [Fact]
            public void Drops_a_step_that_ran_meanwhile_at_once()
            {
                var facts = Settled with
                {
                    Outcome = StoryOutcome.Done,
                    Ready = false,
                };

                HeldRules.Next(facts).Action.ShouldBe(HeldAction.Done);
            }

            [Fact]
            public void Refuses_a_step_that_no_longer_fits_at_once()
            {
                var facts = Settled with
                {
                    Outcome = StoryOutcome.Refuse,
                    Ready = false,
                };

                HeldRules.Next(facts).Action.ShouldBe(HeldAction.Refused);
            }

            [Fact]
            public void Drops_a_step_whose_player_left_first()
            {
                var facts = Settled with
                {
                    PlayerHere = false,
                    Outcome = StoryOutcome.Wait,
                };

                var step = HeldRules.Next(facts);

                step.Action.ShouldBe(HeldAction.Dropped);
                step.Ready.ShouldBeTrue();
                step.ReadySince.Value.ShouldBe(100f);
            }
        }
    }
}
