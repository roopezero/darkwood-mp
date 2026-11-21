using DarkwoodMP.World.Skills;

namespace DarkwoodMP.Tests.World.Skills
{
    public class PanelPressTests
    {
        const int Left = 1;
        const int Right = 2;
        const int Middle = 4;

        static MouseFacts Frame(int frame) => new MouseFacts { Frame = frame };

        public class Limits
        {
            [Fact]
            public void Pins_three_buttons()
            {
                PanelPress.Buttons.ShouldBe(3);
            }
        }

        public class Due
        {
            [Fact]
            public void Is_due_before_any_frame()
            {
                var press = new PanelPress();

                var due = press.Due(0);

                due.ShouldBeTrue();
            }

            [Fact]
            public void Isnt_due_twice_in_a_frame()
            {
                var press = new PanelPress();
                press.Note(Frame(5));

                var due = press.Due(5);

                due.ShouldBeFalse();
            }

            [Fact]
            public void Is_due_in_the_next_frame()
            {
                var press = new PanelPress();
                press.Note(Frame(5));

                var due = press.Due(6);

                due.ShouldBeTrue();
            }
        }

        public class Eats
        {
            [Fact]
            public void Leaves_a_press_alone_off_the_panel()
            {
                var press = new PanelPress();
                press.Note(Frame(1) with { Down = Left, Held = Left });

                var eats = press.Eats(true);

                eats.ShouldBeFalse();
            }

            [Fact]
            public void Eats_a_press_over_the_panel()
            {
                var press = new PanelPress();
                press.Note(Frame(1) with { Over = true });

                var eats = press.Eats(true);

                eats.ShouldBeTrue();
            }

            [Fact]
            public void Leaves_a_held_read_over_the_panel_with_no_press()
            {
                var press = new PanelPress();
                press.Note(Frame(1) with { Over = true });

                var eats = press.Eats(false);

                eats.ShouldBeFalse();
            }

            [Theory]
            [InlineData(Left)]
            [InlineData(Right)]
            [InlineData(Middle)]
            public void Eats_a_button_pressed_on_the_panel_while_held(int bit)
            {
                var press = new PanelPress();
                press.Note(
                    Frame(1) with { Over = true, Down = bit, Held = bit });
                press.Note(Frame(2) with { Held = bit });

                var eats = press.Eats(false);

                eats.ShouldBeTrue();
            }

            [Fact]
            public void Lets_go_once_the_button_is_up()
            {
                var press = new PanelPress();
                press.Note(
                    Frame(1) with { Over = true, Down = Left, Held = Left });
                press.Note(Frame(2));

                var eats = press.Eats(false);

                eats.ShouldBeFalse();
            }

            [Fact]
            public void Ignores_a_second_read_in_the_same_frame()
            {
                var press = new PanelPress();
                press.Note(
                    Frame(1) with { Over = true, Down = Left, Held = Left });
                press.Note(Frame(1));

                var eats = press.Eats(false);

                eats.ShouldBeTrue();
            }

            [Fact]
            public void Keeps_a_press_that_began_in_the_world_unmarked()
            {
                var press = new PanelPress();
                press.Note(Frame(1) with { Down = Left, Held = Left });
                press.Note(Frame(2) with { Over = true, Held = Left });

                var eats = press.Eats(false);

                eats.ShouldBeFalse();
            }
        }
    }
}
