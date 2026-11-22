using DarkwoodMP.World.Doors;

namespace DarkwoodMP.Tests.World.Doors
{
    public partial class DoorRulesTests
    {
        public class MayAct
        {
            /// Broken, locked, blocked and with no barricade: each action
            /// trips its own first check, or none.
            static readonly DoorFacts Worst = Locked with
            {
                Destroyed = true,
                Blocked = true,
            };

            [Theory]
            [InlineData(1, Refusal.DoorBroken)]
            [InlineData(2, Refusal.None)]
            [InlineData(3, Refusal.None)]
            [InlineData(4, Refusal.DoorNotBarricaded)]
            [InlineData(5, Refusal.None)]
            [InlineData(6, Refusal.DoorAlreadyBroken)]
            [InlineData(7, Refusal.StoryOnly)]
            [InlineData(8, Refusal.StoryOnly)]
            [InlineData(9, Refusal.None)]
            [InlineData(10, Refusal.StoryOnly)]
            [InlineData(0, Refusal.None)]
            public void Checks_each_action_by_its_value(
                byte action,
                Refusal reason)
            {
                var verdict = DoorRules.MayAct(Worst, (DoorAction)action, 0f);

                verdict.Reason.ShouldBe(reason);
            }

            [Theory]
            [InlineData(6, Refusal.None)]
            [InlineData(1, Refusal.OutOfReach)]
            [InlineData(0, Refusal.OutOfReach)]
            public void Gives_only_a_hit_the_shot_reach(
                byte action,
                Refusal reason)
            {
                var door = At(1000f);

                var verdict = DoorRules.MayAct(door, (DoorAction)action, 0f);

                verdict.Reason.ShouldBe(reason);
            }

            [Fact]
            public void Passes_x_to_a_hit_as_its_damage()
            {
                var verdict = DoorRules.MayAct(Usable, DoorAction.Hit, 1500f);

                verdict.Reason.ShouldBe(Refusal.HitTooLarge);
            }

            [Fact]
            public void Passes_x_to_an_unlock_as_its_way()
            {
                var verdict = DoorRules.MayAct(Locked, DoorAction.Unlock, 2f);

                verdict.ShouldBe(Verdict.Allow);
            }
        }
    }
}
