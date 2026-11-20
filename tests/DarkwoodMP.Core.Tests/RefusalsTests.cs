using System;
using System.Linq;

namespace DarkwoodMP.Tests
{
    public class RefusalsTests
    {
        public class Text
        {
            public static TheoryData<Refusal> Reasons()
            {
                var reasons = new TheoryData<Refusal>();
                foreach (Refusal reason in Enum.GetValues(typeof(Refusal)))
                {
                    if (reason != Refusal.None)
                    {
                        reasons.Add(reason);
                    }
                }

                return reasons;
            }

            [Fact]
            public void Says_nothing_when_nothing_was_refused()
            {
                var verdict = Verdict.Allow;

                var text = Refusals.Text(verdict);

                text.ShouldBe("");
            }

            [Theory]
            [MemberData(
                nameof(RefusalSentences.All),
                MemberType = typeof(RefusalSentences))]
            public void Words_each_refusal_as_the_host_does(
                Refusal reason,
                string detail,
                string expected)
            {
                var verdict = Verdict.Refuse(reason, detail);

                var text = Refusals.Text(verdict);

                text.ShouldBe(expected);
            }

            [Theory]
            [MemberData(nameof(Reasons))]
            public void Has_a_sentence_for_every_reason(Refusal reason)
            {
                var verdict = Verdict.Refuse(reason, "detail");

                var text = Refusals.Text(verdict);

                text.ShouldNotBe(reason.ToString());
            }

            [Theory]
            [MemberData(nameof(Reasons))]
            public void Pins_the_words_of_every_reason(Refusal reason)
            {
                var pinned = RefusalSentences.All();

                var rows = pinned.Count(row => (Refusal)row[0] == reason);

                rows.ShouldBe(1);
            }

            [Fact]
            public void Leaves_a_missing_value_empty()
            {
                var verdict = Verdict.Refuse(Refusal.TankFull, "99.5");

                var text = Refusals.Text(verdict);

                text.ShouldBe("the tank is full (99.5 of )");
            }

            [Fact]
            public void Leaves_every_value_empty_without_a_detail()
            {
                var verdict = Verdict.Refuse(Refusal.OutOfReach);

                var text = Refusals.Text(verdict);

                text.ShouldBe(" is  away from them (reach )");
            }

            [Fact]
            public void Keeps_a_separator_in_the_last_value()
            {
                // A name with the separator in it.
                var name = Refusals.Detail("Wooden", "door");
                var detail = Refusals.Detail("4041", "600", name);

                var text = Refusals.Text(
                    Verdict.Refuse(Refusal.OutOfReach, detail));

                text.ShouldBe(name + " is 4041 away from them (reach 600)");
            }
        }

        public class Detail
        {
            [Fact]
            public void Keeps_two_values_apart()
            {
                var other = Refusals.Detail("12", "3");

                var detail = Refusals.Detail("1", "23");

                detail.ShouldNotBe(other);
            }

            [Fact]
            public void Keeps_three_values_apart()
            {
                var other = Refusals.Detail("1", "23", "4");

                var detail = Refusals.Detail("1", "2", "34");

                detail.ShouldNotBe(other);
            }
        }
    }
}
