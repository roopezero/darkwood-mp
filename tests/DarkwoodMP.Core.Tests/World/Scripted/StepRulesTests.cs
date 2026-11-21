using DarkwoodMP.World.Scripted;

namespace DarkwoodMP.Tests.World.Scripted
{
    public class StepRulesTests
    {
        /// An enabled shared step: nobody's own, not the host's.
        static readonly StepFacts Shared = new StepFacts { Exists = true };

        static readonly StepFacts PlayerStep = Shared with
        {
            PlayerType = true,
        };

        static readonly StepFacts HostStep = Shared with { HostType = true };

        static readonly StepFacts Message = PlayerStep with { Message = true };

        public class Runs
        {
            [Theory]
            [InlineData(EventPart.All, true, true, true, true)]
            [InlineData(EventPart.PlayerAndShared, true, true, false, true)]
            [InlineData(EventPart.HostAndShared, true, false, true, false)]
            [InlineData(EventPart.SharedOnly, true, false, false, false)]
            [InlineData(EventPart.PlayerOnly, false, true, false, true)]
            [InlineData(EventPart.SharedAndMessages, true, false, false, true)]
            public void Runs_each_kind_of_step_in_its_parts(
                EventPart part,
                bool shared,
                bool player,
                bool host,
                bool message)
            {
                var runs = new[]
                {
                    StepRules.Runs(part, Shared),
                    StepRules.Runs(part, PlayerStep),
                    StepRules.Runs(part, HostStep),
                    StepRules.Runs(part, Message),
                };

                runs.ShouldBe(new[] { shared, player, host, message });
            }

            [Fact]
            public void Skips_a_host_message_with_the_messages()
            {
                var step = HostStep with { Message = true };

                var runs = StepRules.Runs(EventPart.SharedAndMessages, step);

                runs.ShouldBeFalse();
            }

            [Fact]
            public void Counts_an_area_look_as_the_players()
            {
                var step = Shared with { AreaLook = true };

                var runs = StepRules.Runs(EventPart.SharedOnly, step);

                runs.ShouldBeFalse();
            }

            [Fact]
            public void Counts_a_step_on_the_player_as_the_players()
            {
                var step = Shared with { TargetsPlayer = true };

                var runs = StepRules.Runs(EventPart.PlayerOnly, step);

                runs.ShouldBeTrue();
            }

            [Fact]
            public void Gives_a_world_move_to_the_host()
            {
                var step = PlayerStep with { WorldMove = true };

                var runs = StepRules.Runs(EventPart.PlayerAndShared, step);

                runs.ShouldBeFalse();
            }

            [Fact]
            public void Runs_a_world_move_in_the_hosts_part()
            {
                var step = PlayerStep with { WorldMove = true };

                var runs = StepRules.Runs(EventPart.HostAndShared, step);

                runs.ShouldBeTrue();
            }

            [Fact]
            public void Runs_only_later_steps_in_a_later_players_part()
            {
                var step = Shared with { Type = StepType.Later };

                var runs = StepRules.Runs(EventPart.LaterPlayer, step);

                runs.ShouldBeTrue();
            }

            [Fact]
            public void Skips_a_shared_step_in_a_later_players_part()
            {
                var runs = StepRules.Runs(EventPart.LaterPlayer, Shared);

                runs.ShouldBeFalse();
            }
        }

        public class LaterStep
        {
            /// An addition of ordinary items to the player's inventory.
            static readonly StepFacts Gift = Shared with
            {
                Type = StepType.Item,
                ActiveModifier = true,
            };

            [Theory]
            [InlineData(StepType.Later, true)]
            [InlineData(StepType.PlayerTrip, true)]
            [InlineData(StepType.NotLater, false)]
            [InlineData(StepType.Other, false)]
            public void Gives_a_later_player_the_steps_of_its_type(
                StepType type,
                bool later)
            {
                var step = Shared with { Type = type };

                StepRules.LaterStep(step).ShouldBe(later);
            }

            [Fact]
            public void Gives_a_later_player_a_step_on_the_player()
            {
                var step = Shared with { TargetsPlayer = true };

                StepRules.LaterStep(step).ShouldBeTrue();
            }

            [Fact]
            public void Keeps_a_dialogue_on_the_player_from_a_later_player()
            {
                var step = Shared with
                {
                    Type = StepType.NotLater,
                    TargetsPlayer = true,
                };

                StepRules.LaterStep(step).ShouldBeFalse();
            }

            [Fact]
            public void Keeps_a_new_chapter_from_a_later_player()
            {
                var step = Shared with
                {
                    Type = StepType.PlayerTrip,
                    ActiveModifier = true,
                };

                StepRules.LaterStep(step).ShouldBeFalse();
            }

            [Fact]
            public void Gives_a_later_player_ordinary_items()
            {
                StepRules.LaterStep(Gift).ShouldBeTrue();
            }

            [Fact]
            public void Keeps_an_important_item_or_key_from_a_later_player()
            {
                var step = Gift with { SpecialItem = true };

                StepRules.LaterStep(step).ShouldBeFalse();
            }

            [Fact]
            public void Keeps_items_put_in_a_world_inventory()
            {
                var step = Gift with { ActiveModifier = false };

                StepRules.LaterStep(step).ShouldBeFalse();
            }

            [Fact]
            public void Keeps_a_removal_from_a_later_player()
            {
                var step = Gift with { ActiveModifier2 = true };

                StepRules.LaterStep(step).ShouldBeFalse();
            }

            [Fact]
            public void Keeps_a_swap_from_a_later_player()
            {
                var step = Gift with { RelentlessPursuit = true };

                StepRules.LaterStep(step).ShouldBeFalse();
            }

            [Fact]
            public void Skips_a_disabled_step()
            {
                var step = Shared with
                {
                    Type = StepType.Later,
                    Disabled = true,
                };

                StepRules.LaterStep(step).ShouldBeFalse();
            }

            [Fact]
            public void Skips_a_missing_step()
            {
                var step = new StepFacts { Type = StepType.Later };

                StepRules.LaterStep(step).ShouldBeFalse();
            }
        }
    }
}
