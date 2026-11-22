using DarkwoodMP.Sync.Players;

namespace DarkwoodMP.Tests.Sync.Players
{
    public class PuppetRulesTests
    {
        /// A living player with a torch, a lantern and the flashlight on.
        static readonly PuppetFacts Lit = new PuppetFacts
        {
            HasLast = true,
            Alive = true,
            Flashlight = true,
            HeldLight = "torch",
            Lantern = 400f,
            BeamRadius = 900f,
        };

        static readonly Vec2 Origin = new Vec2(0f, 0f);

        public class Limits
        {
            [Fact]
            public void Pins_the_jump_distance()
            {
                var jump = PuppetRules.JumpDistance;

                jump.ShouldBe(300f);
            }

            [Fact]
            public void Pins_the_dummys_id()
            {
                var id = PuppetRules.DummyId;

                id.ShouldBe(18446744073709551614UL);
            }
        }

        public class IsAlive
        {
            [Fact]
            public void Counts_a_puppet_alive_before_its_first_snapshot()
            {
                var puppet = new PuppetFacts { Alive = false };

                var alive = PuppetRules.IsAlive(puppet);

                alive.ShouldBeTrue();
            }

            [Fact]
            public void Takes_the_last_snapshots_word()
            {
                var puppet = Lit with { Alive = false };

                var alive = PuppetRules.IsAlive(puppet);

                alive.ShouldBeFalse();
            }
        }

        public class IsSpectating
        {
            [Fact]
            public void Isnt_a_spectator_before_its_first_snapshot()
            {
                var puppet = new PuppetFacts { Spectating = true };

                var spectating = PuppetRules.IsSpectating(puppet);

                spectating.ShouldBeFalse();
            }

            [Fact]
            public void Takes_the_last_snapshots_word()
            {
                var puppet = Lit with { Spectating = true };

                var spectating = PuppetRules.IsSpectating(puppet);

                spectating.ShouldBeTrue();
            }
        }

        public class IsAway
        {
            [Fact]
            public void Isnt_away_before_its_first_snapshot()
            {
                var puppet = new PuppetFacts { Away = true };

                var away = PuppetRules.IsAway(puppet);

                away.ShouldBeFalse();
            }

            [Fact]
            public void Takes_the_last_snapshots_word()
            {
                var puppet = Lit with { Away = true };

                var away = PuppetRules.IsAway(puppet);

                away.ShouldBeTrue();
            }
        }

        public class Died
        {
            [Theory]
            [InlineData(true, true, false, true)]
            [InlineData(true, true, true, false)]
            [InlineData(true, false, false, false)]
            [InlineData(false, true, false, false)]
            public void Dies_only_from_alive_to_dead(
                bool hadLast,
                bool wasAlive,
                bool alive,
                bool died)
            {
                var result = PuppetRules.Died(hadLast, wasAlive, alive);

                result.ShouldBe(died);
            }
        }

        public class Jumps
        {
            [Fact]
            public void Slides_a_move_of_exactly_the_jump_distance()
            {
                var next = new Vec2(300f, 0f);

                var jumps = PuppetRules.Jumps(true, Origin, next);

                jumps.ShouldBeFalse();
            }

            [Fact]
            public void Jumps_a_move_just_past_it()
            {
                var next = new Vec2(300.1f, 0f);

                var jumps = PuppetRules.Jumps(true, Origin, next);

                jumps.ShouldBeTrue();
            }

            [Fact]
            public void Measures_on_the_ground_plane()
            {
                var next = new Vec2(0f, 301f);

                var jumps = PuppetRules.Jumps(true, Origin, next);

                jumps.ShouldBeTrue();
            }

            [Fact]
            public void Doesnt_jump_from_no_snapshot()
            {
                var next = new Vec2(5000f, 0f);

                var jumps = PuppetRules.Jumps(false, Origin, next);

                jumps.ShouldBeFalse();
            }
        }

        public class KeepsPrevious
        {
            [Fact]
            public void Keeps_the_last_snapshot_to_blend_from()
            {
                var next = new Vec2(10f, 0f);

                var keeps = PuppetRules.KeepsPrevious(true, Origin, next);

                keeps.ShouldBeTrue();
            }

            [Fact]
            public void Drops_it_on_a_jump()
            {
                var next = new Vec2(301f, 0f);

                var keeps = PuppetRules.KeepsPrevious(true, Origin, next);

                keeps.ShouldBeFalse();
            }

            [Fact]
            public void Keeps_none_for_the_first_snapshot()
            {
                var next = new Vec2(10f, 0f);

                var keeps = PuppetRules.KeepsPrevious(false, Origin, next);

                keeps.ShouldBeFalse();
            }
        }

        public class Blend
        {
            /// Snapshots at 1.0 and 1.1, drawn at 1.15.
            static readonly BlendFacts Halfway = new BlendFacts
            {
                HasPrevious = true,
                Previous = new Seconds(1f),
                Last = new Seconds(1.1f),
                Now = new Seconds(1.15f),
            };

            [Fact]
            public void Blends_over_the_time_the_last_took()
            {
                var blend = PuppetRules.Blend(Halfway);

                blend.Fraction.ShouldBe(0.5f, 1e-4f);
            }

            [Fact]
            public void Stops_at_the_last_snapshot()
            {
                var times = Halfway with { Now = new Seconds(5f) };

                var blend = PuppetRules.Blend(times);

                blend.Fraction.ShouldBe(1f);
            }

            [Fact]
            public void Starts_at_the_previous_snapshot()
            {
                var times = Halfway with { Now = new Seconds(0f) };

                var blend = PuppetRules.Blend(times);

                blend.Fraction.ShouldBe(0f);
            }

            [Fact]
            public void Draws_the_last_as_it_is_without_a_previous()
            {
                var times = Halfway with { HasPrevious = false };

                var blend = PuppetRules.Blend(times);

                blend.Blends.ShouldBeFalse();
            }

            [Fact]
            public void Draws_the_last_as_it_is_when_both_came_at_once()
            {
                var times = Halfway with { Last = new Seconds(1f) };

                var blend = PuppetRules.Blend(times);

                blend.Blends.ShouldBeFalse();
            }
        }

        public class Shown
        {
            [Theory]
            [InlineData(false, true, true)]
            [InlineData(true, true, false)]
            [InlineData(false, false, false)]
            public void Shows_a_player_in_our_place(
                bool spectating,
                bool same,
                bool shown)
            {
                var result = PuppetRules.Shown(spectating, same);

                result.ShouldBe(shown);
            }
        }

        public class HeldLight
        {
            [Fact]
            public void Shows_their_held_light()
            {
                var held = PuppetRules.HeldLight(Lit);

                held.ShouldBe("torch");
            }

            [Fact]
            public void Shows_it_while_away()
            {
                var puppet = Lit with { Away = true };

                var held = PuppetRules.HeldLight(puppet);

                held.ShouldBe("torch");
            }

            [Fact]
            public void Shows_none_on_a_dead_player()
            {
                var puppet = Lit with { Alive = false };

                var held = PuppetRules.HeldLight(puppet);

                held.ShouldBe("");
            }

            [Fact]
            public void Shows_none_on_a_spectator()
            {
                var puppet = Lit with { Spectating = true };

                var held = PuppetRules.HeldLight(puppet);

                held.ShouldBe("");
            }

            [Fact]
            public void Shows_none_for_no_type()
            {
                var puppet = Lit with { HeldLight = null };

                var held = PuppetRules.HeldLight(puppet);

                held.ShouldBe("");
            }
        }

        public class RedoesHeld
        {
            [Theory]
            [InlineData("lamp", "torch", true, true)]
            [InlineData("torch", "torch", true, false)]
            [InlineData("torch", "torch", false, true)]
            [InlineData("", "", false, false)]
            [InlineData("", "torch", true, true)]
            public void Redoes_a_changed_or_lost_light(
                string want,
                string shown,
                bool has,
                bool redoes)
            {
                var result = PuppetRules.RedoesHeld(want, shown, has);

                result.ShouldBe(redoes);
            }
        }

        public class Beam
        {
            [Fact]
            public void Shows_their_beam()
            {
                var beam = PuppetRules.Beam(Lit);

                beam.ShouldBeTrue();
            }

            [Fact]
            public void Shows_none_with_the_flashlight_off()
            {
                var puppet = Lit with { Flashlight = false };

                var beam = PuppetRules.Beam(puppet);

                beam.ShouldBeFalse();
            }

            [Fact]
            public void Shows_none_on_a_dead_player()
            {
                var puppet = Lit with { Alive = false };

                var beam = PuppetRules.Beam(puppet);

                beam.ShouldBeFalse();
            }

            [Fact]
            public void Shows_none_on_a_spectator()
            {
                var puppet = Lit with { Spectating = true };

                var beam = PuppetRules.Beam(puppet);

                beam.ShouldBeFalse();
            }
        }

        public class SetsBeam
        {
            [Fact]
            public void Sets_a_radius_they_sent()
            {
                var sets = PuppetRules.SetsBeam(Lit);

                sets.ShouldBeTrue();
            }

            [Fact]
            public void Keeps_our_beams_look_for_a_radius_of_0()
            {
                var puppet = Lit with { BeamRadius = 0f };

                var sets = PuppetRules.SetsBeam(puppet);

                sets.ShouldBeFalse();
            }
        }

        public class Lantern
        {
            [Fact]
            public void Shows_their_lantern()
            {
                var radius = PuppetRules.Lantern(Lit);

                radius.ShouldBe(400f);
            }

            [Fact]
            public void Shows_none_while_away()
            {
                var puppet = Lit with { Away = true };

                var radius = PuppetRules.Lantern(puppet);

                radius.ShouldBe(0f);
            }

            [Fact]
            public void Shows_none_on_a_dead_player()
            {
                var puppet = Lit with { Alive = false };

                var radius = PuppetRules.Lantern(puppet);

                radius.ShouldBe(0f);
            }

            [Fact]
            public void Shows_none_on_a_spectator()
            {
                var puppet = Lit with { Spectating = true };

                var radius = PuppetRules.Lantern(puppet);

                radius.ShouldBe(0f);
            }
        }

        public class RemovesLantern
        {
            [Theory]
            [InlineData(0f, true)]
            [InlineData(-1f, true)]
            [InlineData(0.01f, false)]
            public void Removes_it_at_0_or_less(float want, bool removes)
            {
                var result = PuppetRules.RemovesLantern(want);

                result.ShouldBe(removes);
            }
        }
    }
}
