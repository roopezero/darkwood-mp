using DarkwoodMP.World.Story;

namespace DarkwoodMP.Tests.World.Story
{
    public class ChapterRulesTests
    {
        public class Limits
        {
            [Fact]
            public void Pins_the_wait_for_the_hosts_chapter()
            {
                ChapterRules.WaitFor.ShouldBe(300f);
            }
        }

        public class SendsChapterStart
        {
            [Fact]
            public void Sends_from_the_host_with_a_peer()
            {
                ChapterRules.SendsChapterStart(true, 1, false).ShouldBeTrue();
            }

            [Theory]
            [InlineData(false, 1, false)]
            [InlineData(true, 0, false)]
            [InlineData(true, 2, true)]
            public void Doesnt_send_otherwise(bool host, int peers, bool sent)
            {
                ChapterRules.SendsChapterStart(host, peers, sent)
                    .ShouldBeFalse();
            }
        }

        public class BeforeGenerate
        {
            static readonly GenerateFacts Host = new GenerateFacts
            {
                InSession = true,
                NewChapter = true,
            };

            [Fact]
            public void Starts_a_new_chapter_on_the_host()
            {
                ChapterRules.BeforeGenerate(Host)
                    .ShouldBe(GenerateStep.Start);
            }

            [Fact]
            public void Skips_a_new_chapter_on_a_joiner()
            {
                var facts = Host with { IsClient = true };

                ChapterRules.BeforeGenerate(facts)
                    .ShouldBe(GenerateStep.Skip);
            }

            [Fact]
            public void Runs_a_chapters_save_loading_even_on_a_joiner()
            {
                var facts = Host with { IsClient = true, NewChapter = false };

                ChapterRules.BeforeGenerate(facts).ShouldBe(GenerateStep.Run);
            }

            [Fact]
            public void Runs_it_with_no_session()
            {
                var facts = Host with { InSession = false, IsClient = true };

                ChapterRules.BeforeGenerate(facts).ShouldBe(GenerateStep.Run);
            }
        }

        public class ChapterReady
        {
            [Theory]
            [InlineData(true, true, true)]
            [InlineData(true, false, false)]
            [InlineData(false, true, false)]
            public void Needs_a_chapter_building_and_the_world_ready(
                bool generating,
                bool ready,
                bool done)
            {
                ChapterRules.ChapterReady(generating, ready).ShouldBe(done);
            }
        }

        public class Extend
        {
            [Fact]
            public void Starts_a_wait_when_none_is_on()
            {
                var wait = ChapterRules.Extend(
                    new Seconds(50f),
                    new Seconds(50f));

                wait.Starts.ShouldBeTrue();
                wait.Until.Value.ShouldBe(350f);
            }

            [Fact]
            public void Moves_the_deadline_of_a_wait_on()
            {
                var wait = ChapterRules.Extend(
                    new Seconds(50.1f),
                    new Seconds(50f));

                wait.Starts.ShouldBeFalse();
                wait.Until.Value.ShouldBe(350f);
            }
        }

        public class Wait
        {
            static readonly ChapterWaitFacts Waiting = new ChapterWaitFacts
            {
                Now = new Seconds(299.9f),
                Until = new Seconds(300f),
                InWorld = true,
            };

            [Fact]
            public void Waits_just_before_the_deadline()
            {
                ChapterRules.Wait(Waiting).ShouldBe(ChapterWaitStep.Waits);
            }

            [Fact]
            public void Times_out_exactly_at_the_deadline_whatever_else()
            {
                var facts = Waiting with
                {
                    Now = new Seconds(300f),
                    SceneChanged = true,
                };

                ChapterRules.Wait(facts).ShouldBe(ChapterWaitStep.TimedOut);
            }

            [Fact]
            public void Ends_when_the_scene_changes()
            {
                var facts = Waiting with { SceneChanged = true };

                ChapterRules.Wait(facts).ShouldBe(ChapterWaitStep.Over);
            }

            [Fact]
            public void Ends_when_the_world_is_left()
            {
                var facts = Waiting with { InWorld = false };

                ChapterRules.Wait(facts).ShouldBe(ChapterWaitStep.Over);
            }
        }
    }
}
