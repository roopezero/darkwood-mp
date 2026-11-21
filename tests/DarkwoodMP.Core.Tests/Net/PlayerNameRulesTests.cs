using DarkwoodMP.Net;

namespace DarkwoodMP.Tests.Net
{
    public class PlayerNameRulesTests
    {
        public class Choose
        {
            [Theory]
            [InlineData("Ann", "Steamy", "os", "Ann")]
            [InlineData("", "Steamy", "os", "Steamy")]
            [InlineData("  ", "Steamy", "os", "Steamy")]
            [InlineData(null, null, "os", "os")]
            [InlineData(null, " ", "os", "os")]
            [InlineData(null, null, null, "Player")]
            [InlineData(" ", "\t", "", "Player")]
            public void Takes_the_first_name_that_isnt_blank(
                string setting,
                string steam,
                string os,
                string name)
            {
                var chosen = PlayerNameRules.Choose(setting, steam, os);

                chosen.ShouldBe(name);
            }

            [Fact]
            public void Trims_the_name_chosen()
            {
                var chosen = PlayerNameRules.Choose("  Ann ", "Steamy", "os");

                chosen.ShouldBe("Ann");
            }
        }

        public class Limits
        {
            [Fact]
            public void Pins_the_fallback()
            {
                var name = PlayerNameRules.Fallback;

                name.ShouldBe("Player");
            }
        }
    }
}
