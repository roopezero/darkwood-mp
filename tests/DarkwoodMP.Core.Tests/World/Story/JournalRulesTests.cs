using DarkwoodMP.World.Story;

namespace DarkwoodMP.Tests.World.Story
{
    public class JournalRulesTests
    {
        const string Dream = "{\"inDream\":true}";

        public class Take
        {
            [Theory]
            [InlineData('e')]
            [InlineData('n')]
            [InlineData('k')]
            [InlineData('i')]
            [InlineData('l')]
            [InlineData('d')]
            public void Adds_an_entry_of_each_kind(char kind)
            {
                JournalRules.Take(kind, "{}", false)
                    .ShouldBe(JournalTake.Add);
            }

            [Fact]
            public void Removes_on_the_removed_value()
            {
                JournalRules.Take('e', "-", false)
                    .ShouldBe(JournalTake.Remove);
            }

            [Fact]
            public void Removes_a_kind_it_doesnt_know_too()
            {
                JournalRules.Take('x', "-", false)
                    .ShouldBe(JournalTake.Remove);
            }

            [Fact]
            public void Leaves_a_dream_entry_outside_a_shared_dream()
            {
                JournalRules.Take('e', Dream, false)
                    .ShouldBe(JournalTake.OtherDream);
            }

            [Fact]
            public void Adds_a_dream_entry_in_a_shared_dream()
            {
                JournalRules.Take('e', Dream, true)
                    .ShouldBe(JournalTake.Add);
            }

            [Theory]
            [InlineData('l')]
            [InlineData('d')]
            public void Adds_a_dream_place_or_direction(char kind)
            {
                JournalRules.Take(kind, Dream, false)
                    .ShouldBe(JournalTake.Add);
            }

            [Fact]
            public void Leaves_a_dream_entry_of_an_unknown_kind_as_dream()
            {
                JournalRules.Take('x', Dream, false)
                    .ShouldBe(JournalTake.OtherDream);
            }

            [Fact]
            public void Does_nothing_with_an_unknown_kind()
            {
                JournalRules.Take('x', "{}", false)
                    .ShouldBe(JournalTake.Unknown);
            }
        }

        public class Popup
        {
            [Theory]
            [InlineData('e', false, "JournalEntry")]
            [InlineData('n', false, "Note")]
            [InlineData('l', false, "Location")]
            [InlineData('e', true, "JournalEntry")]
            public void Shows_an_entry_a_note_and_a_place(
                char kind,
                bool quiet,
                string popup)
            {
                JournalRules.Popup(kind, quiet).ShouldBe(popup);
            }

            [Theory]
            [InlineData('k')]
            [InlineData('i')]
            [InlineData('d')]
            [InlineData('x')]
            public void Shows_nothing_for_other_kinds(char kind)
            {
                JournalRules.Popup(kind, false).ShouldBeNull();
            }

            [Fact]
            public void Shows_nothing_for_a_quiet_place()
            {
                JournalRules.Popup('l', true).ShouldBeNull();
            }
        }

        public class Noun
        {
            [Theory]
            [InlineData('e', "entry")]
            [InlineData('n', "note")]
            [InlineData('k', "key")]
            [InlineData('i', "item")]
            [InlineData('l', "place")]
            [InlineData('d', "direction")]
            [InlineData('x', "direction")]
            public void Names_each_kind(char kind, string noun)
            {
                JournalRules.Noun(kind).ShouldBe(noun);
            }
        }

        public class TextOf
        {
            [Theory]
            [InlineData("Location")]
            [InlineData("LocationAlreadyFound")]
            public void Looks_a_place_up_by_its_name_in_ui(string type)
            {
                JournalRules.TextOf(type, "place_10").ShouldBe(new PopupText
                {
                    Key = "place_10",
                    Sheet = "UI",
                });
            }

            [Fact]
            public void Looks_an_entry_up_in_journal_entries()
            {
                var text = JournalRules.TextOf("JournalEntry", "e1");

                text.ShouldBe(new PopupText
                {
                    Key = "e1_name",
                    Sheet = "JournalEntries",
                });
            }

            [Fact]
            public void Looks_a_note_up_in_notes_then_items()
            {
                JournalRules.TextOf("Note", "n1").ShouldBe(new PopupText
                {
                    Key = "n1_name",
                    Sheet = "Notes",
                    OtherSheet = "Items",
                });
            }

            [Theory]
            [InlineData("Key")]
            [InlineData("InvItem")]
            public void Looks_a_key_or_item_up_in_items(string type)
            {
                JournalRules.TextOf(type, "k1").ShouldBe(new PopupText
                {
                    Key = "k1_name",
                    Sheet = "Items",
                });
            }

            [Fact]
            public void Names_a_missing_entry_by_the_suffix_alone()
            {
                JournalRules.TextOf("Note", null).Key.ShouldBe("_name");
            }

            [Fact]
            public void Shows_other_popups_with_no_lookup()
            {
                JournalRules.TextOf("Reputation", "Wolf").ShouldBe(
                    new PopupText { Always = true });
            }
        }
    }
}
