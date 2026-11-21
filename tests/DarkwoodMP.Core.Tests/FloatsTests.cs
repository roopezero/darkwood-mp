namespace DarkwoodMP.Tests
{
    public class FloatsTests
    {
        public class Max
        {
            [Theory]
            [InlineData(1f, 2f, 2f)]
            [InlineData(3f, 2f, 3f)]
            public void Takes_the_larger(float a, float b, float expected)
            {
                Floats.Max(a, b).ShouldBe(expected);
            }

            [Fact]
            public void Takes_a_NaN_second_as_Unity_does()
            {
                float.IsNaN(Floats.Max(1f, float.NaN)).ShouldBeTrue();
            }

            [Fact]
            public void Takes_the_second_after_a_NaN()
            {
                Floats.Max(float.NaN, 1f).ShouldBe(1f);
            }

            [Theory]
            [InlineData(1, 2, 2)]
            [InlineData(3, 2, 3)]
            public void Takes_the_larger_int(int a, int b, int expected)
            {
                Floats.Max(a, b).ShouldBe(expected);
            }
        }

        public class Min
        {
            [Theory]
            [InlineData(1, 2, 1)]
            [InlineData(3, 2, 2)]
            public void Takes_the_smaller_int(int a, int b, int expected)
            {
                Floats.Min(a, b).ShouldBe(expected);
            }
        }

        public class Clamp
        {
            [Theory]
            [InlineData(-1f, 0f)]
            [InlineData(0f, 0f)]
            [InlineData(5f, 5f)]
            [InlineData(10f, 10f)]
            [InlineData(11f, 10f)]
            public void Keeps_it_within(float value, float expected)
            {
                Floats.Clamp(value, 0f, 10f).ShouldBe(expected);
            }

            [Fact]
            public void Leaves_NaN()
            {
                float.IsNaN(Floats.Clamp(float.NaN, 0f, 1f)).ShouldBeTrue();
            }
        }
    }
}
