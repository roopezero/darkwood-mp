using DarkwoodMP.Sync.Players;

namespace DarkwoodMP.Tests.Sync.Players
{
    public class GameMathTests
    {
        public class RoundToInt
        {
            [Theory]
            [InlineData(0.5f, 0)]
            [InlineData(1.5f, 2)]
            [InlineData(2.5f, 2)]
            [InlineData(-2.5f, -2)]
            [InlineData(2.51f, 3)]
            public void Rounds_halves_to_even(float value, int rounded)
            {
                var result = GameMath.RoundToInt(value);

                result.ShouldBe(rounded);
            }
        }

        public class Max
        {
            [Fact]
            public void Takes_the_second_against_nan_first()
            {
                var result = GameMath.Max(float.NaN, 3f);

                result.ShouldBe(3f);
            }

            [Fact]
            public void Takes_the_larger()
            {
                var result = GameMath.Max(2f, 3f);

                result.ShouldBe(3f);
            }
        }

        public class Min
        {
            [Fact]
            public void Takes_the_second_against_nan_first()
            {
                var result = GameMath.Min(float.NaN, 3f);

                result.ShouldBe(3f);
            }

            [Fact]
            public void Takes_the_smaller()
            {
                var result = GameMath.Min(2f, 3f);

                result.ShouldBe(2f);
            }
        }

        public class Clamp01
        {
            [Theory]
            [InlineData(-0.1f, 0f)]
            [InlineData(0.4f, 0.4f)]
            [InlineData(1.1f, 1f)]
            public void Holds_it_to_0_to_1(float value, float held)
            {
                var result = GameMath.Clamp01(value);

                result.ShouldBe(held);
            }
        }
    }
}
