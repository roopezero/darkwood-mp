using DarkwoodMP.World.Story;

namespace DarkwoodMP.Tests.World.Story
{
    public class StoryKeysTests
    {
        public class Prefixes
        {
            [Fact]
            public void Pins_the_key_prefixes()
            {
                var prefixes = new[]
                {
                    StoryKeys.Karma,
                    StoryKeys.Flag,
                    StoryKeys.Npc,
                    StoryKeys.Reputation,
                    StoryKeys.Reward,
                    StoryKeys.Dialogue,
                    StoryKeys.Entry,
                    StoryKeys.Note,
                    StoryKeys.Key,
                    StoryKeys.Item,
                    StoryKeys.Place,
                    StoryKeys.Direction,
                    StoryKeys.Removed,
                    StoryKeys.ModPrefix,
                };

                prefixes.ShouldBe(new[]
                {
                    "k", "f:", "n:", "r:", "ra:", "d:", "je:", "jn:", "jk:",
                    "ji:", "jl:", "jd:", "-", "dwmp_",
                });
            }
        }

        public class Change
        {
            [Fact]
            public void Writes_a_rise_with_a_plus()
            {
                StoryKeys.Change(5).ShouldBe("+5");
            }

            [Fact]
            public void Writes_a_fall_as_plus_minus()
            {
                StoryKeys.Change(-5).ShouldBe("+-5");
            }
        }

        public class Whole
        {
            [Fact]
            public void Writes_the_value_after_an_equals_sign()
            {
                StoryKeys.Whole("12").ShouldBe("=12");
            }
        }

        public class IsRemovable
        {
            [Theory]
            [InlineData("je:a")]
            [InlineData("jn:a")]
            [InlineData("jk:a")]
            [InlineData("ji:a")]
            public void Takes_an_entry_note_key_or_item(string key)
            {
                StoryKeys.IsRemovable(key).ShouldBeTrue();
            }

            [Theory]
            [InlineData("jl:a")]
            [InlineData("jd:a")]
            [InlineData("f:abc")]
            [InlineData("jex:a")]
            [InlineData("xe:a")]
            public void Leaves_out_other_keys(string key)
            {
                StoryKeys.IsRemovable(key).ShouldBeFalse();
            }

            [Fact]
            public void Leaves_out_a_prefix_with_no_name()
            {
                StoryKeys.IsRemovable("je:").ShouldBeFalse();
            }
        }

        public class IsDreamEntry
        {
            [Fact]
            public void Finds_in_dream_true()
            {
                var value = "{\"type\":\"a\",\"inDream\":true}";

                StoryKeys.IsDreamEntry(value).ShouldBeTrue();
            }

            [Fact]
            public void Leaves_in_dream_false()
            {
                var value = "{\"type\":\"a\",\"inDream\":false}";

                StoryKeys.IsDreamEntry(value).ShouldBeFalse();
            }

            [Fact]
            public void Leaves_a_spaced_out_value()
            {
                var value = "{\"inDream\": true}";

                StoryKeys.IsDreamEntry(value).ShouldBeFalse();
            }

            [Fact]
            public void Leaves_null()
            {
                StoryKeys.IsDreamEntry(null).ShouldBeFalse();
            }
        }

        public class FlagValue
        {
            [Fact]
            public void Writes_the_bool_as_dotnet_does()
            {
                StoryKeys.FlagValue(true, 3).ShouldBe("True|3");
            }

            [Fact]
            public void Writes_false_and_a_negative_amount()
            {
                StoryKeys.FlagValue(false, -1).ShouldBe("False|-1");
            }
        }

        public class RewardValue
        {
            [Fact]
            public void Writes_the_day_and_the_amount()
            {
                StoryKeys.RewardValue(4, 15).ShouldBe("4|15");
            }
        }

        public class ReadReward
        {
            [Fact]
            public void Reads_the_day_and_the_amount()
            {
                StoryKeys.ReadReward("4|15", out int day, out int amount);

                (day, amount).ShouldBe((4, 15));
            }

            [Fact]
            public void Ignores_a_third_part()
            {
                StoryKeys.ReadReward("4|15|9", out int day, out int amount);

                (day, amount).ShouldBe((4, 15));
            }

            [Fact]
            public void Throws_without_an_amount()
            {
                Should.Throw<System.IndexOutOfRangeException>(
                    () => StoryKeys.ReadReward("4", out _, out _));
            }

            [Fact]
            public void Throws_on_a_word()
            {
                Should.Throw<System.FormatException>(
                    () => StoryKeys.ReadReward("x|15", out _, out _));
            }
        }

        public class KindOf
        {
            [Theory]
            [InlineData("k", StoryKey.Karma)]
            [InlineData("je:a", StoryKey.Journal)]
            [InlineData("jl:", StoryKey.Journal)]
            [InlineData("f:a", StoryKey.Flag)]
            [InlineData("n:a", StoryKey.Npc)]
            [InlineData("d:a", StoryKey.Dialogue)]
            [InlineData("f:", StoryKey.Flag)]
            [InlineData("x:a", StoryKey.Unknown)]
            [InlineData("kk", StoryKey.Unknown)]
            [InlineData("r:a", StoryKey.Unknown)]
            public void Names_a_key_by_its_first_character(
                string key,
                StoryKey kind)
            {
                StoryKeys.KindOf(key).ShouldBe(kind);
            }

            [Theory]
            [InlineData("")]
            [InlineData("j")]
            [InlineData("je")]
            [InlineData("f")]
            [InlineData("x")]
            public void Calls_a_key_too_short_for_its_prefix_malformed(
                string key)
            {
                StoryKeys.KindOf(key).ShouldBe(StoryKey.Malformed);
            }
        }
    }
}
