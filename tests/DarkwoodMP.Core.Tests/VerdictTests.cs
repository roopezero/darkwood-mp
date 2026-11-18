namespace DarkwoodMP.Tests
{
    public class VerdictTests
    {
        public class Allow
        {
            [Fact]
            public void Refuses_nothing()
            {
                var verdict = Verdict.Allow;

                var refused = verdict.Refused;

                refused.ShouldBeFalse();
            }

            [Fact]
            public void Has_no_reason()
            {
                var verdict = Verdict.Allow;

                var reason = verdict.Reason;

                reason.ShouldBe(Refusal.None);
            }
        }

        public class Refuse
        {
            [Fact]
            public void Marks_the_verdict_as_refused()
            {
                var verdict = Verdict.Refuse(Refusal.NotHere);

                var refused = verdict.Refused;

                refused.ShouldBeTrue();
            }

            [Fact]
            public void Keeps_the_named_reason()
            {
                var verdict = Verdict.Refuse(Refusal.NotHere);

                var reason = verdict.Reason;

                reason.ShouldBe(Refusal.NotHere);
            }

            [Fact]
            public void Leaves_the_detail_empty_when_there_is_none()
            {
                var verdict = Verdict.Refuse(Refusal.NotHere);

                var detail = verdict.Detail;

                detail.ShouldBeNull();
            }

            [Fact]
            public void Keeps_the_detail_it_is_given()
            {
                var verdict = Verdict.Refuse(Refusal.NoKey, "Bunker");

                var detail = verdict.Detail;

                detail.ShouldBe("Bunker");
            }

            [Fact]
            public void Keeps_the_reason_when_given_a_detail()
            {
                var verdict = Verdict.Refuse(Refusal.NoKey, "Bunker");

                var reason = verdict.Reason;

                reason.ShouldBe(Refusal.NoKey);
            }
        }

        public class Equal
        {
            [Fact]
            public void Matches_the_same_reason_and_detail()
            {
                var verdict = Verdict.Refuse(Refusal.NoKey, "Bunker");

                var same = verdict.Equals(
                    Verdict.Refuse(Refusal.NoKey, "Bunker"));

                same.ShouldBeTrue();
            }

            [Fact]
            public void Tells_apart_a_different_reason()
            {
                var verdict = Verdict.Refuse(Refusal.NotHere);

                var same = verdict.Equals(Verdict.Refuse(Refusal.NoKey));

                same.ShouldBeFalse();
            }

            [Fact]
            public void Tells_apart_a_different_detail()
            {
                var verdict = Verdict.Refuse(Refusal.NoKey, "Bunker");

                var same = verdict.Equals(
                    Verdict.Refuse(Refusal.NoKey, "Cellar"));

                same.ShouldBeFalse();
            }
        }

        public class AsText
        {
            [Fact]
            public void Says_allow_when_nothing_was_refused()
            {
                var verdict = Verdict.Allow;

                var text = verdict.ToString();

                text.ShouldBe("Allow");
            }

            [Fact]
            public void Names_the_reason()
            {
                var verdict = Verdict.Refuse(Refusal.NotHere);

                var text = verdict.ToString();

                text.ShouldBe("Refuse(NotHere)");
            }

            [Fact]
            public void Names_the_reason_and_the_detail()
            {
                var verdict = Verdict.Refuse(Refusal.NoKey, "Bunker");

                var text = verdict.ToString();

                text.ShouldBe("Refuse(NoKey, Bunker)");
            }
        }
    }
}
