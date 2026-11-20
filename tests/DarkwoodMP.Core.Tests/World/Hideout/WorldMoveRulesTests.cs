using DarkwoodMP.World.Hideout;

namespace DarkwoodMP.Tests.World.Hideout
{
    public class WorldMoveRulesTests
    {
        /// A moveInventory step from one world container to another.
        static readonly WorldMoveFacts ChestToChest = new WorldMoveFacts
        {
            MovesInventory = true,
            HasSource = true,
            FromParent = false,
            FromLocation = false,
            LocationObjects = 0,
            SourceInventory = true,
            SourcePlayers = false,
            TargetInventories = 1,
            TargetPlayers = false,
        };

        public class IsWorldMove
        {
            [Fact]
            public void Takes_a_move_between_world_containers()
            {
                WorldMoveRules.IsWorldMove(ChestToChest).ShouldBeTrue();
            }

            [Fact]
            public void Leaves_out_another_step_type()
            {
                var step = ChestToChest with { MovesInventory = false };

                WorldMoveRules.IsWorldMove(step).ShouldBeFalse();
            }

            [Fact]
            public void Leaves_out_a_step_with_no_source()
            {
                var step = ChestToChest with { HasSource = false };

                WorldMoveRules.IsWorldMove(step).ShouldBeFalse();
            }

            [Fact]
            public void Leaves_out_targets_from_the_parent()
            {
                var step = ChestToChest with { FromParent = true };

                WorldMoveRules.IsWorldMove(step).ShouldBeFalse();
            }

            [Fact]
            public void Leaves_out_targets_from_the_location()
            {
                var step = ChestToChest with { FromLocation = true };

                WorldMoveRules.IsWorldMove(step).ShouldBeFalse();
            }

            [Fact]
            public void Leaves_out_a_step_with_location_objects()
            {
                var step = ChestToChest with { LocationObjects = 1 };

                WorldMoveRules.IsWorldMove(step).ShouldBeFalse();
            }

            [Fact]
            public void Leaves_out_a_source_with_no_inventory()
            {
                var step = ChestToChest with { SourceInventory = false };

                WorldMoveRules.IsWorldMove(step).ShouldBeFalse();
            }

            [Fact]
            public void Leaves_out_a_players_source()
            {
                var step = ChestToChest with { SourcePlayers = true };

                WorldMoveRules.IsWorldMove(step).ShouldBeFalse();
            }

            [Fact]
            public void Leaves_out_a_players_target()
            {
                var step = ChestToChest with
                {
                    TargetInventories = 2,
                    TargetPlayers = true,
                };

                WorldMoveRules.IsWorldMove(step).ShouldBeFalse();
            }

            [Fact]
            public void Leaves_out_a_step_with_no_target_inventory()
            {
                var step = ChestToChest with { TargetInventories = 0 };

                WorldMoveRules.IsWorldMove(step).ShouldBeFalse();
            }
        }

        public class IsPlayers
        {
            [Theory]
            [InlineData(true, false, false, true)]
            [InlineData(false, true, false, true)]
            [InlineData(false, false, true, true)]
            [InlineData(false, false, false, false)]
            public void Finds_a_players_inventory(
                bool under,
                bool bag,
                bool hotbar,
                bool players)
            {
                WorldMoveRules.IsPlayers(under, bag, hotbar).ShouldBe(players);
            }
        }

        public class SendsAfter
        {
            [Theory]
            [InlineData(true, true, true, true)]
            [InlineData(false, true, true, false)]
            [InlineData(true, false, true, false)]
            [InlineData(true, true, false, false)]
            public void Sends_the_containers_from_the_host_together(
                bool together,
                bool host,
                bool store,
                bool sends)
            {
                var sent = WorldMoveRules.SendsAfter(together, host, store);

                sent.ShouldBe(sends);
            }
        }
    }
}
