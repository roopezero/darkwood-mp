using DarkwoodMP.Sync.Players;

namespace DarkwoodMP.Tests.Sync.Players
{
    public class PlayerLookRulesTests
    {
        public class Limits
        {
            [Fact]
            public void Pins_the_capture_limits()
            {
                var limits = new[]
                {
                    (float)PlayerLookRules.MaxSprites,
                    PlayerLookRules.RescanEvery,
                };

                var pinned = new[] { 255f, 1f };

                limits.ShouldBe(pinned);
            }
        }

        public class IsCharacterArt
        {
            [Theory]
            [InlineData(".")]
            [InlineData("PlayerLegs")]
            [InlineData("PlayerLegs/Feet")]
            [InlineData("Shadow")]
            public void Mirrors_the_body_legs_and_shadow(string path)
            {
                var art = PlayerLookRules.IsCharacterArt(path);

                art.ShouldBeTrue();
            }

            [Theory]
            [InlineData("FOV")]
            [InlineData("PlayerLegsX")]
            [InlineData("Shadow/Child")]
            [InlineData("Cursor")]
            [InlineData("")]
            public void Skips_the_rest_of_the_player(string path)
            {
                var art = PlayerLookRules.IsCharacterArt(path);

                art.ShouldBeFalse();
            }
        }

        public class Rescans
        {
            [Fact]
            public void Rescans_another_player_object_at_once()
            {
                var rescans = PlayerLookRules.Rescans(
                    false, new Seconds(0f), new Seconds(5f));

                rescans.ShouldBeTrue();
            }

            [Fact]
            public void Rescans_exactly_when_due()
            {
                var rescans = PlayerLookRules.Rescans(
                    true, new Seconds(5f), new Seconds(5f));

                rescans.ShouldBeTrue();
            }

            [Fact]
            public void Waits_until_due()
            {
                var rescans = PlayerLookRules.Rescans(
                    true, new Seconds(4.99f), new Seconds(5f));

                rescans.ShouldBeFalse();
            }
        }

        public class NextRescan
        {
            [Fact]
            public void Waits_a_second()
            {
                var next = PlayerLookRules.NextRescan(new Seconds(2f));

                next.Value.ShouldBe(3f);
            }
        }

        public class SendsSprite
        {
            static readonly SpriteFacts Shown = new SpriteFacts
            {
                Exists = true,
                Active = true,
                HasCollection = true,
                HasRenderer = true,
                RendererEnabled = true,
            };

            [Fact]
            public void Sends_a_shown_sprite()
            {
                var sends = PlayerLookRules.SendsSprite(Shown);

                sends.ShouldBeTrue();
            }

            [Fact]
            public void Skips_a_destroyed_sprite()
            {
                var sprite = Shown with { Exists = false };

                var sends = PlayerLookRules.SendsSprite(sprite);

                sends.ShouldBeFalse();
            }

            [Fact]
            public void Skips_an_inactive_sprite()
            {
                var sprite = Shown with { Active = false };

                var sends = PlayerLookRules.SendsSprite(sprite);

                sends.ShouldBeFalse();
            }

            [Fact]
            public void Skips_a_sprite_without_a_collection()
            {
                var sprite = Shown with { HasCollection = false };

                var sends = PlayerLookRules.SendsSprite(sprite);

                sends.ShouldBeFalse();
            }

            [Fact]
            public void Skips_a_sprite_without_a_renderer()
            {
                var sprite = Shown with { HasRenderer = false };

                var sends = PlayerLookRules.SendsSprite(sprite);

                sends.ShouldBeFalse();
            }

            [Fact]
            public void Skips_a_sprite_whose_renderer_is_off()
            {
                var sprite = Shown with { RendererEnabled = false };

                var sends = PlayerLookRules.SendsSprite(sprite);

                sends.ShouldBeFalse();
            }
        }

        public class Alive
        {
            [Theory]
            [InlineData(true, false, true)]
            [InlineData(true, true, false)]
            [InlineData(false, false, false)]
            public void Counts_a_dying_player_as_dead(
                bool alive,
                bool dying,
                bool said)
            {
                var result = PlayerLookRules.Alive(alive, dying);

                result.ShouldBe(said);
            }
        }

        public class HeldLight
        {
            static readonly HeldFacts Torch = new HeldFacts
            {
                HasLightEmitter = true,
                HeldValid = true,
                HasBaseClass = true,
                HeldType = "torch",
            };

            [Fact]
            public void Sends_the_type_of_a_light_in_hand()
            {
                var held = PlayerLookRules.HeldLight(Torch);

                held.ShouldBe("torch");
            }

            [Fact]
            public void Sends_none_without_an_item_light()
            {
                var facts = Torch with { HasLightEmitter = false };

                var held = PlayerLookRules.HeldLight(facts);

                held.ShouldBe("");
            }

            [Fact]
            public void Sends_none_with_an_empty_hand()
            {
                var facts = Torch with { HeldValid = false };

                var held = PlayerLookRules.HeldLight(facts);

                held.ShouldBe("");
            }

            [Fact]
            public void Sends_none_for_an_item_without_a_base_class()
            {
                var facts = Torch with { HasBaseClass = false };

                var held = PlayerLookRules.HeldLight(facts);

                held.ShouldBe("");
            }

            [Fact]
            public void Sends_an_empty_type_for_none()
            {
                var facts = Torch with { HeldType = null };

                var held = PlayerLookRules.HeldLight(facts);

                held.ShouldBe("");
            }
        }

        public class Flames
        {
            static readonly Vec3 Offset = new Vec3(1f, 2f, 3f);

            [Fact]
            public void Sends_the_flames_of_a_held_light()
            {
                var at = PlayerLookRules.Flames("torch", true, Offset);

                at.ShouldBe(Offset);
            }

            [Fact]
            public void Sends_zero_without_a_held_light()
            {
                var at = PlayerLookRules.Flames("", true, Offset);

                at.ShouldBe(default(Vec3));
            }

            [Fact]
            public void Sends_zero_without_an_emitter()
            {
                var at = PlayerLookRules.Flames("torch", false, Offset);

                at.ShouldBe(default(Vec3));
            }
        }

        public class Lantern
        {
            static readonly LanternFacts Lit = new LanternFacts
            {
                HasDot = true,
                LightsPlayer = true,
                HasController = true,
                IsLogicLight = true,
                Radius = 400f,
            };

            [Fact]
            public void Sends_the_radius_of_a_lit_dot()
            {
                var radius = PlayerLookRules.Lantern(Lit);

                radius.ShouldBe(400f);
            }

            [Fact]
            public void Sends_none_without_a_dot()
            {
                var dot = Lit with { HasDot = false };

                var radius = PlayerLookRules.Lantern(dot);

                radius.ShouldBe(0f);
            }

            [Fact]
            public void Sends_none_when_the_dot_lights_nothing()
            {
                var dot = Lit with { LightsPlayer = false };

                var radius = PlayerLookRules.Lantern(dot);

                radius.ShouldBe(0f);
            }

            [Fact]
            public void Sends_none_without_a_controller()
            {
                var dot = Lit with { HasController = false };

                var radius = PlayerLookRules.Lantern(dot);

                radius.ShouldBe(0f);
            }

            [Fact]
            public void Sends_none_when_it_isnt_a_logic_light()
            {
                var dot = Lit with { IsLogicLight = false };

                var radius = PlayerLookRules.Lantern(dot);

                radius.ShouldBe(0f);
            }
        }

        public class Beam
        {
            static readonly BeamFacts On = new BeamFacts
            {
                On = true,
                HasLight = true,
                Radius = 900f,
                Grey = 0.5f,
            };

            [Fact]
            public void Sends_the_radius_of_a_beam_that_is_on()
            {
                var radius = PlayerLookRules.BeamRadius(On);

                radius.ShouldBe(900f);
            }

            [Fact]
            public void Sends_the_grey_of_a_beam_that_is_on()
            {
                var grey = PlayerLookRules.BeamGrey(On);

                grey.ShouldBe(0.5f);
            }

            [Fact]
            public void Sends_no_radius_when_the_flashlight_is_off()
            {
                var beam = On with { On = false };

                var radius = PlayerLookRules.BeamRadius(beam);

                radius.ShouldBe(0f);
            }

            [Fact]
            public void Sends_no_grey_without_a_light()
            {
                var beam = On with { HasLight = false };

                var grey = PlayerLookRules.BeamGrey(beam);

                grey.ShouldBe(0f);
            }
        }

        public class HealthPercent
        {
            [Theory]
            [InlineData(50f, 100f, 50)]
            [InlineData(100f, 100f, 100)]
            [InlineData(150f, 100f, 100)]
            [InlineData(-5f, 100f, 0)]
            [InlineData(1f, 8f, 12)]
            [InlineData(3f, 8f, 38)]
            [InlineData(50f, 0f, 0)]
            [InlineData(50f, -1f, 0)]
            public void Rounds_halves_to_even_within_0_to_100(
                float health,
                float max,
                int percent)
            {
                var result = PlayerLookRules.HealthPercent(health, max);

                ((int)result).ShouldBe(percent);
            }
        }

        public class Away
        {
            [Theory]
            [InlineData(false, false, false)]
            [InlineData(true, false, true)]
            [InlineData(false, true, true)]
            public void Is_away_outside_or_dreaming(
                bool outside,
                bool dreaming,
                bool away)
            {
                var result = PlayerLookRules.Away(outside, dreaming);

                result.ShouldBe(away);
            }
        }
    }
}
