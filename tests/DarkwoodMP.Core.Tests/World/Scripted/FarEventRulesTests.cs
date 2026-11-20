using DarkwoodMP.World.Scripted;

namespace DarkwoodMP.Tests.World.Scripted
{
    public class FarEventRulesTests
    {
        /// The host, together with a client, at a world location at the
        /// origin whose cull rect is 2000 by 1500; its player exactly at
        /// the rect's edge on x.
        static readonly FarFacts Host = new FarFacts
        {
            Together = true,
            AnyProxies = true,
            Host = new HostSpot
            {
                Known = true,
                Pos = new Vec2(2000f, 0f),
            },
            CullX = 2000,
            CullZ = 1500,
        };

        static readonly FarFacts Client = Host with { Client = true };

        static HostSpot HostAt(float x, float z)
        {
            return Host.Host with { Pos = new Vec2(x, z) };
        }

        public class Limits
        {
            [Fact]
            public void Pins_the_margin_and_the_list_interval()
            {
                var limits = new[]
                {
                    FarEventRules.Margin,
                    FarEventRules.ListEvery,
                };

                limits.ShouldBe(new[] { 600f, 10f });
            }
        }

        public class IsWorldLocation
        {
            [Theory]
            [InlineData(false, false, true)]
            [InlineData(true, false, false)]
            [InlineData(false, true, false)]
            public void Counts_only_a_location_neither_sub_nor_outside(
                bool sub,
                bool outside,
                bool world)
            {
                FarEventRules.IsWorldLocation(sub, outside).ShouldBe(world);
            }
        }

        public class HostNear
        {
            [Fact]
            public void Counts_a_player_at_the_rects_edge()
            {
                FarEventRules.HostNear(Host, false).ShouldBeTrue();
            }

            [Fact]
            public void Skips_a_player_just_past_the_edge()
            {
                var f = Host with { Host = HostAt(2001f, 0f) };

                FarEventRules.HostNear(f, false).ShouldBeFalse();
            }

            [Fact]
            public void Measures_the_z_axis_by_the_second_distance()
            {
                var f = Host with { Host = HostAt(0f, 1501f) };

                FarEventRules.HostNear(f, false).ShouldBeFalse();
            }

            [Fact]
            public void Counts_a_player_at_the_margin_who_was_in()
            {
                var f = Host with { Host = HostAt(-2600f, 2100f) };

                FarEventRules.HostNear(f, true).ShouldBeTrue();
            }

            [Fact]
            public void Skips_a_player_just_past_the_margin()
            {
                var f = Host with { Host = HostAt(2601f, 0f) };

                FarEventRules.HostNear(f, true).ShouldBeFalse();
            }

            [Fact]
            public void Skips_a_host_away()
            {
                var f = Host with { Host = Host.Host with { Away = true } };

                FarEventRules.HostNear(f, true).ShouldBeFalse();
            }

            [Fact]
            public void Skips_a_host_with_no_player()
            {
                var f = Host with { Host = Host.Host with { Known = false } };

                FarEventRules.HostNear(f, true).ShouldBeFalse();
            }
        }

        public class OtherNear
        {
            static readonly RemoteSpot Near = new RemoteSpot
            {
                Shown = true,
                Pos = new Vec2(0f, 1500f),
            };

            [Fact]
            public void Counts_a_player_at_the_rects_edge()
            {
                var near = FarEventRules.OtherNear(new[] { Near }, Client);

                near.ShouldBeTrue();
            }

            [Fact]
            public void Skips_a_player_just_past_it_with_no_margin()
            {
                var far = Near with { Pos = new Vec2(0f, 1501f) };

                var near = FarEventRules.OtherNear(new[] { far }, Client);

                near.ShouldBeFalse();
            }

            [Fact]
            public void Skips_a_player_away()
            {
                var away = Near with { Away = true };

                var near = FarEventRules.OtherNear(new[] { away }, Client);

                near.ShouldBeFalse();
            }

            [Fact]
            public void Skips_the_map_tests_dummy()
            {
                var dummy = Near with { Dummy = true };

                var near = FarEventRules.OtherNear(new[] { dummy }, Client);

                near.ShouldBeFalse();
            }

            [Fact]
            public void Skips_a_puppet_with_no_root()
            {
                var hidden = Near with { Shown = false };

                var near = FarEventRules.OtherNear(new[] { hidden }, Client);

                near.ShouldBeFalse();
            }

            [Fact]
            public void Counts_any_one_player_near()
            {
                var away = Near with { Away = true };

                var near =
                    FarEventRules.OtherNear(new[] { away, Near }, Client);

                near.ShouldBeTrue();
            }
        }

        public class OnEnter
        {
            [Fact]
            public void Runs_a_clients_events_as_its_own()
            {
                var part = FarEventRules.OnEnter(Client);

                part.ShouldBe(new FarPart
                {
                    Set = true,
                    Part = EventPart.PlayerAndShared,
                });
            }

            [Fact]
            public void Runs_them_as_the_game_does_with_the_host_by_it()
            {
                var part = FarEventRules.OnEnter(Host);

                part.ShouldBe(new FarPart
                {
                    Record = FarRecord.Store,
                    HostIn = true,
                    Part = EventPart.All,
                });
            }

            [Fact]
            public void Counts_the_host_in_within_the_margin()
            {
                var f = Host with { Host = HostAt(2600f, 0f) };

                FarEventRules.OnEnter(f).HostIn.ShouldBeTrue();
            }

            [Fact]
            public void Runs_the_world_side_only_with_the_host_far()
            {
                var f = Host with { Host = HostAt(2601f, 0f) };

                var part = FarEventRules.OnEnter(f);

                part.ShouldBe(new FarPart
                {
                    Set = true,
                    Part = EventPart.HostAndShared,
                    Record = FarRecord.Store,
                });
            }

            [Fact]
            public void Counts_the_host_in_with_no_clients_stand_in()
            {
                var f = Host with
                {
                    AnyProxies = false,
                    Host = HostAt(9000f, 0f),
                };

                FarEventRules.OnEnter(f).HostIn.ShouldBeTrue();
            }

            [Theory]
            [InlineData(false, false, false, EventPart.All, false)]
            [InlineData(true, true, false, EventPart.All, false)]
            [InlineData(true, false, true, EventPart.All, false)]
            [InlineData(true, false, false, EventPart.SharedOnly, false)]
            [InlineData(true, false, false, EventPart.All, true)]
            public void Leaves_them_to_the_game_when_not_shared(
                bool together,
                bool sub,
                bool outside,
                EventPart current,
                bool replaying)
            {
                var f = Client with
                {
                    Together = together,
                    Sub = sub,
                    Outside = outside,
                    Current = current,
                    Replaying = replaying,
                };

                FarEventRules.OnEnter(f).ShouldBe(default(FarPart));
            }
        }

        public class OnExit
        {
            [Fact]
            public void Runs_a_clients_exit_as_its_own()
            {
                var part = FarEventRules.OnExit(Client);

                part.Part.ShouldBe(EventPart.PlayerAndShared);
            }

            [Fact]
            public void Runs_it_as_the_game_does_with_no_record()
            {
                var f = Host with { Host = HostAt(9000f, 0f) };

                var part = FarEventRules.OnExit(f);

                part.ShouldBe(new FarPart
                {
                    Record = FarRecord.Forget,
                    Part = EventPart.All,
                });
            }

            [Fact]
            public void Runs_the_world_side_only_when_the_host_was_far()
            {
                var f = Host with { Recorded = true, HostIn = false };

                var part = FarEventRules.OnExit(f);

                part.ShouldBe(new FarPart
                {
                    Set = true,
                    Part = EventPart.HostAndShared,
                    Record = FarRecord.Forget,
                });
            }

            [Fact]
            public void Runs_it_as_the_game_does_when_the_host_was_in()
            {
                var f = Host with
                {
                    Recorded = true,
                    HostIn = true,
                    Host = HostAt(9000f, 0f),
                };

                FarEventRules.OnExit(f).Set.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_it_to_the_game_during_a_replay()
            {
                var f = Host with { Replaying = true, Recorded = true };

                FarEventRules.OnExit(f).ShouldBe(default(FarPart));
            }
        }

        public class Despawns
        {
            static readonly RemoteSpot[] Other =
            {
                new RemoteSpot { Shown = true },
            };

            [Fact]
            public void Keeps_them_on_a_client_with_another_player_by_it()
            {
                FarEventRules.Despawns(Client, Other).ShouldBeFalse();
            }

            [Fact]
            public void Despawns_them_with_nobody_else_by_it()
            {
                var none = new RemoteSpot[0];

                FarEventRules.Despawns(Client, none).ShouldBeTrue();
            }

            [Fact]
            public void Despawns_them_on_the_host()
            {
                FarEventRules.Despawns(Host, Other).ShouldBeTrue();
            }

            [Fact]
            public void Despawns_them_at_a_sub_location()
            {
                var f = Client with { Sub = true };

                FarEventRules.Despawns(f, Other).ShouldBeTrue();
            }

            [Fact]
            public void Despawns_them_when_not_together()
            {
                var f = Client with { Together = false };

                FarEventRules.Despawns(f, Other).ShouldBeTrue();
            }

            [Fact]
            public void Keeps_them_during_a_replay()
            {
                var f = Client with { Replaying = true };

                FarEventRules.Despawns(f, Other).ShouldBeFalse();
            }
        }

        public class MayTick
        {
            static readonly FarTickFacts Ready = new FarTickFacts
            {
                Host = true,
                InWorld = true,
            };

            [Fact]
            public void Ticks_the_host_in_a_world()
            {
                FarEventRules.MayTick(Ready).ShouldBeTrue();
            }

            [Theory]
            [InlineData(false, true, false, false)]
            [InlineData(true, false, false, false)]
            [InlineData(true, true, true, false)]
            [InlineData(true, true, false, true)]
            public void Skips_a_client_a_load_or_a_dream(
                bool host,
                bool world,
                bool loading,
                bool dreaming)
            {
                var f = Ready with
                {
                    Host = host,
                    InWorld = world,
                    Loading = loading,
                    Dreaming = dreaming,
                };

                FarEventRules.MayTick(f).ShouldBeFalse();
            }
        }

        public class Relist
        {
            static readonly FarTickFacts Listed = new FarTickFacts
            {
                Listed = true,
                ListedAt = new Seconds(100f),
                Now = new Seconds(110f),
            };

            [Fact]
            public void Keeps_the_list_exactly_at_the_interval()
            {
                FarEventRules.Relist(Listed).ShouldBeFalse();
            }

            [Fact]
            public void Lists_again_just_past_the_interval()
            {
                var f = Listed with { Now = new Seconds(110.5f) };

                FarEventRules.Relist(f).ShouldBeTrue();
            }

            [Fact]
            public void Lists_when_there_is_no_list()
            {
                var f = Listed with { Listed = false };

                FarEventRules.Relist(f).ShouldBeTrue();
            }
        }

        public class Tick
        {
            /// An entered, loaded location the host's player was in.
            static readonly FarFacts In = Host with
            {
                Entered = true,
                Loaded = true,
                Recorded = true,
                HostIn = true,
            };

            [Fact]
            public void Forgets_a_location_not_entered()
            {
                var f = In with { Entered = false };

                FarEventRules.Tick(f).ShouldBe(FarTick.Forget);
            }

            [Fact]
            public void Waits_for_a_location_still_loading()
            {
                var f = In with { Loaded = false };

                FarEventRules.Tick(f).ShouldBe(FarTick.Wait);
            }

            [Fact]
            public void Waits_for_a_location_activating()
            {
                var f = In with { Activating = true };

                FarEventRules.Tick(f).ShouldBe(FarTick.Wait);
            }

            [Fact]
            public void Records_a_location_seen_first()
            {
                var f = In with { Recorded = false };

                FarEventRules.Tick(f).ShouldBe(FarTick.Record);
            }

            [Fact]
            public void Keeps_a_player_who_was_in_within_the_margin()
            {
                var f = In with { Host = HostAt(2600f, 0f) };

                FarEventRules.Tick(f).ShouldBe(FarTick.Same);
            }

            [Fact]
            public void Notices_a_player_going_out_past_the_margin()
            {
                var f = In with { Host = HostAt(2601f, 0f) };

                FarEventRules.Tick(f).ShouldBe(FarTick.Went);
            }

            [Fact]
            public void Notices_a_player_coming_in()
            {
                var f = In with { HostIn = false };

                FarEventRules.Tick(f).ShouldBe(FarTick.Came);
            }

            [Fact]
            public void Leaves_out_the_margin_for_a_player_who_was_out()
            {
                var f = In with { HostIn = false, Host = HostAt(2100f, 0f) };

                FarEventRules.Tick(f).ShouldBe(FarTick.Same);
            }
        }
    }
}
