using DarkwoodMP.World.Scripted;

namespace DarkwoodMP.Tests.World.Scripted
{
    public class WorldTriggerRulesTests
    {
        /// An onDeath trigger on the host, in the active scene, named.
        static readonly WorldTriggerFacts Death = new WorldTriggerFacts
        {
            Matches = true,
            HostWorldType = true,
            Active = true,
            InActiveScene = true,
            Named = true,
        };

        /// A saved once-only object appearing.
        static readonly WorldTriggerFacts Appears = Death with
        {
            Appears = true,
            Saved = true,
        };

        public class HostsWorldTrigger
        {
            [Fact]
            public void Hosts_a_death()
            {
                WorldTriggerRules.HostsWorldTrigger(Death).ShouldBeTrue();
            }

            [Fact]
            public void Hosts_a_saved_once_only_object_appearing()
            {
                WorldTriggerRules.HostsWorldTrigger(Appears).ShouldBeTrue();
            }

            [Fact]
            public void Hosts_a_clients_trigger_outside_its_own_area()
            {
                var f = Death with { Client = true };

                WorldTriggerRules.HostsWorldTrigger(f).ShouldBeTrue();
            }

            [Fact]
            public void Leaves_a_clients_own_area_to_it()
            {
                var f = Death with { Client = true, LocalOnly = true };

                WorldTriggerRules.HostsWorldTrigger(f).ShouldBeFalse();
            }

            [Fact]
            public void Ignores_the_hosts_own_area_mark()
            {
                var f = Death with { LocalOnly = true };

                WorldTriggerRules.HostsWorldTrigger(f).ShouldBeTrue();
            }

            [Fact]
            public void Skips_a_trigger_of_another_type()
            {
                var f = Death with { Matches = false };

                WorldTriggerRules.HostsWorldTrigger(f).ShouldBeFalse();
            }

            [Fact]
            public void Skips_a_type_a_player_sets_off()
            {
                var f = Death with { HostWorldType = false };

                WorldTriggerRules.HostsWorldTrigger(f).ShouldBeFalse();
            }

            [Fact]
            public void Skips_it_with_no_session()
            {
                var f = Death with { Active = false };

                WorldTriggerRules.HostsWorldTrigger(f).ShouldBeFalse();
            }

            [Fact]
            public void Skips_it_inside_a_part_being_run()
            {
                var f = Death with { Current = EventPart.SharedOnly };

                WorldTriggerRules.HostsWorldTrigger(f).ShouldBeFalse();
            }

            [Fact]
            public void Skips_a_chain_from_another_events_step()
            {
                var f = Death with { StepDepth = 1 };

                WorldTriggerRules.HostsWorldTrigger(f).ShouldBeFalse();
            }

            [Fact]
            public void Skips_a_scene_that_isnt_the_active_one()
            {
                var f = Death with { InActiveScene = false };

                WorldTriggerRules.HostsWorldTrigger(f).ShouldBeFalse();
            }

            [Fact]
            public void Skips_what_cant_be_named()
            {
                var f = Death with { Named = false };

                WorldTriggerRules.HostsWorldTrigger(f).ShouldBeFalse();
            }

            [Fact]
            public void Skips_something_made_while_playing_appearing()
            {
                var f = Appears with { Saved = false };

                WorldTriggerRules.HostsWorldTrigger(f).ShouldBeFalse();
            }

            [Fact]
            public void Skips_an_object_that_appears_each_time()
            {
                var f = Appears with { MultipleFire = true };

                WorldTriggerRules.HostsWorldTrigger(f).ShouldBeFalse();
            }

            [Fact]
            public void Hosts_a_repeatable_death()
            {
                var f = Death with { MultipleFire = true };

                WorldTriggerRules.HostsWorldTrigger(f).ShouldBeTrue();
            }
        }

        public class Remember
        {
            /// Our own player sets off an ordinary trigger.
            static readonly RememberFacts Ours = new RememberFacts
            {
                SettingOff = true,
                HasTriggers = true,
            };

            [Fact]
            public void Remembers_a_world_trigger_on_the_host_as_the_worlds()
            {
                var f = new RememberFacts
                {
                    HostWithPeers = true,
                    HostsWorld = true,
                };

                var remembered = WorldTriggerRules.Remember(f);

                remembered.ShouldBe(new Remembered
                {
                    Remembers = true,
                    World = true,
                    ByPlayer = true,
                    Part = EventPart.All,
                });
            }

            [Fact]
            public void Remembers_a_world_trigger_inside_a_part_as_the_worlds()
            {
                var f = new RememberFacts
                {
                    HostWithPeers = true,
                    HostsWorld = true,
                    Current = EventPart.HostAndShared,
                };

                WorldTriggerRules.Remember(f).World.ShouldBeTrue();
            }

            [Fact]
            public void Skips_a_world_trigger_with_no_peers()
            {
                var f = new RememberFacts { HostsWorld = true };

                WorldTriggerRules.Remember(f).ShouldBe(default(Remembered));
            }

            [Fact]
            public void Remembers_our_players_trigger_to_share()
            {
                var remembered = WorldTriggerRules.Remember(Ours);

                remembered.ShouldBe(new Remembered
                {
                    Remembers = true,
                    ByPlayer = true,
                    Part = EventPart.All,
                });
            }

            [Fact]
            public void Remembers_a_once_only_sight_as_ours()
            {
                var f = Ours with { Sight = true };

                WorldTriggerRules.Remember(f).ByPlayer.ShouldBeTrue();
            }

            [Fact]
            public void Keeps_a_repeatable_sight_unshared()
            {
                var f = Ours with { Sight = true, MultipleFire = true };

                WorldTriggerRules.Remember(f).ShouldBe(default(Remembered));
            }

            [Fact]
            public void Keeps_going_out_of_sight_unshared()
            {
                var f = Ours with { Sight = true, OutOfSight = true };

                WorldTriggerRules.Remember(f).ShouldBe(default(Remembered));
            }

            [Fact]
            public void Keeps_a_trigger_with_no_triggers_object_unshared()
            {
                var f = Ours with { HasTriggers = false };

                WorldTriggerRules.Remember(f).ShouldBe(default(Remembered));
            }

            [Fact]
            public void Remembers_a_shared_replay_as_the_shared_part()
            {
                var f = new RememberFacts { SharedReplay = true };

                var remembered = WorldTriggerRules.Remember(f);

                remembered.ShouldBe(new Remembered
                {
                    Remembers = true,
                    Part = EventPart.SharedOnly,
                });
            }

            [Fact]
            public void Prefers_our_player_over_a_shared_replay()
            {
                var f = Ours with { SharedReplay = true };

                var remembered = WorldTriggerRules.Remember(f);

                remembered.Part.ShouldBe(EventPart.All);
            }

            [Fact]
            public void Keeps_the_part_being_run()
            {
                var f = new RememberFacts
                {
                    Current = EventPart.SharedOnly,
                };

                var remembered = WorldTriggerRules.Remember(f);

                remembered.ShouldBe(new Remembered
                {
                    Remembers = true,
                    Part = EventPart.SharedOnly,
                });
            }

            [Fact]
            public void Keeps_the_part_being_run_over_a_shared_replay()
            {
                var f = new RememberFacts
                {
                    Current = EventPart.PlayerAndShared,
                    SharedReplay = true,
                };

                var remembered = WorldTriggerRules.Remember(f);

                remembered.Part.ShouldBe(EventPart.PlayerAndShared);
            }

            [Fact]
            public void Remembers_nothing_of_a_trigger_nobody_set_off()
            {
                var f = new RememberFacts();

                WorldTriggerRules.Remember(f).ShouldBe(default(Remembered));
            }
        }
    }
}
