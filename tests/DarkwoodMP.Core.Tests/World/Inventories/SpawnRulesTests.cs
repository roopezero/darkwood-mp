using DarkwoodMP.World.Inventories;

namespace DarkwoodMP.Tests.World.Inventories
{
    public class SpawnRulesTests
    {
        const string Trap = "Traps/beartrap";
        const string Dropped = "Items/DroppedItem";

        /// Our player dropped an item while the world is shared.
        static readonly MadeFacts Drop = new MadeFacts
        {
            Exists = true,
            Sharing = true,
            Prefab = Dropped,
        };

        /// Our player placed a placeable thing while the world is shared.
        static readonly MadeFromFacts Placed = new MadeFromFacts
        {
            Placing = true,
            Exists = true,
            Sharing = true,
            Placeable = true,
        };

        /// The host gets a client's trap, asked for, in reach, carried, and
        /// known here.
        static readonly SpawnFacts ClientTrap = new SpawnFacts
        {
            IsHost = true,
            Prefab = Trap,
            Placed = true,
            Handling = true,
            Placeable = true,
            CharacterKnown = true,
            Carried = 1,
            PrefabExists = true,
        };

        public class Limits
        {
            [Fact]
            public void Pins_the_delays_and_the_marker_tilt()
            {
                var limits = new[]
                {
                    SpawnRules.PlacedDelay,
                    SpawnRules.RestDelay,
                    SpawnRules.CorpseDelay,
                    SpawnRules.MarkerTilt,
                };

                limits.ShouldBe(new[] { 0.2f, 1.5f, 2f, 90f });
            }

            [Fact]
            public void Pins_the_prefab_paths()
            {
                var paths = new[]
                {
                    SpawnRules.GasolineTrail,
                    SpawnRules.ThrownGasoline,
                    SpawnRules.BagMarker,
                };

                paths.ShouldBe(new[]
                {
                    "Items/GasolineTrail",
                    "Items/Gasoline",
                    "Objects/_Unique/deathDrop_marker",
                });
            }
        }

        public class IsShared
        {
            [Theory]
            [InlineData("Items/DroppedItem")]
            [InlineData("Items/DroppedItem_water")]
            [InlineData("Objects/_Unique/deathDrop")]
            [InlineData("Objects/_Unique/deathDrop_water")]
            [InlineData("Items/GasolineTrail")]
            public void Shares_drops_death_bags_and_trails(string prefab)
            {
                var shared = SpawnRules.IsShared(prefab);

                shared.ShouldBeTrue();
            }

            [Theory]
            [InlineData("Items/Gasoline")]
            [InlineData("Traps/beartrap")]
            [InlineData("")]
            public void Leaves_out_everything_else(string prefab)
            {
                var shared = SpawnRules.IsShared(prefab);

                shared.ShouldBeFalse();
            }
        }

        public class IsThrownSpawn
        {
            [Theory]
            [InlineData("Items/Gasoline", true)]
            [InlineData("Items/GasolineTrail", false)]
            public void Takes_only_a_molotovs_gasoline(
                string prefab,
                bool expected)
            {
                var thrown = SpawnRules.IsThrownSpawn(prefab);

                thrown.ShouldBe(expected);
            }
        }

        public class Shares
        {
            [Fact]
            public void Shares_a_dropped_item()
            {
                var shares = SpawnRules.Shares(Drop);

                shares.ShouldBeTrue();
            }

            [Fact]
            public void Shares_anything_our_player_places()
            {
                var trap = Drop with { Prefab = Trap, Placing = true };

                var shares = SpawnRules.Shares(trap);

                shares.ShouldBeTrue();
            }

            [Fact]
            public void Leaves_out_another_kind_not_placed()
            {
                var trap = Drop with { Prefab = Trap };

                var shares = SpawnRules.Shares(trap);

                shares.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_out_a_replay()
            {
                var replay = Drop with { Replaying = true };

                var shares = SpawnRules.Shares(replay);

                shares.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_out_everything_while_not_sharing()
            {
                var alone = Drop with { Sharing = false };

                var shares = SpawnRules.Shares(alone);

                shares.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_out_a_missing_object()
            {
                var none = Drop with { Exists = false };

                var shares = SpawnRules.Shares(none);

                shares.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_out_a_drop_in_a_dream_of_our_own()
            {
                var dream = Drop with { OursAlone = true };

                var shares = SpawnRules.Shares(dream);

                shares.ShouldBeFalse();
            }
        }

        public class SendDelay
        {
            [Fact]
            public void Sends_a_drop_once_at_rest()
            {
                var delay = SpawnRules.SendDelay(false, Dropped);

                delay.Value.ShouldBe(1.5f);
            }

            [Fact]
            public void Sends_a_placed_thing_soon()
            {
                var delay = SpawnRules.SendDelay(true, Dropped);

                delay.Value.ShouldBe(0.2f);
            }

            [Fact]
            public void Sends_a_gasoline_trail_soon()
            {
                var delay = SpawnRules.SendDelay(false, "Items/GasolineTrail");

                delay.Value.ShouldBe(0.2f);
            }
        }

        public class Due
        {
            [Theory]
            [InlineData(9.9f, false)]
            [InlineData(10f, true)]
            [InlineData(10.1f, true)]
            public void Sends_from_the_time_set(float now, bool expected)
            {
                var due = SpawnRules.Due(new Seconds(now), new Seconds(10f));

                due.ShouldBe(expected);
            }
        }

        public class AsksHost
        {
            [Theory]
            [InlineData(true, true, true)]
            [InlineData(true, false, false)]
            [InlineData(false, true, false)]
            public void Asks_only_for_a_clients_placed_thing(
                bool placed,
                bool client,
                bool expected)
            {
                var asks = SpawnRules.AsksHost(placed, client);

                asks.ShouldBe(expected);
            }
        }

        public class FromPrefab
        {
            [Fact]
            public void Shares_a_placed_thing()
            {
                var step = SpawnRules.FromPrefab(Placed);

                step.ShouldBe(MadeFromStep.Share);
            }

            [Fact]
            public void Shares_what_a_thrown_item_left_first()
            {
                var thrown = new MadeFromFacts { Thrown = true };

                var step = SpawnRules.FromPrefab(thrown);

                step.ShouldBe(MadeFromStep.Thrown);
            }

            [Fact]
            public void Ignores_what_isnt_being_placed()
            {
                var made = Placed with { Placing = false };

                var step = SpawnRules.FromPrefab(made);

                step.ShouldBe(MadeFromStep.Ignore);
            }

            [Fact]
            public void Ignores_a_missing_object()
            {
                var made = Placed with { Exists = false };

                var step = SpawnRules.FromPrefab(made);

                step.ShouldBe(MadeFromStep.Ignore);
            }

            [Fact]
            public void Ignores_a_replay()
            {
                var made = Placed with { Replaying = true };

                var step = SpawnRules.FromPrefab(made);

                step.ShouldBe(MadeFromStep.Ignore);
            }

            [Fact]
            public void Ignores_it_while_not_sharing()
            {
                var made = Placed with { Sharing = false };

                var step = SpawnRules.FromPrefab(made);

                step.ShouldBe(MadeFromStep.Ignore);
            }

            [Fact]
            public void Warns_of_a_prefab_that_isnt_placeable()
            {
                var made = Placed with { Placeable = false };

                var step = SpawnRules.FromPrefab(made);

                step.ShouldBe(MadeFromStep.NotPlaceable);
            }
        }

        public class SharesThrown
        {
            [Theory]
            [InlineData(true, true, true, true)]
            [InlineData(false, true, true, false)]
            [InlineData(true, false, true, false)]
            [InlineData(true, true, false, false)]
            public void Shares_only_from_the_host_while_sharing(
                bool exists,
                bool host,
                bool sharing,
                bool expected)
            {
                var shares = SpawnRules.SharesThrown(exists, host, sharing);

                shares.ShouldBe(expected);
            }
        }

        public class TakesMarker
        {
            [Theory]
            [InlineData(true, false, true)]
            [InlineData(true, true, false)]
            [InlineData(false, false, false)]
            public void Makes_a_marker_for_a_bag_here_without_one(
                bool bagHere,
                bool hasMarker,
                bool expected)
            {
                var takes = SpawnRules.TakesMarker(bagHere, hasMarker);

                takes.ShouldBe(expected);
            }
        }

        public class Receive
        {
            static readonly Verdict Far = Verdict.Refuse(
                Refusal.OutOfReach,
                Refusals.Detail("700", "600", Trap));

            [Fact]
            public void Makes_a_clients_trap_on_the_host()
            {
                var answer = SpawnRules.Receive(ClientTrap);

                answer.Step.ShouldBe(SpawnStep.Make);
            }

            [Fact]
            public void Counts_the_trap_off_the_clients_character()
            {
                var answer = SpawnRules.Receive(ClientTrap);

                answer.CountsPlaced.ShouldBeTrue();
            }

            [Fact]
            public void Allows_a_trap_from_a_client_with_no_character_yet()
            {
                var trap = ClientTrap with
                {
                    CharacterKnown = false,
                    Carried = 0,
                };

                var answer = SpawnRules.Receive(trap);

                answer.Step.ShouldBe(SpawnStep.Make);
            }

            [Fact]
            public void Counts_nothing_for_a_client_with_no_character_yet()
            {
                var trap = ClientTrap with { CharacterKnown = false };

                var answer = SpawnRules.Receive(trap);

                answer.CountsPlaced.ShouldBeFalse();
            }

            [Fact]
            public void Ignores_a_kind_that_isnt_shared()
            {
                var trap = ClientTrap with { Placed = false };

                var answer = SpawnRules.Receive(trap);

                answer.Step.ShouldBe(SpawnStep.Ignore);
            }

            [Fact]
            public void Ignores_an_id_known_here_already()
            {
                var trap = ClientTrap with { Known = true };

                var answer = SpawnRules.Receive(trap);

                answer.Step.ShouldBe(SpawnStep.Ignore);
            }

            [Fact]
            public void Ignores_a_trap_placed_outside_a_request()
            {
                var trap = ClientTrap with { Handling = false };

                var answer = SpawnRules.Receive(trap);

                answer.Step.ShouldBe(SpawnStep.Unasked);
            }

            [Fact]
            public void Refuses_a_prefab_a_player_cant_place()
            {
                var trap = ClientTrap with { Placeable = false };

                var answer = SpawnRules.Receive(trap);

                answer.Verdict.Reason.ShouldBe(Refusal.NotPlaceable);
            }

            [Fact]
            public void Names_the_prefab_in_the_refusal()
            {
                var trap = ClientTrap with { Placeable = false };

                var answer = SpawnRules.Receive(trap);

                answer.Verdict.Detail.ShouldBe(Trap);
            }

            [Fact]
            public void Refuses_a_trap_out_of_reach()
            {
                var trap = ClientTrap with { Reach = Far };

                var answer = SpawnRules.Receive(trap);

                answer.Verdict.ShouldBe(Far);
            }

            [Fact]
            public void Refuses_a_trap_the_character_doesnt_carry()
            {
                var trap = ClientTrap with { Carried = 0 };

                var answer = SpawnRules.Receive(trap);

                answer.Verdict.Reason.ShouldBe(Refusal.NotCarried);
            }

            [Fact]
            public void Refuses_a_prefab_this_game_hasnt_got()
            {
                var trap = ClientTrap with { PrefabExists = false };

                var answer = SpawnRules.Receive(trap);

                answer.Verdict.Reason.ShouldBe(Refusal.NoSuchPrefab);
            }

            [Fact]
            public void Still_counts_a_trap_refused_for_a_missing_prefab()
            {
                var trap = ClientTrap with { PrefabExists = false };

                var answer = SpawnRules.Receive(trap);

                answer.CountsPlaced.ShouldBeTrue();
            }

            [Fact]
            public void Says_not_placeable_before_out_of_reach()
            {
                var trap = ClientTrap with { Placeable = false, Reach = Far };

                var answer = SpawnRules.Receive(trap);

                answer.Verdict.Reason.ShouldBe(Refusal.NotPlaceable);
            }

            [Fact]
            public void Says_out_of_reach_before_not_carried()
            {
                var trap = ClientTrap with { Reach = Far, Carried = 0 };

                var answer = SpawnRules.Receive(trap);

                answer.Verdict.Reason.ShouldBe(Refusal.OutOfReach);
            }

            [Fact]
            public void Says_not_carried_before_a_missing_prefab()
            {
                var trap = ClientTrap with
                {
                    Carried = 0,
                    PrefabExists = false,
                };

                var answer = SpawnRules.Receive(trap);

                answer.Verdict.Reason.ShouldBe(Refusal.NotCarried);
            }

            [Fact]
            public void Checks_nothing_but_the_prefab_for_a_hosts_trap()
            {
                var trap = new SpawnFacts
                {
                    IsClient = true,
                    Prefab = Trap,
                    Placed = true,
                    PrefabExists = true,
                };

                var answer = SpawnRules.Receive(trap);

                answer.Step.ShouldBe(SpawnStep.Make);
            }

            [Fact]
            public void Checks_nothing_but_the_prefab_for_a_dropped_item()
            {
                var drop = new SpawnFacts
                {
                    IsHost = true,
                    Prefab = Dropped,
                    PrefabExists = true,
                };

                var answer = SpawnRules.Receive(drop);

                answer.Step.ShouldBe(SpawnStep.Make);
            }

            [Fact]
            public void Takes_a_molotovs_gasoline_on_a_client()
            {
                var gas = new SpawnFacts
                {
                    IsClient = true,
                    Prefab = "Items/Gasoline",
                    PrefabExists = true,
                };

                var answer = SpawnRules.Receive(gas);

                answer.Thrown.ShouldBeTrue();
            }

            [Fact]
            public void Ignores_a_molotovs_gasoline_on_the_host()
            {
                var gas = new SpawnFacts
                {
                    IsHost = true,
                    Prefab = "Items/Gasoline",
                    PrefabExists = true,
                };

                var answer = SpawnRules.Receive(gas);

                answer.Step.ShouldBe(SpawnStep.Ignore);
            }
        }
    }
}
