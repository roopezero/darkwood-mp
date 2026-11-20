using System.Collections.Generic;
using DarkwoodMP.World.Story;

namespace DarkwoodMP.Tests.World.Story
{
    public class StoryDiffTests
    {
        const string Dream = "{\"inDream\":true}";

        static Dictionary<string, string> Story(params string[] pairs)
        {
            var story = new Dictionary<string, string>();
            for (int i = 0; i < pairs.Length; i += 2)
            {
                story[pairs[i]] = pairs[i + 1];
            }

            return story;
        }

        static KeyValuePair<string, string> Pair(string key, string value)
        {
            return new KeyValuePair<string, string>(key, value);
        }

        public class Changes
        {
            [Fact]
            public void Sends_nothing_when_nothing_changed()
            {
                var was = Story("f:a", "True|1", "r:Wolf", "5");
                var now = Story("f:a", "True|1", "r:Wolf", "5");

                var changed = StoryDiff.Changes(was, now, false);

                changed.ShouldBeEmpty();
            }

            [Fact]
            public void Sends_a_changed_flag_as_it_is()
            {
                var was = Story("f:a", "True|1");

                var changed = StoryDiff.Changes(
                    was,
                    Story("f:a", "False|1"),
                    false);

                changed.ShouldBe(new[] { Pair("f:a", "False|1") });
            }

            [Fact]
            public void Sends_a_new_entry()
            {
                var changed = StoryDiff.Changes(
                    Story(),
                    Story("jl:Village", ""),
                    false);

                changed.ShouldBe(new[] { Pair("jl:Village", "") });
            }

            [Fact]
            public void Sends_a_reputation_as_the_change()
            {
                var changed = StoryDiff.Changes(
                    Story("r:Wolf", "5"),
                    Story("r:Wolf", "2"),
                    false);

                changed.ShouldBe(new[] { Pair("r:Wolf", "+-3") });
            }

            [Fact]
            public void Sends_a_new_reputation_whole()
            {
                var changed = StoryDiff.Changes(
                    Story(),
                    Story("r:Wolf", "2"),
                    false);

                changed.ShouldBe(new[] { Pair("r:Wolf", "=2") });
            }

            [Fact]
            public void Sends_a_reputation_whole_after_a_word()
            {
                var changed = StoryDiff.Changes(
                    Story("r:Wolf", "x"),
                    Story("r:Wolf", "2"),
                    false);

                changed.ShouldBe(new[] { Pair("r:Wolf", "=2") });
            }

            [Fact]
            public void Sends_a_removed_journal_entry_as_removed()
            {
                var changed = StoryDiff.Changes(
                    Story("jk:key1", "{}"),
                    Story(),
                    false);

                changed.ShouldBe(new[] { Pair("jk:key1", "-") });
            }

            [Fact]
            public void Leaves_out_a_gone_entry_that_cant_be_removed()
            {
                var changed = StoryDiff.Changes(
                    Story("jl:Village", "", "f:a", "True|1"),
                    Story(),
                    false);

                changed.ShouldBeEmpty();
            }

            [Fact]
            public void Leaves_out_a_dream_entry_gone_outside_a_shared_dream()
            {
                var changed = StoryDiff.Changes(
                    Story("je:dream", Dream),
                    Story(),
                    false);

                changed.ShouldBeEmpty();
            }

            [Fact]
            public void Sends_a_dream_entry_gone_in_a_shared_dream()
            {
                var changed = StoryDiff.Changes(
                    Story("je:dream", Dream),
                    Story(),
                    true);

                changed.ShouldBe(new[] { Pair("je:dream", "-") });
            }

            [Fact]
            public void Puts_the_removals_after_the_changes()
            {
                var changed = StoryDiff.Changes(
                    Story("ji:a", "{}"),
                    Story("f:a", "True|1"),
                    false);

                changed.ShouldBe(new[]
                {
                    Pair("f:a", "True|1"),
                    Pair("ji:a", "-"),
                });
            }
        }

        public class SinceSnapshot
        {
            [Fact]
            public void Sends_a_reputation_whole()
            {
                var changed = StoryDiff.SinceSnapshot(
                    Story("r:Wolf", "5"),
                    Story("r:Wolf", "2"),
                    false);

                changed.ShouldBe(new[] { Pair("r:Wolf", "=2") });
            }

            [Fact]
            public void Sends_a_changed_flag_as_it_is()
            {
                var changed = StoryDiff.SinceSnapshot(
                    Story("f:a", "True|1"),
                    Story("f:a", "True|2"),
                    false);

                changed.ShouldBe(new[] { Pair("f:a", "True|2") });
            }

            [Fact]
            public void Leaves_out_what_the_snapshot_had()
            {
                var changed = StoryDiff.SinceSnapshot(
                    Story("f:a", "True|1"),
                    Story("f:a", "True|1"),
                    false);

                changed.ShouldBeEmpty();
            }

            [Fact]
            public void Sends_a_removed_note()
            {
                var changed = StoryDiff.SinceSnapshot(
                    Story("jn:note", "{}"),
                    Story(),
                    false);

                changed.ShouldBe(new[] { Pair("jn:note", "-") });
            }

            [Fact]
            public void Leaves_out_a_dream_entry_gone_outside_a_shared_dream()
            {
                var changed = StoryDiff.SinceSnapshot(
                    Story("jn:note", Dream),
                    Story(),
                    false);

                changed.ShouldBeEmpty();
            }
        }

        public class Absorbed
        {
            [Fact]
            public void Adds_the_change_to_the_baseline()
            {
                StoryDiff.Absorbed("5", -2).ShouldBe("3");
            }

            [Fact]
            public void Leaves_it_for_no_change()
            {
                StoryDiff.Absorbed("5", 0).ShouldBeNull();
            }

            [Fact]
            public void Leaves_it_for_no_entry()
            {
                StoryDiff.Absorbed(null, 2).ShouldBeNull();
            }

            [Fact]
            public void Leaves_a_value_that_isnt_a_number()
            {
                StoryDiff.Absorbed("x", 2).ShouldBeNull();
            }
        }

        public class AfterTaking
        {
            [Fact]
            public void Takes_the_value_sent()
            {
                StoryDiff.AfterTaking("f:a", "True|1", null)
                    .ShouldBe("True|1");
            }

            [Fact]
            public void Takes_the_value_of_a_place()
            {
                StoryDiff.AfterTaking("jl:Village", "", "x").ShouldBe("");
            }

            [Fact]
            public void Takes_a_removal_out()
            {
                StoryDiff.AfterTaking("je:a", "-", "{}").ShouldBeNull();
            }

            [Fact]
            public void Takes_our_own_journal_entry()
            {
                StoryDiff.AfterTaking("je:a", "{\"x\":1}", "{\"x\":2}")
                    .ShouldBe("{\"x\":2}");
            }

            [Fact]
            public void Takes_an_entry_we_didnt_take_out()
            {
                StoryDiff.AfterTaking("je:a", "{\"x\":1}", null)
                    .ShouldBeNull();
            }

            [Fact]
            public void Keeps_a_removal_of_what_cant_be_removed()
            {
                StoryDiff.AfterTaking("f:a", "-", null).ShouldBe("-");
            }
        }
    }
}
