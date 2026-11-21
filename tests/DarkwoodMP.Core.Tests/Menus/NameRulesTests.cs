using DarkwoodMP.Menus;

namespace DarkwoodMP.Tests.Menus
{
    public class NameRulesTests
    {
        public class MayRename
        {
            [Fact]
            public void Allows_a_new_name_in_no_game()
            {
                var verdict = NameRules.MayRename(false);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Refuses_a_new_name_in_a_game()
            {
                var verdict = NameRules.MayRename(true);

                verdict.Reason.ShouldBe(Refusal.NameLocked);
            }
        }

        public class OthersSee
        {
            [Fact]
            public void Says_the_name_the_others_see()
            {
                var text = NameRules.OthersSee("Ann");

                text.ShouldBe(
                    "Others see you as Ann. Left empty, it's your Steam"
                    + " name, or your computer's user name.");
            }

            [Fact]
            public void Says_no_name_with_no_session()
            {
                var text = NameRules.OthersSee(null);

                text.ShouldStartWith("Others see you as . Left empty");
            }
        }

        public class AppliesTypedName
        {
            [Theory]
            [InlineData(true, "Ann", "Bob", true)]
            [InlineData(true, "Bob", "Bob", false)]
            [InlineData(false, "Ann", "Bob", false)]
            [InlineData(true, "", "Bob", true)]
            [InlineData(true, "bob", "Bob", true)]
            public void Applies_a_changed_name_while_the_box_is_ticked(
                bool ticked,
                string typed,
                string current,
                bool applies)
            {
                var result = NameRules.AppliesTypedName(ticked, typed, current);

                result.ShouldBe(applies);
            }
        }

        public class BrowserHint
        {
            [Theory]
            [InlineData("")]
            [InlineData(null)]
            [InlineData("   ")]
            public void Points_to_Options_while_no_name_is_set(string setting)
            {
                var hint = NameRules.BrowserHint(false, setting, "ann");

                hint.ShouldBe("You're ann: set a name in Options");
            }

            [Fact]
            public void Says_nothing_once_a_name_is_set()
            {
                var hint = NameRules.BrowserHint(false, "Ann", "Ann");

                hint.ShouldBeNull();
            }

            [Fact]
            public void Says_nothing_in_a_game()
            {
                var hint = NameRules.BrowserHint(true, "", "ann");

                hint.ShouldBeNull();
            }
        }
    }
}
