using DarkwoodMP.World.Dragging;

namespace DarkwoodMP.Tests.World.Dragging
{
    public class EchoListTests
    {
        static readonly Vec3 First = new Vec3(100f, 0f, 200f);
        static readonly Vec3 Second = new Vec3(150f, 0f, 200f);
        static readonly Vec3 Third = new Vec3(200f, 0f, 200f);

        static EchoList Three()
        {
            var echoes = new EchoList();
            echoes.Sent(First);
            echoes.Sent(Second);
            echoes.Sent(Third);
            return echoes;
        }

        public class Limits
        {
            [Fact]
            public void Pins_the_list_limits()
            {
                var limits = new[] { EchoList.Most, EchoList.Match };

                limits.ShouldBe(new[] { 16f, 0.01f });
            }
        }

        public class Sent
        {
            [Fact]
            public void Keeps_sixteen_rests()
            {
                var echoes = new EchoList();

                for (int i = 0; i < 17; i++)
                {
                    echoes.Sent(new Vec3(i, 0f, 0f));
                }

                echoes.Count.ShouldBe(16);
            }

            [Fact]
            public void Drops_rests_past_sixteen()
            {
                var echoes = new EchoList();
                for (int i = 0; i < 17; i++)
                {
                    echoes.Sent(new Vec3(i, 0f, 0f));
                }

                bool echo = echoes.Take(new Vec3(16f, 0f, 0f));

                echo.ShouldBeFalse();
            }
        }

        public class Take
        {
            [Fact]
            public void Skips_our_own_rest_passed_back()
            {
                var echoes = Three();

                echoes.Take(Second).ShouldBeTrue();
            }

            [Fact]
            public void Drops_the_match_and_every_older_rest()
            {
                var echoes = Three();

                echoes.Take(Second);

                echoes.Count.ShouldBe(1);
            }

            [Fact]
            public void Keeps_the_newer_rests_for_their_echoes()
            {
                var echoes = Three();
                echoes.Take(Second);

                echoes.Take(Third).ShouldBeTrue();
            }

            [Fact]
            public void Empties_once_the_last_is_passed_back()
            {
                var echoes = Three();

                echoes.Take(Third);

                echoes.Empty.ShouldBeTrue();
            }

            [Fact]
            public void Takes_a_move_that_isnt_ours()
            {
                var echoes = Three();

                echoes.Take(new Vec3(500f, 0f, 0f)).ShouldBeFalse();
            }

            [Fact]
            public void Forgets_every_rest_after_a_move_that_isnt_ours()
            {
                var echoes = Three();

                echoes.Take(new Vec3(500f, 0f, 0f));

                echoes.Empty.ShouldBeTrue();
            }

            [Fact]
            public void Takes_a_rest_exactly_a_tenth_away_as_not_ours()
            {
                var echoes = new EchoList();
                echoes.Sent(new Vec3(0f, 0f, 0f));

                echoes.Take(new Vec3(0.08f, 0.06f, 0f)).ShouldBeFalse();
            }

            [Fact]
            public void Skips_a_rest_just_under_a_tenth_away()
            {
                var echoes = new EchoList();
                echoes.Sent(new Vec3(0f, 0f, 0f));

                echoes.Take(new Vec3(0f, 0.09f, 0.04f)).ShouldBeTrue();
            }

            [Theory]
            [InlineData(1f, 0f, 0f)]
            [InlineData(0f, 1f, 0f)]
            [InlineData(0f, 0f, 1f)]
            public void Takes_a_rest_a_unit_off_on_one_axis_as_not_ours(
                float x,
                float y,
                float z)
            {
                var echoes = new EchoList();
                echoes.Sent(new Vec3(0f, 0f, 0f));

                echoes.Take(new Vec3(x, y, z)).ShouldBeFalse();
            }

            [Fact]
            public void Takes_the_first_match_from_the_oldest()
            {
                var echoes = new EchoList();
                echoes.Sent(First);
                echoes.Sent(Second);
                echoes.Sent(First);

                echoes.Take(First);

                echoes.Count.ShouldBe(2);
            }
        }
    }
}
