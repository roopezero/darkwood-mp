using DarkwoodMP.World;
using DarkwoodMP.World.Windows;

namespace DarkwoodMP.Tests.World.Windows
{
    public class WindowRulesTests
    {
        /// A request being handled, from a player standing at the origin.
        static readonly ReachFacts Requester = new ReachFacts
        {
            Handling = true,
            Known = true,
            StandIn = new Vec2(0f, 0f),
            Target = new Vec2(100f, 0f),
            What = "Window",
        };

        /// A window with no barricade near the requester.
        static readonly WindowFacts Bare = new WindowFacts
        {
            Exists = true,
            Requester = Requester,
        };

        static readonly WindowFacts Nailed = Bare with { Barricaded = true };

        static readonly WindowFacts Missing = Bare with { Exists = false };

        /// The bare window, x away from the requester.
        static WindowFacts At(float x)
        {
            var far = Requester with { Target = new Vec2(x, 0f) };
            return Bare with { Requester = far };
        }

        static readonly WindowState Before = new WindowState
        {
            Barricaded = false,
            BarricadeHealth = 0,
        };

        /// The outermost call, while sharing, that barricaded the window.
        static readonly WindowSendFacts Barricaded = new WindowSendFacts
        {
            Outermost = true,
            Sharing = true,
            Before = Before,
            After = new WindowState { Barricaded = true, BarricadeHealth = 3 },
        };

        public class Limits
        {
            [Fact]
            public void Pins_the_reaches()
            {
                var reaches = new[]
                {
                    WindowRules.Reach,
                    WindowRules.ShotReach,
                };

                reaches.ShouldBe(new[] { 600f, 1200f });
            }

            [Fact]
            public void Pins_the_largest_hit()
            {
                WindowRules.MaxHit.ShouldBe(1000f);
            }
        }

        public class MayBarricade
        {
            [Fact]
            public void Allows_a_window_with_no_barricade()
            {
                var verdict = WindowRules.MayBarricade(Bare);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Refuses_a_barricaded_window()
            {
                var verdict = WindowRules.MayBarricade(Nailed);

                verdict.Reason.ShouldBe(Refusal.WindowBarricaded);
            }

            [Fact]
            public void Refuses_a_window_that_isnt_there()
            {
                var verdict = WindowRules.MayBarricade(Missing);

                verdict.Reason.ShouldBe(Refusal.NoSuchWindow);
            }

            [Fact]
            public void Allows_a_window_exactly_at_the_reach()
            {
                var verdict = WindowRules.MayBarricade(At(600f));

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Refuses_a_window_just_past_the_reach()
            {
                var verdict = WindowRules.MayBarricade(At(600.5f));

                verdict.Reason.ShouldBe(Refusal.OutOfReach);
            }

            [Fact]
            public void Words_the_reach_with_the_windows_name()
            {
                var verdict = WindowRules.MayBarricade(At(700f));

                var text = Refusals.Text(verdict);

                text.ShouldBe("Window is 700 away from them (reach 600)");
            }

            [Fact]
            public void Prefers_the_reach_to_a_barricade()
            {
                var window = At(600.5f) with { Barricaded = true };

                var verdict = WindowRules.MayBarricade(window);

                verdict.Reason.ShouldBe(Refusal.OutOfReach);
            }

            [Fact]
            public void Prefers_a_missing_window_to_the_reach()
            {
                var window = At(4041f) with { Exists = false };

                var verdict = WindowRules.MayBarricade(window);

                verdict.Reason.ShouldBe(Refusal.NoSuchWindow);
            }
        }

        public class MayDestroyBarricade
        {
            [Fact]
            public void Allows_a_barricaded_window()
            {
                var verdict = WindowRules.MayDestroyBarricade(Nailed);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Refuses_a_window_with_no_barricade()
            {
                var verdict = WindowRules.MayDestroyBarricade(Bare);

                verdict.Reason.ShouldBe(Refusal.WindowNotBarricaded);
            }

            [Fact]
            public void Refuses_a_window_just_past_the_reach()
            {
                var window = At(600.5f) with { Barricaded = true };

                var verdict = WindowRules.MayDestroyBarricade(window);

                verdict.Reason.ShouldBe(Refusal.OutOfReach);
            }
        }

        public class MayHit
        {
            [Fact]
            public void Allows_a_hit_exactly_at_the_shot_reach()
            {
                var verdict = WindowRules.MayHit(At(1200f), 10f);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Refuses_a_hit_just_past_the_shot_reach()
            {
                var verdict = WindowRules.MayHit(At(1200.5f), 10f);

                verdict.Reason.ShouldBe(Refusal.OutOfReach);
            }

            [Fact]
            public void Allows_a_hit_exactly_at_the_largest()
            {
                var verdict = WindowRules.MayHit(Nailed, 1000f);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Refuses_a_hit_just_past_the_largest()
            {
                var verdict = WindowRules.MayHit(Nailed, 1000.5f);

                verdict.Reason.ShouldBe(Refusal.HitTooLarge);
            }

            [Fact]
            public void Words_the_hit_as_a_whole_number()
            {
                var verdict = WindowRules.MayHit(Nailed, 1500f);

                var text = Refusals.Text(verdict);

                text.ShouldBe("a hit of 1500 is more than any weapon does");
            }

            [Fact]
            public void Allows_a_negative_hit()
            {
                var verdict = WindowRules.MayHit(Nailed, -50f);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Allows_a_hit_on_a_window_with_no_barricade()
            {
                var verdict = WindowRules.MayHit(Bare, 10f);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Prefers_the_reach_to_a_hit_too_large()
            {
                var verdict = WindowRules.MayHit(At(1200.5f), 1500f);

                verdict.Reason.ShouldBe(Refusal.OutOfReach);
            }
        }

        public class MayAct
        {
            [Theory]
            [InlineData(11, Refusal.WindowBarricaded)]
            [InlineData(12, Refusal.None)]
            [InlineData(13, Refusal.None)]
            [InlineData(14, Refusal.None)]
            [InlineData(20, Refusal.None)]
            public void Checks_each_action_by_its_value(
                byte action,
                Refusal reason)
            {
                var act = (WindowAction)action;

                var verdict = WindowRules.MayAct(Nailed, act, 0f);

                verdict.Reason.ShouldBe(reason);
            }

            [Theory]
            [InlineData(13, Refusal.None)]
            [InlineData(11, Refusal.OutOfReach)]
            [InlineData(14, Refusal.OutOfReach)]
            public void Gives_only_a_hit_the_shot_reach(
                byte action,
                Refusal reason)
            {
                var window = At(1000f);
                var act = (WindowAction)action;

                var verdict = WindowRules.MayAct(window, act, 0f);

                verdict.Reason.ShouldBe(reason);
            }

            [Fact]
            public void Passes_x_to_a_hit_as_its_damage()
            {
                var hit = WindowAction.Hit;

                var verdict = WindowRules.MayAct(Nailed, hit, 1500f);

                verdict.Reason.ShouldBe(Refusal.HitTooLarge);
            }
        }

        public class SendsChange
        {
            [Fact]
            public void Sends_a_call_that_changed_the_window()
            {
                var sends = WindowRules.SendsChange(Barricaded);

                sends.ShouldBeTrue();
            }

            [Fact]
            public void Sends_a_change_to_the_barricades_health()
            {
                var after = Before with { BarricadeHealth = 2 };
                var send = Barricaded with { After = after };

                var sends = WindowRules.SendsChange(send);

                sends.ShouldBeTrue();
            }

            [Fact]
            public void Keeps_a_call_that_changed_nothing()
            {
                var send = Barricaded with { After = Before };

                var sends = WindowRules.SendsChange(send);

                sends.ShouldBeFalse();
            }

            [Fact]
            public void Keeps_a_call_inside_another()
            {
                var send = Barricaded with { Outermost = false };

                var sends = WindowRules.SendsChange(send);

                sends.ShouldBeFalse();
            }

            [Fact]
            public void Keeps_a_call_while_not_sharing()
            {
                var send = Barricaded with { Sharing = false };

                var sends = WindowRules.SendsChange(send);

                sends.ShouldBeFalse();
            }
        }

        public class RollBack
        {
            [Theory]
            [InlineData(true, false, WindowFix.Barricade)]
            [InlineData(false, true, WindowFix.RemoveBarricade)]
            [InlineData(true, true, WindowFix.None)]
            [InlineData(false, false, WindowFix.None)]
            public void Matches_the_hosts_barricade(
                bool host,
                bool ours,
                WindowFix fix)
            {
                var done = WindowRules.RollBack(host, ours);

                done.ShouldBe(fix);
            }
        }
    }
}
