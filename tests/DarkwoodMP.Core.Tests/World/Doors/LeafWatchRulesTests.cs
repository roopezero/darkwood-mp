using DarkwoodMP.World.Doors;

namespace DarkwoodMP.Tests.World.Doors
{
    public partial class LeafWatchRulesTests
    {
        static Seconds At(float t) => new Seconds(t);

        /// An open door near our player, watched, its leaf free.
        static readonly LeafDoorFacts Watched = new LeafDoorFacts
        {
            Exists = true,
            HasBody = true,
            HasRigidbody = true,
            Opened = true,
            Active = true,
            Watched = true,
            Distance = 100f,
        };

        /// A still leaf at its shared angle, our player far from it, on
        /// a client.
        static readonly LeafFrame Still = new LeafFrame
        {
            Watch = new LeafWatch { Shared = 0f },
            Now = At(10f),
            Angle = 0f,
            Shape = new LeafShape { Centre = new Vec2(0f, 0f) },
            Player = new Vec2(1000f, 0f),
            Named = true,
        };

        /// A leaf our player pushed, swinging now.
        static readonly LeafFrame Swinging = Still with
        {
            Watch = new LeafWatch { Moving = true, Touched = true },
            Angle = 30f,
            Spin = 1f,
        };

        /// A leaf our player pushed to 20, still since time 0.
        static readonly LeafFrame Rested = Still with
        {
            Watch = new LeafWatch
            {
                Shared = 0f,
                Moving = true,
                Touched = true,
                Resting = true,
                RestingSince = At(0f),
            },
            Now = At(1f),
            Angle = 20f,
        };

        public class Limits
        {
            [Fact]
            public void Pins_the_distances_and_angles()
            {
                var limits = new[]
                {
                    LeafWatchRules.WatchRadius,
                    LeafWatchRules.UnwatchMargin,
                    LeafWatchRules.TouchDistance,
                    LeafWatchRules.MinChange,
                    LeafWatchRules.SwingSpeed,
                };

                limits.ShouldBe(new[] { 500f, 100f, 120f, 4f, 0.2f });
            }

            [Fact]
            public void Pins_the_times()
            {
                var times = new[]
                {
                    LeafWatchRules.RestTime.Value,
                    LeafWatchRules.MinInterval.Value,
                    LeafWatchRules.StreamInterval.Value,
                    LeafWatchRules.LookInterval.Value,
                };

                times.ShouldBe(new[] { 0.4f, 0.5f, 0.1f, 1f });
            }
        }

        public class DueToLook
        {
            [Fact]
            public void Looks_once_the_time_comes()
            {
                var due = LeafWatchRules.DueToLook(At(5f), At(5f));

                due.ShouldBeTrue();
            }

            [Fact]
            public void Waits_until_the_time_comes()
            {
                var due = LeafWatchRules.DueToLook(At(4.9f), At(5f));

                due.ShouldBeFalse();
            }
        }

        public class Looks
        {
            static readonly LeafDoorFacts Found = Watched with
            {
                Watched = false,
            };

            [Fact]
            public void Watches_an_open_door_found_near()
            {
                var looks = LeafWatchRules.Looks(Found);

                looks.ShouldBeTrue();
            }

            [Fact]
            public void Skips_a_door_watched_already()
            {
                var looks = LeafWatchRules.Looks(Watched);

                looks.ShouldBeFalse();
            }

            [Fact]
            public void Skips_a_shut_door()
            {
                var door = Found with { Opened = false };

                var looks = LeafWatchRules.Looks(door);

                looks.ShouldBeFalse();
            }

            [Fact]
            public void Skips_a_broken_door()
            {
                var door = Found with { Destroyed = true };

                var looks = LeafWatchRules.Looks(door);

                looks.ShouldBeFalse();
            }

            [Fact]
            public void Skips_a_collider_that_isnt_the_leafs()
            {
                var door = Found with { HasRigidbody = false };

                var looks = LeafWatchRules.Looks(door);

                looks.ShouldBeFalse();
            }
        }

        public class FromLook
        {
            [Fact]
            public void Shares_the_leaf_where_it_is()
            {
                var watch = LeafWatchRules.FromLook(35f);

                watch.ShouldBe(new LeafWatch { Shared = 35f });
            }
        }

        public class WatchesOpened
        {
            static readonly LeafOpenFacts Opened = new LeafOpenFacts
            {
                Sharing = true,
                Door = Watched with { Watched = false },
            };

            [Fact]
            public void Watches_a_door_this_game_opened()
            {
                var watches = LeafWatchRules.WatchesOpened(Opened);

                watches.ShouldBeTrue();
            }

            [Fact]
            public void Skips_an_open_from_another_game()
            {
                var open = Opened with { Replaying = true };

                var watches = LeafWatchRules.WatchesOpened(open);

                watches.ShouldBeFalse();
            }

            [Fact]
            public void Skips_an_open_while_not_sharing()
            {
                var open = Opened with { Sharing = false };

                var watches = LeafWatchRules.WatchesOpened(open);

                watches.ShouldBeFalse();
            }

            [Fact]
            public void Skips_a_door_with_no_rigidbody()
            {
                var door = Opened.Door with { HasRigidbody = false };

                var watches = LeafWatchRules.WatchesOpened(
                    Opened with { Door = door });

                watches.ShouldBeFalse();
            }
        }

        public class FromOpen
        {
            [Fact]
            public void Follows_the_leaf_from_shut_as_our_push()
            {
                var watch = LeafWatchRules.FromOpen();

                watch.ShouldBe(new LeafWatch { Moving = true, Touched = true });
            }
        }

        public class Unwatches
        {
            [Fact]
            public void Keeps_a_door_exactly_at_the_limit()
            {
                var door = Watched with { Distance = 600f };

                var gone = LeafWatchRules.Unwatches(door);

                gone.ShouldBeFalse();
            }

            [Fact]
            public void Lets_go_a_door_just_past_the_limit()
            {
                var door = Watched with { Distance = 600.5f };

                var gone = LeafWatchRules.Unwatches(door);

                gone.ShouldBeTrue();
            }

            [Theory]
            [InlineData(0)]
            [InlineData(1)]
            [InlineData(2)]
            [InlineData(3)]
            [InlineData(4)]
            [InlineData(5)]
            public void Lets_go_a_door_that_cant_be_pushed(int fact)
            {
                var door = Without(Watched, fact);

                var gone = LeafWatchRules.Unwatches(door);

                gone.ShouldBeTrue();
            }

            static LeafDoorFacts Without(LeafDoorFacts door, int fact)
            {
                switch (fact)
                {
                    case 0:
                        return door with { Exists = false };
                    case 1:
                        return door with { Opened = false };
                    case 2:
                        return door with { Destroyed = true };
                    case 3:
                        return door with { HasBody = false };
                    case 4:
                        return door with { HasRigidbody = false };
                    default:
                        return door with { Active = false };
                }
            }
        }

        public class Waits
        {
            [Fact]
            public void Follows_a_free_leaf()
            {
                var waits = LeafWatchRules.Waits(Watched);

                waits.ShouldBeFalse();
            }

            [Fact]
            public void Skips_a_kinematic_leaf()
            {
                var door = Watched with { Kinematic = true };

                var waits = LeafWatchRules.Waits(door);

                waits.ShouldBeTrue();
            }

            [Fact]
            public void Skips_a_gliding_leaf()
            {
                var door = Watched with { Gliding = true };

                var waits = LeafWatchRules.Waits(door);

                waits.ShouldBeTrue();
            }
        }

        public class Heard
        {
            [Fact]
            public void Shares_the_angle_and_stops_following()
            {
                var watch = Rested.Watch with { NextSend = At(3f) };

                var heard = LeafWatchRules.Heard(watch, 45f);

                heard.ShouldBe(new LeafWatch
                {
                    Shared = 45f,
                    RestingSince = At(0f),
                    NextSend = At(3f),
                });
            }
        }
    }
}
