using DarkwoodMP.World.Dreaming;

namespace DarkwoodMP.Tests.World.Dreaming
{
    public partial class DreamRulesTests
    {
        public class ReplayBy
        {
            [Fact]
            public void Waits_two_seconds_at_most()
            {
                var by = DreamRules.ReplayBy(T(10f));

                by.Value.ShouldBe(12f);
            }
        }

        public class Replay
        {
            static readonly ReplayFacts Nothing = new ReplayFacts
            {
                Now = T(10f),
                WaitBy = T(12f),
            };

            [Fact]
            public void Ends_at_once_with_nothing_open()
            {
                var step = DreamRules.Replay(Nothing);

                step.ShouldBe(ReplayStep.End);
            }

            [Fact]
            public void Closes_an_open_dialogue_first()
            {
                var step = DreamRules.Replay(
                    Nothing with { DialogueOpen = true });

                step.ShouldBe(ReplayStep.CloseFirst);
            }

            [Fact]
            public void Closes_the_map_or_the_journal_first()
            {
                var step = DreamRules.Replay(Nothing with { MenuOpen = true });

                step.ShouldBe(ReplayStep.CloseFirst);
            }

            [Fact]
            public void Waits_for_a_dialogue_that_is_opening()
            {
                var step = DreamRules.Replay(
                    Nothing with
                    {
                        DialogueOpen = true,
                        DialogueTweening = true,
                    });

                step.ShouldBe(ReplayStep.Wait);
            }

            [Fact]
            public void Closes_a_dialogue_still_tweening_once_the_wait_is_up()
            {
                var step = DreamRules.Replay(
                    Nothing with
                    {
                        Now = T(12.5f),
                        DialogueOpen = true,
                        DialogueTweening = true,
                    });

                step.ShouldBe(ReplayStep.CloseFirst);
            }
        }
    }
}
