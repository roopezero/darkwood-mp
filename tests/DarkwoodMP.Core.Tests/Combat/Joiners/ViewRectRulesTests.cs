using DarkwoodMP.Combat.Joiners;

namespace DarkwoodMP.Tests.Combat.Joiners
{
    public class ViewRectRulesTests
    {
        public class Limits
        {
            [Fact]
            public void Pins_the_view_rect_limits()
            {
                var limits = new[]
                {
                    ViewRectRules.CheckEvery,
                    ViewRectRules.MinHalf,
                    ViewRectRules.MaxHalf,
                };

                limits.ShouldBe(new[] { 2f, 600f, 6000f });
            }
        }

        public class Accepted
        {
            [Theory]
            [InlineData(1200, 800, 1200, 800)]
            [InlineData(600, 6000, 600, 6000)]
            [InlineData(599, 6001, 600, 6000)]
            [InlineData(0, 65535, 600, 6000)]
            public void Clamps_each_half(
                int dx,
                int dy,
                int keptDx,
                int keptDy)
            {
                var rect = ViewRectRules.Accepted(new ViewRect(dx, dy));

                rect.ShouldBe(new ViewRect(keptDx, keptDy));
            }
        }

        public class Sendable
        {
            [Theory]
            [InlineData(1, 1, true)]
            [InlineData(0, 900, false)]
            [InlineData(900, 0, false)]
            [InlineData(-5, 900, false)]
            public void Sends_only_halves_above_zero(
                int dx,
                int dy,
                bool sends)
            {
                var result = ViewRectRules.Sendable(new ViewRect(dx, dy));

                result.ShouldBe(sends);
            }
        }
    }
}
