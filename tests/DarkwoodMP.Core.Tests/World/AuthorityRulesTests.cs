using DarkwoodMP.World;

namespace DarkwoodMP.Tests.World
{
    public class AuthorityRulesTests
    {
        static readonly Verdict Locked = Verdict.Refuse(Refusal.DoorLocked);

        static readonly Verdict Far = Verdict.Refuse(Refusal.OutOfReach, "x");

        /// A request from player 3 that the handler has started on.
        static readonly RequestAnswer Asked = AuthorityRules.Begin(3);

        public class Fits
        {
            [Fact]
            public void Pins_the_longest_request()
            {
                AuthorityRules.MaxRequest.ShouldBe(65536);
            }

            [Theory]
            [InlineData(65536, true)]
            [InlineData(65537, false)]
            public void Sends_a_request_up_to_the_longest(int length, bool fits)
            {
                var sent = AuthorityRules.Fits(length);

                sent.ShouldBe(fits);
            }
        }

        public class MayHandle
        {
            [Fact]
            public void Refuses_an_empty_request()
            {
                int length = 0;
                bool nested = false;

                var verdict = AuthorityRules.MayHandle(length, nested);

                verdict.Reason.ShouldBe(Refusal.EmptyRequest);
            }

            [Fact]
            public void Refuses_a_request_inside_a_request()
            {
                int length = 9;
                bool nested = true;

                var verdict = AuthorityRules.MayHandle(length, nested);

                verdict.Reason.ShouldBe(Refusal.EmptyRequest);
            }

            [Fact]
            public void Allows_a_message()
            {
                int length = 1;
                bool nested = false;

                var verdict = AuthorityRules.MayHandle(length, nested);

                verdict.ShouldBe(Verdict.Allow);
            }
        }

        public class Begin
        {
            [Fact]
            public void Starts_handling_with_nothing_refused()
            {
                var answer = AuthorityRules.Begin(3);

                answer.Handling.ShouldBeTrue();
                answer.From.ShouldBe(3UL);
                answer.Verdict.ShouldBe(Verdict.Allow);
                answer.Result.ShouldBe("");
            }
        }

        public class Refuse
        {
            [Fact]
            public void Refuses_the_request()
            {
                var answer = AuthorityRules.Refuse(Asked, Locked);

                answer.Verdict.ShouldBe(Locked);
            }

            [Fact]
            public void Keeps_the_first_refusal()
            {
                var first = AuthorityRules.Refuse(Asked, Locked);

                var answer = AuthorityRules.Refuse(first, Far);

                answer.Verdict.ShouldBe(Locked);
            }

            [Fact]
            public void Keeps_the_result()
            {
                var given = AuthorityRules.Result(Asked, "12");

                var answer = AuthorityRules.Refuse(given, Locked);

                answer.Result.ShouldBe("12");
            }

            [Fact]
            public void Ignores_a_refusal_outside_a_request()
            {
                var answer = AuthorityRules.Refuse(default, Locked);

                answer.Verdict.ShouldBe(Verdict.Allow);
            }
        }

        public class Result
        {
            [Fact]
            public void Keeps_the_last_result()
            {
                var first = AuthorityRules.Result(Asked, "12");

                var answer = AuthorityRules.Result(first, "40");

                answer.Result.ShouldBe("40");
            }

            [Fact]
            public void Reads_no_result_as_empty()
            {
                var first = AuthorityRules.Result(Asked, "12");

                var answer = AuthorityRules.Result(first, null);

                answer.Result.ShouldBe("");
            }

            [Fact]
            public void Ignores_a_result_outside_a_request()
            {
                var answer = AuthorityRules.Result(default, "12");

                answer.Result.ShouldBe("");
            }
        }

        public class AcceptAway
        {
            [Fact]
            public void Takes_back_the_refusal()
            {
                var refused = AuthorityRules.Refuse(Asked, Locked);

                var answer = AuthorityRules.AcceptAway(refused, "alone");

                answer.Verdict.ShouldBe(Verdict.Allow);
                answer.Result.ShouldBe("alone");
            }

            [Fact]
            public void Lets_a_later_refusal_count()
            {
                var away = AuthorityRules.AcceptAway(Asked, "alone");

                var answer = AuthorityRules.Refuse(away, Far);

                answer.Verdict.ShouldBe(Far);
            }

            [Fact]
            public void Ignores_it_outside_a_request()
            {
                var ended = AuthorityRules.End(
                    AuthorityRules.Refuse(Asked, Locked));

                var answer = AuthorityRules.AcceptAway(ended, "alone");

                answer.Verdict.ShouldBe(Locked);
            }
        }

        public class End
        {
            [Fact]
            public void Stops_handling()
            {
                var answer = AuthorityRules.End(Asked);

                answer.Handling.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_who_asked_the_refusal_and_the_result()
            {
                var refused = AuthorityRules.Result(
                    AuthorityRules.Refuse(Asked, Locked),
                    "12");

                var answer = AuthorityRules.End(refused);

                answer.From.ShouldBe(3UL);
                answer.Verdict.ShouldBe(Locked);
                answer.Result.ShouldBe("12");
            }
        }

        public class RequesterAway
        {
            [Theory]
            [InlineData(true, true, true, true)]
            [InlineData(false, true, true, false)]
            [InlineData(true, false, true, false)]
            [InlineData(true, true, false, false)]
            public void Holds_for_a_requester_whose_stand_in_is_away(
                bool handling,
                bool known,
                bool away,
                bool expected)
            {
                var result = AuthorityRules.RequesterAway(
                    handling,
                    known,
                    away);

                result.ShouldBe(expected);
            }
        }
    }
}
