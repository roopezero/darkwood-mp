using DarkwoodMP.Combat.Landings;

namespace DarkwoodMP.Tests.Combat.Landings
{
    public class LandingRulesTests
    {
        /// A tracked enemy of the host's throwing, shared with one peer.
        static readonly ThrowWatchFacts Throw = new ThrowWatchFacts
        {
            HasSource = true,
            Host = true,
            Peers = 1,
            Sharing = true,
            Tracked = true,
        };

        /// The host's message, in its world.
        static readonly HostSendFacts Sent = new HostSendFacts
        {
            HasSession = true,
            InWorld = true,
            IsClient = true,
            FromHost = true,
        };

        static readonly SensorChoice Web = new SensorChoice
        {
            Ranged = true,
            Name = "web",
            HasSensor = true,
        };

        public class Limits
        {
            [Fact]
            public void Pins_the_landing_limits()
            {
                var limits = new[]
                {
                    LandingRules.WatchEvery,
                    LandingRules.CopyLife,
                    LandingRules.SensorDrag,
                    LandingRules.CopySpin,
                };

                limits.ShouldBe(new[] { 0.5f, 4f, 2f, 225000f });
            }
        }

        public class Notes
        {
            [Fact]
            public void Notes_a_tracked_enemy_s_throw()
            {
                LandingRules.Notes(Throw).ShouldBeTrue();
            }

            [Fact]
            public void Leaves_a_throw_inside_one_noted_already()
            {
                var start = Throw with { Busy = true };

                LandingRules.Notes(start).ShouldBeFalse();
            }

            [Fact]
            public void Leaves_a_throw_with_nothing_to_start_from()
            {
                var start = Throw with { HasSource = false };

                LandingRules.Notes(start).ShouldBeFalse();
            }

            [Theory]
            [InlineData(false, 1, true, true)]
            [InlineData(true, 0, true, true)]
            [InlineData(true, 1, false, true)]
            [InlineData(true, 1, true, false)]
            public void Leaves_a_throw_nobody_shares(
                bool host,
                int peers,
                bool sharing,
                bool tracked)
            {
                var start = Throw with
                {
                    Host = host,
                    Peers = peers,
                    Sharing = sharing,
                    Tracked = tracked,
                };

                LandingRules.Notes(start).ShouldBeFalse();
            }
        }

        public class TakesThrown
        {
            [Theory]
            [InlineData(true, true, false, true)]
            [InlineData(false, true, false, false)]
            [InlineData(true, false, false, false)]
            [InlineData(true, true, true, false)]
            public void Takes_a_new_thrown_item_while_noting(
                bool noting,
                bool thrownItem,
                bool listed,
                bool taken)
            {
                var takes = LandingRules.TakesThrown(
                    noting,
                    thrownItem,
                    listed);

                takes.ShouldBe(taken);
            }
        }

        public class TakesLanded
        {
            [Theory]
            [InlineData(true, true, true)]
            [InlineData(false, true, false)]
            [InlineData(true, false, false)]
            public void Takes_the_land_prefab_while_noting(
                bool noting,
                bool landPrefab,
                bool taken)
            {
                var takes = LandingRules.TakesLanded(noting, landPrefab);

                takes.ShouldBe(taken);
            }
        }

        public class StartsWatch
        {
            [Theory]
            [InlineData(1, false, true)]
            [InlineData(0, false, false)]
            [InlineData(3, true, false)]
            public void Starts_one_watch_over_what_landed(
                int landed,
                bool watching,
                bool starts)
            {
                LandingRules.StartsWatch(landed, watching).ShouldBe(starts);
            }
        }

        public class Gone
        {
            [Theory]
            [InlineData(true, true, false)]
            [InlineData(false, true, true)]
            [InlineData(true, false, true)]
            public void Says_a_thing_destroyed_or_off_is_gone(
                bool exists,
                bool active,
                bool gone)
            {
                LandingRules.Gone(exists, active).ShouldBe(gone);
            }
        }

        public class MayThrow
        {
            [Theory]
            [InlineData(false, true, true, true)]
            [InlineData(true, false, false, true)]
            [InlineData(true, true, false, false)]
            [InlineData(true, false, true, false)]
            public void Stops_a_client_s_frozen_characters_and_copies(
                bool client,
                bool frozen,
                bool copy,
                bool throws)
            {
                var may = LandingRules.MayThrow(client, frozen, copy);

                may.ShouldBe(throws);
            }
        }

        public class FromHost
        {
            [Fact]
            public void Takes_the_host_s_message_in_its_world()
            {
                LandingRules.FromHost(Sent).ShouldBeTrue();
            }

            [Theory]
            [InlineData(false, true, true, true)]
            [InlineData(true, false, true, true)]
            [InlineData(true, true, false, true)]
            [InlineData(true, true, true, false)]
            public void Drops_any_other_message(
                bool session,
                bool inWorld,
                bool client,
                bool fromHost)
            {
                var message = Sent with
                {
                    HasSession = session,
                    InWorld = inWorld,
                    IsClient = client,
                    FromHost = fromHost,
                };

                LandingRules.FromHost(message).ShouldBeFalse();
            }
        }

        public class ShowsCopy
        {
            [Theory]
            [InlineData(true, true, true)]
            [InlineData(false, true, false)]
            [InlineData(true, false, false)]
            public void Shows_only_a_thrown_thing_with_a_body(
                bool thrownItem,
                bool body,
                bool shown)
            {
                LandingRules.ShowsCopy(thrownItem, body).ShouldBe(shown);
            }
        }

        public class SensorIndex
        {
            [Fact]
            public void Picks_the_first_ranged_sensor_of_the_name()
            {
                var sensors = new[] { Web with { Ranged = false }, Web, Web };

                LandingRules.SensorIndex(sensors, "web").ShouldBe(1);
            }

            [Fact]
            public void Skips_a_sensor_type_with_no_sensor()
            {
                var sensors = new[] { Web with { HasSensor = false }, Web };

                LandingRules.SensorIndex(sensors, "web").ShouldBe(1);
            }

            [Fact]
            public void Says_none_for_another_name()
            {
                var sensors = new[] { Web };

                LandingRules.SensorIndex(sensors, "Web").ShouldBe(-1);
            }
        }

        public class Landed
        {
            [Theory]
            [InlineData(false, true, LandedWay.Make)]
            [InlineData(true, true, LandedWay.Ignore)]
            [InlineData(true, false, LandedWay.Ignore)]
            [InlineData(false, false, LandedWay.Unknown)]
            public void Makes_the_host_s_thing_once(
                bool had,
                bool known,
                LandedWay way)
            {
                LandingRules.Landed(had, known).ShouldBe(way);
            }
        }
    }
}
