using DarkwoodMP.Menus;

namespace DarkwoodMP.Tests.Menus
{
    public class JoinRulesTests
    {
        /// A server that answered, with no password, joined from no game.
        static readonly ConnectFacts Open = new ConnectFacts
        {
            Address = "192.168.1.20:27015",
            Known = true,
        };

        static readonly ConnectFacts Locked = Open with
        {
            NeedsPassword = true,
        };

        /// A second click on the server clicked a moment ago.
        static readonly ClickFacts Twice = new ClickFacts
        {
            Address = "192.168.1.20:27015",
            Last = "192.168.1.20:27015",
            Now = new Seconds(10.3f),
            LastAt = new Seconds(10f),
        };

        public class Limits
        {
            [Fact]
            public void Pins_the_double_click_time()
            {
                var time = JoinRules.DoubleClick.Value;

                time.ShouldBe(0.4f);
            }
        }

        public class MayJoin
        {
            [Theory]
            [InlineData(true, false, true)]
            [InlineData(true, true, false)]
            [InlineData(false, false, true)]
            [InlineData(false, true, true)]
            public void Refuses_only_a_server_of_another_version(
                bool known,
                bool incompatible,
                bool may)
            {
                var result = JoinRules.MayJoin(known, incompatible);

                result.ShouldBe(may);
            }
        }

        public class Connect
        {
            [Fact]
            public void Joins_from_no_game()
            {
                var plan = JoinRules.Connect(Open);

                plan.Step.ShouldBe(ConnectStep.Join);
                plan.LeaveFirst.ShouldBeFalse();
            }

            [Fact]
            public void Joins_a_server_that_has_not_answered()
            {
                var join = Open with { Known = false };

                var plan = JoinRules.Connect(join);

                plan.Step.ShouldBe(ConnectStep.Join);
            }

            [Fact]
            public void Leaves_a_game_as_a_joiner_first()
            {
                var join = Open with { InGame = true };

                var plan = JoinRules.Connect(join);

                plan.Step.ShouldBe(ConnectStep.Join);
                plan.LeaveFirst.ShouldBeTrue();
            }

            [Fact]
            public void Does_nothing_with_no_address()
            {
                var join = Open with { Address = null };

                var plan = JoinRules.Connect(join);

                plan.Step.ShouldBe(ConnectStep.Nothing);
            }

            [Fact]
            public void Does_nothing_for_another_version()
            {
                var join = Locked with { Incompatible = true };

                var plan = JoinRules.Connect(join);

                plan.Step.ShouldBe(ConnectStep.Nothing);
            }

            [Fact]
            public void Asks_for_a_password_the_server_needs()
            {
                var plan = JoinRules.Connect(Locked);

                plan.Step.ShouldBe(ConnectStep.AskPassword);
                plan.LeaveFirst.ShouldBeFalse();
            }

            [Fact]
            public void Joins_with_a_password_given()
            {
                var join = Locked with { PasswordGiven = true, InGame = true };

                var plan = JoinRules.Connect(join);

                plan.Step.ShouldBe(ConnectStep.Join);
                plan.LeaveFirst.ShouldBeTrue();
            }

            [Fact]
            public void Does_nothing_while_hosting()
            {
                var join = Open with { InGame = true, Host = true };

                var plan = JoinRules.Connect(join);

                plan.Step.ShouldBe(ConnectStep.Nothing);
            }

            [Fact]
            public void Asks_a_host_for_the_password_before_doing_nothing()
            {
                var join = Locked with { InGame = true, Host = true };

                var plan = JoinRules.Connect(join);

                plan.Step.ShouldBe(ConnectStep.AskPassword);
            }

            [Fact]
            public void Joins_when_host_is_set_with_no_session()
            {
                var join = Open with { Host = true };

                var plan = JoinRules.Connect(join);

                plan.Step.ShouldBe(ConnectStep.Join);
            }
        }

        public class IsDoubleClick
        {
            [Fact]
            public void Holds_for_a_quick_second_click()
            {
                var twice = JoinRules.IsDoubleClick(Twice);

                twice.ShouldBeTrue();
            }

            [Fact]
            public void Holds_just_under_the_time()
            {
                var click = Twice with { Now = new Seconds(10.39f) };

                var twice = JoinRules.IsDoubleClick(click);

                twice.ShouldBeTrue();
            }

            [Fact]
            public void Refuses_a_click_exactly_at_the_time()
            {
                var click = Twice with
                {
                    Now = new Seconds(0.4f),
                    LastAt = new Seconds(0f),
                };

                var twice = JoinRules.IsDoubleClick(click);

                twice.ShouldBeFalse();
            }

            [Fact]
            public void Refuses_a_slow_second_click()
            {
                var click = Twice with { Now = new Seconds(10.5f) };

                var twice = JoinRules.IsDoubleClick(click);

                twice.ShouldBeFalse();
            }

            [Fact]
            public void Refuses_a_click_on_another_server()
            {
                var click = Twice with { Last = "10.0.0.5:27015" };

                var twice = JoinRules.IsDoubleClick(click);

                twice.ShouldBeFalse();
            }

            [Fact]
            public void Refuses_the_first_click_after_the_game_starts()
            {
                var click = Twice with
                {
                    Last = null,
                    Now = new Seconds(0.1f),
                    LastAt = default,
                };

                var twice = JoinRules.IsDoubleClick(click);

                twice.ShouldBeFalse();
            }
        }
    }
}
