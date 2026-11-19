using System;

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

            [Fact]
            public void Words_a_refusal_that_has_no_detail()
            {
                var verdict = Verdict.Refuse(Refusal.NotHere);

                var text = Refusals.Text(verdict);

                text.ShouldBe("not here");
            }

            [Fact]
            public void Puts_the_key_into_its_sentence()
            {
                var verdict = Verdict.Refuse(Refusal.NoKey, "Bunker");

                var text = Refusals.Text(verdict);

                text.ShouldBe("nobody has the key (Bunker)");
            }

            [Fact]
            public void Puts_the_hit_into_its_sentence()
            {
                var verdict = Verdict.Refuse(Refusal.HitTooLarge, "1500");

                var text = Refusals.Text(verdict);

                text.ShouldBe("a hit of 1500 is more than any weapon does");
            }

            [Theory]
            [MemberData(nameof(Reasons))]
            public void Has_a_sentence_for_every_reason(Refusal reason)
            {
                var verdict = Verdict.Refuse(reason, "detail");

                var text = Refusals.Text(verdict);

                text.ShouldNotBe(reason.ToString());
            }
        }
    }
}
