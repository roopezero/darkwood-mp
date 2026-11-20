using DarkwoodMP.Combat.Enemies;

namespace DarkwoodMP.Tests.Combat.Enemies
{
    public class FreezeRulesTests
    {
        static readonly ClientFacts InHostWorld = new ClientFacts
        {
            HasSession = true,
            Client = true,
            Connected = true,
            Redirected = true,
        };

        /// A thing in the active scene, 1000 from our player, where the
        /// host doesn't run the area: only ours.
        static readonly LocalFacts NearUs = new LocalFacts
        {
            Exists = true,
            ActiveScene = true,
            HasPlayer = true,
            Thing = new Vec3(1000f, 0f, 0f),
            Us = new Vec3(0f, 0f, 0f),
        };

        /// The same thing, far from our player: the host's.
        static readonly LocalFacts Far = NearUs with
        {
            Thing = new Vec3(5000f, 0f, 0f),
        };

        /// A character starting on a client in the host's world, that
        /// the host runs.
        static readonly StartFacts Starting = new StartFacts
        {
            ClientMode = true,
            Eligible = true,
        };

        /// One of our characters the sweep looks at: the host's, not
        /// frozen yet.
        static readonly SweepFacts Ours = new SweepFacts
        {
            Eligible = true,
        };

        public class Limits
        {
            [Fact]
            public void Pins_the_sweep_and_the_local_range()
            {
                var limits = new[]
                {
                    FreezeRules.SweepInterval,
                    FreezeRules.LocalRange,
                };

                limits.ShouldBe(new[] { 1f, 4000f });
            }
        }

        public class ClientMode
        {
            [Fact]
            public void Is_on_for_a_connected_client_in_the_host_world()
            {
                var on = FreezeRules.ClientMode(InHostWorld);

                on.ShouldBeTrue();
            }

            [Fact]
            public void Is_off_with_no_session()
            {
                var client = InHostWorld with { HasSession = false };

                var on = FreezeRules.ClientMode(client);

                on.ShouldBeFalse();
            }

            [Fact]
            public void Is_off_for_a_host()
            {
                var client = InHostWorld with { Client = false };

                var on = FreezeRules.ClientMode(client);

                on.ShouldBeFalse();
            }

            [Fact]
            public void Is_off_before_it_connects()
            {
                var client = InHostWorld with { Connected = false };

                var on = FreezeRules.ClientMode(client);

                on.ShouldBeFalse();
            }

            [Fact]
            public void Is_off_outside_the_host_world()
            {
                var client = InHostWorld with { Redirected = false };

                var on = FreezeRules.ClientMode(client);

                on.ShouldBeFalse();
            }
        }

        public class FromHost
        {
            [Theory]
            [InlineData(true, true, true)]
            [InlineData(false, true, false)]
            [InlineData(true, false, false)]
            public void Takes_the_host_messages_in_client_mode(
                bool host,
                bool clientMode,
                bool taken)
            {
                var result = FreezeRules.FromHost(host, clientMode);

                result.ShouldBe(taken);
            }
        }

        public class LocalOnly
        {
            [Fact]
            public void Takes_a_thing_near_our_player_as_ours()
            {
                var local = FreezeRules.LocalOnly(NearUs);

                local.ShouldBeTrue();
            }

            [Fact]
            public void Takes_a_thing_far_from_our_player_as_the_host_s()
            {
                var local = FreezeRules.LocalOnly(Far);

                local.ShouldBeFalse();
            }

            [Fact]
            public void Takes_a_thing_exactly_at_the_range_as_the_host_s()
            {
                var thing = NearUs with { Thing = new Vec3(4000f, 0f, 0f) };

                var local = FreezeRules.LocalOnly(thing);

                local.ShouldBeFalse();
            }

            [Fact]
            public void Takes_a_thing_just_inside_the_range_as_ours()
            {
                var thing = NearUs with { Thing = new Vec3(3999.9f, 0f, 0f) };

                var local = FreezeRules.LocalOnly(thing);

                local.ShouldBeTrue();
            }

            [Fact]
            public void Measures_the_range_in_3d()
            {
                var thing = NearUs with { Thing = new Vec3(3000f, 3000f, 0f) };

                var local = FreezeRules.LocalOnly(thing);

                local.ShouldBeFalse();
            }

            [Fact]
            public void Takes_nothing_as_ours()
            {
                var thing = NearUs with { Exists = false };

                var local = FreezeRules.LocalOnly(thing);

                local.ShouldBeFalse();
            }

            [Fact]
            public void Takes_a_thing_in_another_scene_as_ours()
            {
                var thing = Far with
                {
                    ActiveScene = false,
                    HostRunsHere = true,
                };

                var local = FreezeRules.LocalOnly(thing);

                local.ShouldBeTrue();
            }

            [Fact]
            public void Takes_a_thing_in_our_own_dream_as_ours()
            {
                var thing = Far with
                {
                    Dreaming = true,
                    InDream = true,
                    OwnDream = true,
                    HostRunsHere = true,
                };

                var local = FreezeRules.LocalOnly(thing);

                local.ShouldBeTrue();
            }

            [Fact]
            public void Takes_a_thing_in_a_shared_dream_as_the_host_s()
            {
                var thing = Far with { Dreaming = true, InDream = true };

                var local = FreezeRules.LocalOnly(thing);

                local.ShouldBeFalse();
            }

            [Fact]
            public void Takes_a_thing_outside_our_dream_by_the_distance()
            {
                var thing = Far with { Dreaming = true, OwnDream = true };

                var local = FreezeRules.LocalOnly(thing);

                local.ShouldBeFalse();
            }

            [Fact]
            public void Takes_a_thing_under_a_dream_not_dreamt_by_the_distance()
            {
                var thing = Far with { InDream = true, OwnDream = true };

                var local = FreezeRules.LocalOnly(thing);

                local.ShouldBeFalse();
            }

            [Fact]
            public void Gives_the_host_a_thing_near_us_where_it_runs()
            {
                var thing = NearUs with { HostRunsHere = true };

                var local = FreezeRules.LocalOnly(thing);

                local.ShouldBeFalse();
            }

            [Fact]
            public void Takes_a_thing_as_the_host_s_with_no_player()
            {
                var thing = NearUs with { HasPlayer = false };

                var local = FreezeRules.LocalOnly(thing);

                local.ShouldBeFalse();
            }
        }

        public class FreezesAtStart
        {
            [Fact]
            public void Freezes_a_character_the_host_runs()
            {
                var freezes = FreezeRules.FreezesAtStart(Starting);

                freezes.ShouldBeTrue();
            }

            [Fact]
            public void Starts_a_character_thaw_starts()
            {
                var c = Starting with { Thawing = true };

                var freezes = FreezeRules.FreezesAtStart(c);

                freezes.ShouldBeFalse();
            }

            [Fact]
            public void Starts_anything_outside_client_mode()
            {
                var c = Starting with { ClientMode = false };

                var freezes = FreezeRules.FreezesAtStart(c);

                freezes.ShouldBeFalse();
            }

            [Fact]
            public void Starts_a_friendly_npc()
            {
                var c = Starting with { Eligible = false };

                var freezes = FreezeRules.FreezesAtStart(c);

                freezes.ShouldBeFalse();
            }

            [Fact]
            public void Starts_a_character_only_we_have()
            {
                var c = Starting with { Local = true };

                var freezes = FreezeRules.FreezesAtStart(c);

                freezes.ShouldBeFalse();
            }
        }

        public class Hides
        {
            [Theory]
            [InlineData(true, false, true)]
            [InlineData(true, true, false)]
            [InlineData(false, false, false)]
            public void Hides_when_asked_unless_it_is_a_kept_corpse(
                bool hide,
                bool keptCorpse,
                bool hides)
            {
                var result = FreezeRules.Hides(hide, keptCorpse);

                result.ShouldBe(hides);
            }
        }

        public class Sweep
        {
            [Fact]
            public void Freezes_one_the_host_runs()
            {
                var step = FreezeRules.Sweep(Ours);

                step.ShouldBe(SweepStep.Freeze);
            }

            [Fact]
            public void Leaves_one_already_frozen()
            {
                var c = Ours with { Frozen = true };

                var step = FreezeRules.Sweep(c);

                step.ShouldBe(SweepStep.None);
            }

            [Fact]
            public void Thaws_a_frozen_one_only_we_have()
            {
                var c = Ours with { Frozen = true, Local = true };

                var step = FreezeRules.Sweep(c);

                step.ShouldBe(SweepStep.Thaw);
            }

            [Fact]
            public void Leaves_a_running_one_only_we_have()
            {
                var c = Ours with { Local = true };

                var step = FreezeRules.Sweep(c);

                step.ShouldBe(SweepStep.None);
            }

            [Fact]
            public void Leaves_a_copy_of_the_host_s_enemy()
            {
                var c = Ours with { Copy = true };

                var step = FreezeRules.Sweep(c);

                step.ShouldBe(SweepStep.None);
            }

            [Fact]
            public void Leaves_a_friendly_npc()
            {
                var c = Ours with { Eligible = false };

                var step = FreezeRules.Sweep(c);

                step.ShouldBe(SweepStep.None);
            }
        }

        public class StaysFrozen
        {
            [Theory]
            [InlineData(true, true, true, true)]
            [InlineData(false, true, true, false)]
            [InlineData(true, false, true, false)]
            [InlineData(true, true, false, false)]
            public void Keeps_a_frozen_one_off_as_its_area_wakes(
                bool enable,
                bool clientMode,
                bool frozen,
                bool stays)
            {
                var result = FreezeRules.StaysFrozen(
                    enable,
                    clientMode,
                    frozen);

                result.ShouldBe(stays);
            }
        }

        public class StaysSilent
        {
            [Theory]
            [InlineData("dog_idle", true, true, true)]
            [InlineData("", true, true, false)]
            [InlineData(null, true, true, false)]
            [InlineData("dog_idle", false, true, false)]
            [InlineData("dog_idle", true, false, false)]
            public void Starts_no_loop_on_a_hidden_one(
                string loop,
                bool clientMode,
                bool hidden,
                bool silent)
            {
                var result = FreezeRules.StaysSilent(loop, clientMode, hidden);

                result.ShouldBe(silent);
            }
        }

        public class SpawnsOwn
        {
            [Theory]
            [InlineData(false, false, true)]
            [InlineData(true, true, true)]
            [InlineData(true, false, false)]
            public void Spawns_only_where_the_host_does_not_run_it(
                bool clientMode,
                bool local,
                bool spawns)
            {
                var result = FreezeRules.SpawnsOwn(clientMode, local);

                result.ShouldBe(spawns);
            }
        }

        public class Hears
        {
            [Theory]
            [InlineData(true, true, false)]
            [InlineData(true, false, true)]
            [InlineData(false, true, true)]
            public void Lets_no_frozen_one_hear_in_client_mode(
                bool clientMode,
                bool frozen,
                bool hears)
            {
                var result = FreezeRules.Hears(clientMode, frozen);

                result.ShouldBe(hears);
            }
        }

        public class IsCopy
        {
            [Theory]
            [InlineData(true, true, false, true)]
            [InlineData(true, false, true, true)]
            [InlineData(true, false, false, false)]
            [InlineData(false, true, true, false)]
            public void Takes_frozen_ones_and_copies_on_a_client(
                bool client,
                bool frozen,
                bool copy,
                bool isCopy)
            {
                var result = FreezeRules.IsCopy(client, frozen, copy);

                result.ShouldBe(isCopy);
            }
        }
    }
}
