using DarkwoodMP.World.Saves;

namespace DarkwoodMP.Tests.World.Saves
{
    public class SaveBookRulesTests
    {
        static readonly BookPlayer Kept = new BookPlayer
        {
            Key = "k1",
            Name = "Ann",
        };

        public class SamePlayer
        {
            [Fact]
            public void Matches_by_key()
            {
                var joiner = Kept with { Name = "Bea" };

                SaveBookRules.SamePlayer(Kept, joiner).ShouldBeTrue();
            }

            [Fact]
            public void Ignores_the_name_when_keyed()
            {
                var joiner = Kept with { Key = "k2" };

                SaveBookRules.SamePlayer(Kept, joiner).ShouldBeFalse();
            }

            [Fact]
            public void Matches_by_name_without_a_key()
            {
                var joiner = new BookPlayer { Key = "", Name = "Ann" };

                SaveBookRules.SamePlayer(Kept, joiner).ShouldBeTrue();
            }

            [Fact]
            public void Matches_a_null_key_by_key()
            {
                var joiner = new BookPlayer { Key = null, Name = "Ann" };

                SaveBookRules.SamePlayer(Kept, joiner).ShouldBeFalse();
            }
        }

        public class Renames
        {
            [Theory]
            [InlineData("Farm", " Farm ", false)]
            [InlineData("", null, false)]
            [InlineData("Farm", "Wood", true)]
            public void Writes_only_a_new_trimmed_name(
                string old,
                string typed,
                bool renames)
            {
                var result = SaveBookRules.Renames(old, typed);

                result.ShouldBe(renames);
            }
        }
    }
}
