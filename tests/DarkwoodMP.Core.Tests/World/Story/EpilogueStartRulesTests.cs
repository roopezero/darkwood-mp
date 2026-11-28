using DarkwoodMP.World.Story;

namespace DarkwoodMP.Tests.World.Story
{
    public class EpilogueStartRulesTests
    {
        /// The host of a game started at the epilogue, in its world, awake.
        static readonly EpilogueStartFacts Ready = new EpilogueStartFacts
        {
            AtEpilogue = true,
            InWorld = true,
        };

        public class SkipsPrologue
        {
            [Fact]
            public void Plays_the_prologue_from_the_prologue()
            {
                var skips = EpilogueStartRules.SkipsPrologue(
                    StoryStart.Prologue);

                skips.ShouldBeFalse();
            }

            [Theory]
            [InlineData(StoryStart.ChapterOne)]
            [InlineData(StoryStart.Epilogue)]
            public void Skips_it_from_chapter_1_and_the_epilogue(
                StoryStart start)
            {
                var skips = EpilogueStartRules.SkipsPrologue(start);

                skips.ShouldBeTrue();
            }
        }

        public class StartOf
        {
            [Fact]
            public void Reads_a_marked_save_as_the_epilogue()
            {
                var start = EpilogueStartRules.StartOf(true, true);

                start.ShouldBe(StoryStart.Epilogue);
            }

            [Fact]
            public void Reads_a_skipped_prologue_as_chapter_1()
            {
                var start = EpilogueStartRules.StartOf(true, false);

                start.ShouldBe(StoryStart.ChapterOne);
            }

            [Fact]
            public void Reads_the_rest_as_the_prologue()
            {
                var start = EpilogueStartRules.StartOf(false, false);

                start.ShouldBe(StoryStart.Prologue);
            }
        }

        public class Flags
        {
            [Fact]
            public void Sets_the_burned_tree_flag()
            {
                var flags = EpilogueStartRules.Flags(TreeFate.Burned);

                flags.ShouldBe(new[] { "talkingTree_burnt" });
            }

            [Fact]
            public void Sets_nothing_for_a_standing_tree()
            {
                var flags = EpilogueStartRules.Flags(TreeFate.Standing);

                flags.ShouldBeEmpty();
            }
        }

        public class TreeNamed
        {
            [Fact]
            public void Reads_burned()
            {
                var tree = EpilogueStartRules.TreeNamed("burned");

                tree.ShouldBe(TreeFate.Burned);
            }

            [Theory]
            [InlineData("standing")]
            [InlineData("")]
            public void Reads_anything_else_as_standing(string name)
            {
                var tree = EpilogueStartRules.TreeNamed(name);

                tree.ShouldBe(TreeFate.Standing);
            }
        }

        public class Shows
        {
            [Fact]
            public void Shows_the_button_to_the_host()
            {
                var shows = EpilogueStartRules.Shows(Ready);

                shows.ShouldBeTrue();
            }

            [Fact]
            public void Hides_it_from_a_joiner()
            {
                var game = Ready with { Joiner = true };

                var shows = EpilogueStartRules.Shows(game);

                shows.ShouldBeFalse();
            }

            [Fact]
            public void Hides_it_once_the_epilogue_plays()
            {
                var game = Ready with { InEpilogue = true };

                var shows = EpilogueStartRules.Shows(game);

                shows.ShouldBeFalse();
            }

            [Fact]
            public void Hides_it_in_a_game_not_started_there()
            {
                var game = Ready with { AtEpilogue = false };

                var shows = EpilogueStartRules.Shows(game);

                shows.ShouldBeFalse();
            }
        }

        public class MayStart
        {
            [Fact]
            public void Allows_the_host_in_its_world()
            {
                var verdict = EpilogueStartRules.MayStart(Ready);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Refuses_a_game_not_started_there()
            {
                var game = Ready with { AtEpilogue = false };

                var verdict = EpilogueStartRules.MayStart(game);

                verdict.Reason.ShouldBe(Refusal.NotAnEpilogueStart);
            }

            [Fact]
            public void Refuses_a_joiner()
            {
                var game = Ready with { Joiner = true };

                var verdict = EpilogueStartRules.MayStart(game);

                verdict.Reason.ShouldBe(Refusal.HostStartsEpilogue);
            }

            [Fact]
            public void Refuses_once_the_epilogue_plays()
            {
                var game = Ready with { InEpilogue = true };

                var verdict = EpilogueStartRules.MayStart(game);

                verdict.Reason.ShouldBe(Refusal.PlayingEnding);
            }

            [Fact]
            public void Refuses_before_the_world_has_loaded()
            {
                var game = Ready with { InWorld = false };

                var verdict = EpilogueStartRules.MayStart(game);

                verdict.Reason.ShouldBe(Refusal.WorldNotLoaded);
            }

            [Fact]
            public void Refuses_in_a_dream()
            {
                var game = Ready with { Dreaming = true };

                var verdict = EpilogueStartRules.MayStart(game);

                verdict.Reason.ShouldBe(Refusal.WaitForDream);
            }
        }
    }
}
