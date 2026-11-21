using DarkwoodMP.World.Throws;

namespace DarkwoodMP.Tests.World.Throws
{
    public partial class ThrowRulesTests
    {
        public class Limits
        {
            [Fact]
            public void Pins_the_throw_limits()
            {
                var limits = new[]
                {
                    ThrowRules.ThrowReach,
                    ThrowRules.DroppedDistance,
                    ThrowRules.RecoverDelay,
                };

                limits.ShouldBe(new[] { 400f, -1f, 2f });
            }
        }

        public class Shares
        {
            /// A molotov, held, thrown while sharing.
            static readonly ThrowFacts Molotov = new ThrowFacts
            {
                Sharing = true,
                Held = true,
                Type = "molotov",
                Thrown = true,
                Body = true,
            };

            [Fact]
            public void Shares_a_held_thrown_thing()
            {
                ThrowRules.Shares(Molotov).ShouldBeTrue();
            }

            [Fact]
            public void Keeps_a_throw_while_not_sharing()
            {
                var throwing = Molotov with { Sharing = false };

                ThrowRules.Shares(throwing).ShouldBeFalse();
            }

            [Fact]
            public void Keeps_a_throw_of_nothing_held()
            {
                var throwing = Molotov with { Held = false };

                ThrowRules.Shares(throwing).ShouldBeFalse();
            }

            [Theory]
            [InlineData(null)]
            [InlineData("")]
            public void Keeps_a_throw_of_no_type(string type)
            {
                var throwing = Molotov with { Type = type };

                ThrowRules.Shares(throwing).ShouldBeFalse();
            }

            [Fact]
            public void Keeps_a_thing_that_isnt_a_thrown_item()
            {
                var throwing = Molotov with { Thrown = false };

                ThrowRules.Shares(throwing).ShouldBeFalse();
            }

            [Fact]
            public void Keeps_a_thing_with_no_rigidbody()
            {
                var throwing = Molotov with { Body = false };

                ThrowRules.Shares(throwing).ShouldBeFalse();
            }
        }

        public class WasDropped
        {
            [Fact]
            public void Takes_a_held_thing_now_gone_as_a_drop()
            {
                ThrowRules.WasDropped(true, false).ShouldBeTrue();
            }

            [Fact]
            public void Ignores_a_thing_still_held()
            {
                ThrowRules.WasDropped(true, true).ShouldBeFalse();
            }

            [Fact]
            public void Ignores_a_drop_with_nothing_held_before()
            {
                ThrowRules.WasDropped(false, false).ShouldBeFalse();
            }
        }

        public class Thrown
        {
            [Fact]
            public void Measures_from_the_thrower_to_where_it_lands_in_3d()
            {
                var shot = ThrowRules.Thrown(
                    new Vec3(1f, 2f, 3f),
                    new Vec3(3f, 4f, 12f),
                    new Vec3(0f, 0f, 0f));

                shot.Distance.ShouldBe(13f, 0.001f);
            }

            [Fact]
            public void Sends_the_velocity()
            {
                var shot = ThrowRules.Thrown(
                    new Vec3(1f, 2f, 3f),
                    new Vec3(3f, 4f, 12f),
                    new Vec3(0f, 0f, 0f));

                shot.Velocity.ShouldBe(new Vec3(1f, 2f, 3f));
            }

            [Fact]
            public void Sends_where_it_lands()
            {
                var shot = ThrowRules.Thrown(
                    new Vec3(1f, 2f, 3f),
                    new Vec3(3f, 4f, 12f),
                    new Vec3(0f, 0f, 0f));

                shot.Target.ShouldBe(new Vec3(3f, 4f, 12f));
            }
        }

        public class Dropped
        {
            [Fact]
            public void Sends_no_velocity()
            {
                var shot = ThrowRules.Dropped(new Vec3(5f, 0f, 7f));

                shot.Velocity.ShouldBe(new Vec3(0f, 0f, 0f));
            }

            [Fact]
            public void Sends_where_it_is_as_the_target()
            {
                var shot = ThrowRules.Dropped(new Vec3(5f, 0f, 7f));

                shot.Target.ShouldBe(new Vec3(5f, 0f, 7f));
            }

            [Fact]
            public void Sends_the_dropped_distance()
            {
                var shot = ThrowRules.Dropped(new Vec3(5f, 0f, 7f));

                shot.Distance.ShouldBe(-1f);
            }
        }

        public class IsDrop
        {
            [Fact]
            public void Takes_a_distance_below_zero_as_a_drop()
            {
                ThrowRules.IsDrop(-0.001f).ShouldBeTrue();
            }

            [Fact]
            public void Takes_a_distance_of_zero_as_a_throw()
            {
                ThrowRules.IsDrop(0f).ShouldBeFalse();
            }
        }

        public class Landing
        {
            /// A host's copy of a thrown molotov.
            static readonly ThrowCopyFacts Molotov = new ThrowCopyFacts
            {
                Distance = 250f,
                IsHost = true,
            };

            [Fact]
            public void Saves_a_thrown_copy_on_the_host()
            {
                ThrowRules.Landing(Molotov).ShouldBe(CopyEnd.Saved);
            }

            [Fact]
            public void Leaves_a_thrown_copy_unsaved_on_a_client()
            {
                var copy = Molotov with { IsHost = false };

                ThrowRules.Landing(copy).ShouldBe(CopyEnd.Stays);
            }

            [Fact]
            public void Destroys_a_thrown_copy_that_can_be_picked_up()
            {
                var copy = Molotov with { Recoverable = true };

                ThrowRules.Landing(copy).ShouldBe(CopyEnd.Goes);
            }

            [Fact]
            public void Keeps_a_dropped_copy_that_can_be_picked_up()
            {
                var copy = Molotov with { Distance = -1f, Recoverable = true };

                ThrowRules.Landing(copy).ShouldBe(CopyEnd.Saved);
            }

            [Fact]
            public void Leaves_a_dropped_copy_unsaved_on_a_client()
            {
                var copy = Molotov with { Distance = -1f, IsHost = false };

                ThrowRules.Landing(copy).ShouldBe(CopyEnd.Stays);
            }
        }

        public class GoesAfter
        {
            [Fact]
            public void Waits_two_seconds_after_the_flight()
            {
                var after = ThrowRules.GoesAfter(1.5f);

                after.Value.ShouldBe(3.5f);
            }
        }
    }
}
