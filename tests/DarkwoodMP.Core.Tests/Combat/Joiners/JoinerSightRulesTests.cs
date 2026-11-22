using System.Collections.Generic;
using DarkwoodMP.Combat.Joiners;
using DarkwoodMP.Combat.Targeting;

namespace DarkwoodMP.Tests.Combat.Joiners
{
    public class JoinerSightRulesTests
    {
        /// A living joiner facing a thing 500 away, with the host's 90
        /// degree view and a light radius of 100.
        static readonly ViewFacts Facing = new ViewFacts
        {
            StandInAlive = true,
            Target = true,
            Distance = 500f,
            HasHost = true,
            HostFov = 90f,
            HasLight = true,
            LightRadius = 100f,
            Angle = 0f,
        };

        /// The forest spirit on the host, with a target nobody sees, every
        /// player 300 away.
        static readonly TeleportFacts Unseen = new TeleportFacts
        {
            On = true,
            HasTarget = true,
            Nearest = 300f,
            TargetAt = new Vec3(10f, 20f, 30f),
            TargetUp = new Vec3(0f, 0f, 1f),
            RollX = -100,
            RollZ = 99,
        };

        /// A living banshee on the host that a joiner sees and the host's
        /// player doesn't.
        static readonly BansheeFacts SeenByJoiner = new BansheeFacts
        {
            On = true,
            Banshee = true,
            Alive = true,
            JoinerSees = true,
        };

        /// A banshee on the host whose target is a living joiner.
        static readonly BansheeFacts AfterJoiner = new BansheeFacts
        {
            On = true,
            HasTarget = true,
            TargetStandIn = true,
            TargetAlive = true,
        };

        static float[] Parts(Vec3 v) => new[] { v.X, v.Y, v.Z };

        public class Limits
        {
            [Fact]
            public void Pins_the_sight_limits()
            {
                var limits = new[]
                {
                    JoinerSightRules.SightRange,
                    JoinerSightRules.DefaultFov,
                    JoinerSightRules.SpiritFar,
                    JoinerSightRules.SpiritRise,
                    JoinerSightRules.SpreadMin,
                    JoinerSightRules.SpreadMax,
                    JoinerSightRules.SpiritEffectTime,
                    JoinerSightRules.BansheeCheckEvery,
                };

                limits.ShouldBe(new[]
                {
                    800f, 90f, 200f, 500f, -100f, 100f, 4f, 0.5f,
                });
            }

            [Fact]
            public void Pins_the_spirit_s_effect()
            {
                var effect = JoinerSightRules.SpiritEffect;

                effect.ShouldBe("ForestSpirit_fastSpawnEff");
            }
        }

        public class InView
        {
            [Fact]
            public void Looks_at_a_thing_straight_ahead()
            {
                JoinerSightRules.InView(Facing).ShouldBeTrue();
            }

            [Fact]
            public void Looks_just_inside_the_sight_range()
            {
                var view = Facing with { Distance = 799.9f };

                JoinerSightRules.InView(view).ShouldBeTrue();
            }

            [Fact]
            public void Stops_exactly_at_the_sight_range()
            {
                var view = Facing with { Distance = 800f };

                JoinerSightRules.InView(view).ShouldBeFalse();
            }

            [Fact]
            public void Sees_nothing_for_a_dead_joiner()
            {
                var view = Facing with { StandInAlive = false };

                JoinerSightRules.InView(view).ShouldBeFalse();
            }

            [Fact]
            public void Sees_nothing_with_no_thing()
            {
                var view = Facing with { Target = false };

                JoinerSightRules.InView(view).ShouldBeFalse();
            }

            [Fact]
            public void Looks_just_inside_half_the_view()
            {
                var view = Facing with { Angle = 44.9f };

                JoinerSightRules.InView(view).ShouldBeTrue();
            }

            [Fact]
            public void Misses_a_thing_exactly_at_half_the_view()
            {
                var view = Facing with { Angle = 45f };

                JoinerSightRules.InView(view).ShouldBeFalse();
            }

            [Fact]
            public void Looks_behind_within_the_light()
            {
                var view = Facing with { Angle = 180f, Distance = 99.9f };

                JoinerSightRules.InView(view).ShouldBeTrue();
            }

            [Fact]
            public void Misses_behind_exactly_at_the_light()
            {
                var view = Facing with { Angle = 180f, Distance = 100f };

                JoinerSightRules.InView(view).ShouldBeFalse();
            }

            [Fact]
            public void Uses_ninety_degrees_with_no_host()
            {
                var view = Facing with
                {
                    HasHost = false,
                    HasLight = false,
                    HostFov = 360f,
                    Angle = 45f,
                };

                JoinerSightRules.InView(view).ShouldBeFalse();
            }

            [Fact]
            public void Uses_no_light_with_no_light()
            {
                var view = Facing with
                {
                    HasLight = false,
                    Angle = 180f,
                    Distance = 50f,
                };

                JoinerSightRules.InView(view).ShouldBeFalse();
            }

            [Fact]
            public void Uses_the_host_s_view()
            {
                var view = Facing with { HostFov = 120f, Angle = 59f };

                JoinerSightRules.InView(view).ShouldBeTrue();
            }
        }

        public class Nearest
        {
            static readonly StandInSpot Joiner = new StandInSpot
            {
                Alive = true,
                Distance = 300f,
            };

            [Fact]
            public void Gives_a_nearer_living_joiner()
            {
                var standIns = new List<StandInSpot> { Joiner };

                JoinerSightRules.Nearest(true, 500f, standIns).ShouldBe(300f);
            }

            [Fact]
            public void Gives_the_host_when_nearer()
            {
                var standIns = new List<StandInSpot> { Joiner };

                JoinerSightRules.Nearest(true, 100f, standIns).ShouldBe(100f);
            }

            [Fact]
            public void Skips_a_dead_joiner()
            {
                var dead = Joiner with { Alive = false };
                var standIns = new List<StandInSpot> { dead };

                JoinerSightRules.Nearest(true, 500f, standIns).ShouldBe(500f);
            }

            [Fact]
            public void Gives_the_largest_float_with_nobody()
            {
                var standIns = new List<StandInSpot>();

                var d = JoinerSightRules.Nearest(false, 5f, standIns);

                d.ShouldBe(float.MaxValue);
            }
        }

        public class Teleport
        {
            [Fact]
            public void Teleports_when_unseen_and_far()
            {
                var step = JoinerSightRules.Teleport(Unseen);

                step.ShouldBe(TeleportStep.Teleport);
            }

            [Fact]
            public void Leaves_it_to_the_game_when_off()
            {
                var spirit = Unseen with { On = false };

                var step = JoinerSightRules.Teleport(spirit);

                step.ShouldBe(TeleportStep.Game);
            }

            [Fact]
            public void Leaves_it_to_the_game_on_a_client()
            {
                var spirit = Unseen with { ClientMode = true };

                var step = JoinerSightRules.Teleport(spirit);

                step.ShouldBe(TeleportStep.Game);
            }

            [Fact]
            public void Skips_the_game_s_with_no_target()
            {
                var spirit = Unseen with { HasTarget = false };

                var step = JoinerSightRules.Teleport(spirit);

                step.ShouldBe(TeleportStep.Skip);
            }

            [Fact]
            public void Skips_the_game_s_when_seen()
            {
                var spirit = Unseen with { Seen = true };

                var step = JoinerSightRules.Teleport(spirit);

                step.ShouldBe(TeleportStep.Skip);
            }

            [Fact]
            public void Skips_the_game_s_exactly_at_the_far_limit()
            {
                var spirit = Unseen with { Nearest = 200f };

                var step = JoinerSightRules.Teleport(spirit);

                step.ShouldBe(TeleportStep.Skip);
            }

            [Fact]
            public void Teleports_just_past_the_far_limit()
            {
                var spirit = Unseen with { Nearest = 200.1f };

                var step = JoinerSightRules.Teleport(spirit);

                step.ShouldBe(TeleportStep.Teleport);
            }
        }

        public class SpotOf
        {
            [Fact]
            public void Lands_past_the_target_with_the_rolls()
            {
                var at = JoinerSightRules.SpotOf(Unseen);

                Parts(at).ShouldBe(new[] { -90f, 20f, 629f });
            }
        }

        public class BansheeGoesForJoiner
        {
            [Fact]
            public void Goes_for_a_joiner_who_sees_it()
            {
                var goes = JoinerSightRules.BansheeGoesForJoiner(SeenByJoiner);

                goes.ShouldBeTrue();
            }

            [Fact]
            public void Leaves_it_to_the_game_when_the_host_sees_it()
            {
                var banshee = SeenByJoiner with { HostSees = true };

                var goes = JoinerSightRules.BansheeGoesForJoiner(banshee);

                goes.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_it_to_the_game_when_no_joiner_sees_it()
            {
                var banshee = SeenByJoiner with { JoinerSees = false };

                var goes = JoinerSightRules.BansheeGoesForJoiner(banshee);

                goes.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_it_to_the_game_when_off()
            {
                var banshee = SeenByJoiner with { On = false };

                var goes = JoinerSightRules.BansheeGoesForJoiner(banshee);

                goes.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_it_to_the_game_on_a_client()
            {
                var banshee = SeenByJoiner with { ClientMode = true };

                var goes = JoinerSightRules.BansheeGoesForJoiner(banshee);

                goes.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_another_character_to_the_game()
            {
                var banshee = SeenByJoiner with { Banshee = false };

                var goes = JoinerSightRules.BansheeGoesForJoiner(banshee);

                goes.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_a_dead_banshee_to_the_game()
            {
                var banshee = SeenByJoiner with { Alive = false };

                var goes = JoinerSightRules.BansheeGoesForJoiner(banshee);

                goes.ShouldBeFalse();
            }
        }

        public class BansheeKeepsChasing
        {
            [Fact]
            public void Keeps_after_a_living_joiner()
            {
                var keeps = JoinerSightRules.BansheeKeepsChasing(AfterJoiner);

                keeps.ShouldBeTrue();
            }

            [Fact]
            public void Stops_for_a_dead_joiner()
            {
                var banshee = AfterJoiner with { TargetAlive = false };

                var keeps = JoinerSightRules.BansheeKeepsChasing(banshee);

                keeps.ShouldBeFalse();
            }

            [Fact]
            public void Stops_for_another_target()
            {
                var banshee = AfterJoiner with { TargetStandIn = false };

                var keeps = JoinerSightRules.BansheeKeepsChasing(banshee);

                keeps.ShouldBeFalse();
            }

            [Fact]
            public void Stops_with_no_target()
            {
                var banshee = AfterJoiner with { HasTarget = false };

                var keeps = JoinerSightRules.BansheeKeepsChasing(banshee);

                keeps.ShouldBeFalse();
            }

            [Fact]
            public void Stops_when_off()
            {
                var banshee = AfterJoiner with { On = false };

                var keeps = JoinerSightRules.BansheeKeepsChasing(banshee);

                keeps.ShouldBeFalse();
            }

            [Fact]
            public void Stops_on_a_client()
            {
                var banshee = AfterJoiner with { ClientMode = true };

                var keeps = JoinerSightRules.BansheeKeepsChasing(banshee);

                keeps.ShouldBeFalse();
            }
        }

        public class SeesFar
        {
            [Theory]
            [InlineData(499.9f, true)]
            [InlineData(500f, false)]
            public void Sees_far_when_nearer_than_the_far_sight(
                float distance,
                bool far)
            {
                JoinerSightRules.SeesFar(distance, 500f).ShouldBe(far);
            }
        }

        public class Approaches
        {
            [Theory]
            [InlineData(false, true)]
            [InlineData(true, false)]
            public void Approaches_unless_defensive(
                bool defensive,
                bool approaches)
            {
                JoinerSightRules.Approaches(defensive).ShouldBe(approaches);
            }
        }
    }
}
