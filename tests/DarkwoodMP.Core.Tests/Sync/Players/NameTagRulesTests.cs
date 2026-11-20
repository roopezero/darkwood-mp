using DarkwoodMP.Sync.Players;

namespace DarkwoodMP.Tests.Sync.Players
{
    public class NameTagRulesTests
    {
        /// A living player at 80%.
        static readonly LabelFacts Fighter = new LabelFacts
        {
            Name = "Fighter",
            HasLast = true,
            Alive = true,
            Health = 80,
        };

        /// The dummy, hit for 12.4 at 9, so flashing until 10.5.
        static readonly LabelFacts Dummy = new LabelFacts
        {
            Name = "Dummy",
            Dummy = true,
            HasLast = true,
            Alive = true,
            Health = 100,
            Now = new Seconds(10f),
            FlashUntil = new Seconds(10.5f),
            Damage = 12.4f,
        };

        public class Limits
        {
            [Fact]
            public void Pins_the_flash_time()
            {
                var time = NameTagRules.FlashTime;

                time.ShouldBe(1.5f);
            }
        }

        public class Where
        {
            [Fact]
            public void Draws_above_the_puppets_in_play()
            {
                var layer = NameTagRules.Where(new TagFacts());

                layer.ShouldBe(TagLayer.World);
            }

            [Fact]
            public void Draws_nothing_over_a_menu()
            {
                var screen = new TagFacts { MenuOver = true, MapOpen = true };

                var layer = NameTagRules.Where(screen);

                layer.ShouldBe(TagLayer.None);
            }

            [Fact]
            public void Draws_on_the_map_while_its_open()
            {
                var screen = new TagFacts
                {
                    MapOpen = true,
                    OverlaysHidden = true,
                };

                var layer = NameTagRules.Where(screen);

                layer.ShouldBe(TagLayer.Map);
            }

            [Fact]
            public void Draws_nothing_while_overlays_are_hidden()
            {
                var screen = new TagFacts { OverlaysHidden = true };

                var layer = NameTagRules.Where(screen);

                layer.ShouldBe(TagLayer.None);
            }
        }

        public class Draws
        {
            [Theory]
            [InlineData(true, 0f, true)]
            [InlineData(true, 5f, true)]
            [InlineData(true, -0.01f, false)]
            [InlineData(false, 5f, false)]
            public void Draws_a_shown_puppet_in_front_of_the_camera(
                bool shown,
                float depth,
                bool draws)
            {
                var result = NameTagRules.Draws(shown, depth);

                result.ShouldBe(draws);
            }
        }

        public class RefreshesName
        {
            [Fact]
            public void Reads_a_players_name_again()
            {
                var refreshes = NameTagRules.RefreshesName(5);

                refreshes.ShouldBeTrue();
            }

            [Fact]
            public void Keeps_the_dummys_name()
            {
                var refreshes = NameTagRules.RefreshesName(
                    PuppetRules.DummyId);

                refreshes.ShouldBeFalse();
            }
        }

        public class FlashUntil
        {
            [Fact]
            public void Flashes_for_a_second_and_a_half()
            {
                var until = NameTagRules.FlashUntil(new Seconds(9f));

                until.Value.ShouldBe(10.5f);
            }
        }

        public class Label
        {
            [Fact]
            public void Shows_the_name_and_health()
            {
                var tag = NameTagRules.Label(Fighter);

                tag.Text.ShouldBe("Fighter  80%");
            }

            [Fact]
            public void Shows_no_health_for_a_dead_player()
            {
                var facts = Fighter with { Alive = false };

                var tag = NameTagRules.Label(facts);

                tag.Text.ShouldBe("Fighter");
            }

            [Fact]
            public void Shows_no_health_before_a_snapshot()
            {
                var facts = Fighter with { HasLast = false };

                var tag = NameTagRules.Label(facts);

                tag.Text.ShouldBe("Fighter");
            }

            [Fact]
            public void Keeps_a_players_damage_total_alone()
            {
                var tag = NameTagRules.Label(Fighter);

                tag.ResetsDamage.ShouldBeFalse();
            }

            [Fact]
            public void Shows_the_dummys_damage_while_it_flashes()
            {
                var tag = NameTagRules.Label(Dummy);

                tag.Text.ShouldBe("Dummy  100%  -12");
            }

            [Fact]
            public void Keeps_the_dummys_total_while_it_flashes()
            {
                var tag = NameTagRules.Label(Dummy);

                tag.ResetsDamage.ShouldBeFalse();
            }

            [Fact]
            public void Hides_the_damage_when_the_flash_ends()
            {
                var facts = Dummy with { Now = new Seconds(10.5f) };

                var tag = NameTagRules.Label(facts);

                tag.Text.ShouldBe("Dummy  100%");
            }

            [Fact]
            public void Resets_the_dummys_total_when_the_flash_ends()
            {
                var facts = Dummy with { Now = new Seconds(10.5f) };

                var tag = NameTagRules.Label(facts);

                tag.ResetsDamage.ShouldBeTrue();
            }
        }
    }
}
