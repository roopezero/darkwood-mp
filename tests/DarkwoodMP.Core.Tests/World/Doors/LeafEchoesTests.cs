using DarkwoodMP.World.Doors;

namespace DarkwoodMP.Tests.World.Doors
{
    public class LeafEchoesTests
    {
        static LeafEchoes Sending(params float[] angles)
        {
            var echoes = new LeafEchoes();
            foreach (var angle in angles)
            {
                echoes.Sent(angle);
            }

            return echoes;
        }

        public class Limits
        {
            [Fact]
            public void Pins_the_longest_list()
            {
                LeafEchoes.MaxKept.ShouldBe(64);
            }

            [Fact]
            public void Pins_the_match()
            {
                LeafEchoes.Match.ShouldBe(0.01f);
            }
        }

        public class IsEcho
        {
            [Fact]
            public void Finds_no_echo_when_nothing_was_sent()
            {
                var echoes = new LeafEchoes();

                var echo = echoes.IsEcho(10f);

                echo.ShouldBeFalse();
            }

            [Fact]
            public void Matches_an_angle_just_inside_the_match()
            {
                var echoes = Sending(0f);

                var echo = echoes.IsEcho(0.0099f);

                echo.ShouldBeTrue();
            }

            [Fact]
            public void Misses_an_angle_exactly_at_the_match()
            {
                var echoes = Sending(0f);

                var echo = echoes.IsEcho(0.01f);

                echo.ShouldBeFalse();
            }

            [Fact]
            public void Drops_the_echo_and_every_older_angle()
            {
                var echoes = Sending(10f, 20f, 30f);

                echoes.IsEcho(20f);

                echoes.IsEcho(10f).ShouldBeFalse();
            }

            [Fact]
            public void Keeps_the_newer_angles()
            {
                var echoes = Sending(10f, 20f, 30f);

                echoes.IsEcho(20f);

                echoes.IsEcho(30f).ShouldBeTrue();
            }

            [Fact]
            public void Forgets_every_angle_when_none_matches()
            {
                var echoes = Sending(10f, 20f);

                echoes.IsEcho(50f);

                echoes.Empty.ShouldBeTrue();
            }

            [Fact]
            public void Empties_once_the_last_echo_came()
            {
                var echoes = Sending(10f);

                echoes.IsEcho(10f);

                echoes.Empty.ShouldBeTrue();
            }
        }

        public class Sent
        {
            [Fact]
            public void Keeps_the_sixty_fourth_angle()
            {
                var echoes = new LeafEchoes();
                for (int i = 0; i < 64; i++)
                {
                    echoes.Sent(i);
                }

                var echo = echoes.IsEcho(63f);

                echo.ShouldBeTrue();
            }

            [Fact]
            public void Drops_an_angle_past_the_sixty_fourth()
            {
                var echoes = new LeafEchoes();
                for (int i = 0; i < 65; i++)
                {
                    echoes.Sent(i);
                }

                var echo = echoes.IsEcho(64f);

                echo.ShouldBeFalse();
            }
        }
    }
}
