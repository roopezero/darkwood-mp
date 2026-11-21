using DarkwoodMP.Combat.Beams;

namespace DarkwoodMP.Tests.Combat.Beams
{
    public class BeamRulesTests
    {
        /// A puppet at the origin, facing +z.
        static readonly Vec3 Origin = new Vec3(0f, 0f, 0f);

        static readonly Vec3 North = new Vec3(0f, 0f, 1f);

        /// Living shadow armour on a hosting game with one stand-in.
        static readonly ArmorFacts Armour = new ArmorFacts
        {
            Exists = true,
            Health = 10f,
            StandIns = 1,
            Hosting = true,
        };

        /// A living joiner with the flashlight on, not spectating.
        static readonly BeamOwnerFacts Torch = new BeamOwnerFacts
        {
            StandInAlive = true,
            Root = true,
            HasSnapshot = true,
            Flashlight = true,
            Alive = true,
        };

        static bool In(float x, float y, float z) =>
            BeamRules.InBeam(Origin, North, new Vec3(x, y, z)).In;

        static float[] Parts(Vec3 v) => new[] { v.X, v.Y, v.Z };

        public class Limits
        {
            [Fact]
            public void Pins_the_beam_limits()
            {
                var limits = new[]
                {
                    BeamRules.MaxBeam,
                    BeamRules.Behind,
                    BeamRules.BaseWidth,
                    BeamRules.Widening,
                    BeamRules.MinWidth,
                    BeamRules.MinFacing,
                    BeamRules.BoxAhead,
                    BeamRules.BoxSide,
                    BeamRules.BoxTurned,
                    BeamRules.BoxHeight,
                };

                limits.ShouldBe(new[]
                {
                    1500f, 50f, 50f, 0.5f, 100f, 0.01f, 500f, 1000f, 1.42f,
                    10000f,
                });
            }

            [Fact]
            public void Pins_the_puppet_root_s_name()
            {
                BeamRules.PuppetPrefix.ShouldBe("DarkwoodMP_Remote_");
            }
        }

        public class InBeam
        {
            [Theory]
            [InlineData(0f, 1000f, true)]
            [InlineData(0f, 1500f, true)]
            [InlineData(0f, 1500.1f, false)]
            [InlineData(0f, -50f, true)]
            [InlineData(0f, -50.1f, false)]
            [InlineData(100f, 0f, true)]
            [InlineData(100.1f, 0f, false)]
            [InlineData(-100f, 0f, true)]
            [InlineData(550f, 1000f, true)]
            [InlineData(550.1f, 1000f, false)]
            public void Lights_a_widening_wedge_ahead(
                float x,
                float z,
                bool lit)
            {
                In(x, 0f, z).ShouldBe(lit);
            }

            [Fact]
            public void Ignores_height()
            {
                In(0f, 5000f, 1000f).ShouldBeTrue();
            }

            [Fact]
            public void Measures_ahead_and_aside()
            {
                var spot = BeamRules.InBeam(
                    Origin,
                    North,
                    new Vec3(300f, 0f, 400f));

                new[] { spot.Ahead, spot.Aside }.ShouldBe(
                    new[] { 400f, 300f },
                    0.001f);
            }

            [Fact]
            public void Faces_along_the_ground_whatever_the_up_s_height()
            {
                var up = new Vec3(0f, 5f, 1f);

                var spot = BeamRules.InBeam(Origin, up, new Vec3(0f, 0f, 9f));

                spot.Ahead.ShouldBe(9f, 0.001f);
            }

            [Fact]
            public void Lights_nothing_facing_nowhere_on_the_ground()
            {
                var up = new Vec3(0f, 1f, 0f);

                var spot = BeamRules.InBeam(Origin, up, new Vec3(0f, 0f, 9f));

                spot.ShouldBe(new BeamSpot());
            }

            [Fact]
            public void Faces_exactly_at_the_least_facing()
            {
                var up = new Vec3(0.1f, 0f, 0f);

                var spot = BeamRules.InBeam(Origin, up, new Vec3(9f, 0f, 0f));

                spot.In.ShouldBeTrue();
            }

            [Fact]
            public void Faces_nowhere_just_below_the_least_facing()
            {
                var up = new Vec3(0.0999f, 0f, 0f);

                var spot = BeamRules.InBeam(Origin, up, new Vec3(9f, 0f, 0f));

                spot.In.ShouldBeFalse();
            }
        }

        public class LightsBeam
        {
            [Fact]
            public void Lights_with_the_flashlight_on()
            {
                BeamRules.LightsBeam(Torch).ShouldBeTrue();
            }

            [Fact]
            public void Lights_nothing_for_a_dead_stand_in()
            {
                var owner = Torch with { StandInAlive = false };

                BeamRules.LightsBeam(owner).ShouldBeFalse();
            }

            [Fact]
            public void Lights_nothing_with_no_puppet_root()
            {
                var owner = Torch with { Root = false };

                BeamRules.LightsBeam(owner).ShouldBeFalse();
            }

            [Fact]
            public void Lights_nothing_with_no_state_yet()
            {
                var owner = Torch with { HasSnapshot = false };

                BeamRules.LightsBeam(owner).ShouldBeFalse();
            }

            [Fact]
            public void Lights_nothing_with_the_flashlight_off()
            {
                var owner = Torch with { Flashlight = false };

                BeamRules.LightsBeam(owner).ShouldBeFalse();
            }

            [Fact]
            public void Lights_nothing_for_a_dead_player()
            {
                var owner = Torch with { Alive = false };

                BeamRules.LightsBeam(owner).ShouldBeFalse();
            }

            [Fact]
            public void Lights_nothing_for_a_spectator()
            {
                var owner = Torch with { Spectating = true };

                BeamRules.LightsBeam(owner).ShouldBeFalse();
            }
        }

        public class ChecksArmour
        {
            [Fact]
            public void Checks_living_armour_on_the_host()
            {
                BeamRules.ChecksArmour(Armour).ShouldBeTrue();
            }

            [Fact]
            public void Skips_no_armour()
            {
                var armour = Armour with { Exists = false };

                BeamRules.ChecksArmour(armour).ShouldBeFalse();
            }

            [Theory]
            [InlineData(0f)]
            [InlineData(-1f)]
            [InlineData(float.NaN)]
            public void Skips_armour_with_no_health(float health)
            {
                var armour = Armour with { Health = health };

                BeamRules.ChecksArmour(armour).ShouldBeFalse();
            }

            [Fact]
            public void Skips_with_no_stand_ins()
            {
                var armour = Armour with { StandIns = 0 };

                BeamRules.ChecksArmour(armour).ShouldBeFalse();
            }

            [Fact]
            public void Skips_when_not_hosting()
            {
                var armour = Armour with { Hosting = false };

                BeamRules.ChecksArmour(armour).ShouldBeFalse();
            }
        }

        public class HurtsArmour
        {
            [Theory]
            [InlineData(false, false, true)]
            [InlineData(true, false, false)]
            [InlineData(false, true, false)]
            public void Hurts_only_where_the_game_did_not(
                bool lit,
                bool burning,
                bool hurts)
            {
                BeamRules.HurtsArmour(lit, burning).ShouldBe(hurts);
            }
        }

        public class ArmourDamage
        {
            [Fact]
            public void Multiplies_light_damage_by_its_modifier_and_rate()
            {
                BeamRules.ArmourDamage(2f, 3f, 0.5f).ShouldBe(3f);
            }
        }

        public class ArmourTurns
        {
            [Theory]
            [InlineData(true, true, false, true)]
            [InlineData(false, true, false, false)]
            [InlineData(true, false, false, false)]
            [InlineData(true, true, true, false)]
            public void Turns_a_living_character_that_is_no_dummy(
                bool character,
                bool alive,
                bool dummy,
                bool turns)
            {
                var result = BeamRules.ArmourTurns(character, alive, dummy);

                result.ShouldBe(turns);
            }
        }

        public class IsPuppetLight
        {
            [Fact]
            public void Finds_a_light_on_a_puppet_root()
            {
                var name = "DarkwoodMP_Remote_76561198000000000";

                BeamRules.IsPuppetLight(true, name).ShouldBeTrue();
            }

            [Fact]
            public void Leaves_the_host_s_lights()
            {
                BeamRules.IsPuppetLight(true, "Flashlight").ShouldBeFalse();
            }

            [Fact]
            public void Leaves_a_light_with_no_parent()
            {
                BeamRules.IsPuppetLight(false, null).ShouldBeFalse();
            }
        }

        public class PuppetBox
        {
            static readonly Vec3 Parent = new Vec3(10f, 20f, 30f);

            [Fact]
            public void Centres_the_box_ahead_of_the_puppet()
            {
                var up = new Vec3(0f, 0f, 2f);

                var box = BeamRules.PuppetBox(Parent, up);

                Parts(box.Center).ShouldBe(new[] { 10f, 20f, 530f });
            }

            [Fact]
            public void Sizes_the_box_to_cover_any_turn()
            {
                var box = BeamRules.PuppetBox(Parent, North);

                Parts(box.Size).ShouldBe(new[] { 1420f, 10000f, 1420f });
            }

            [Fact]
            public void Replaces_the_game_s_box()
            {
                var box = BeamRules.PuppetBox(Parent, North);

                box.Changed.ShouldBeTrue();
            }

            [Fact]
            public void Leaves_the_game_s_box_facing_nowhere()
            {
                var up = new Vec3(0f, 1f, 0f);

                var box = BeamRules.PuppetBox(Parent, up);

                box.Changed.ShouldBeFalse();
            }
        }
    }
}
