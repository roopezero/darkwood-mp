using DarkwoodMP.Combat.Joiners;

namespace DarkwoodMP.Tests.Combat.Joiners
{
    public class ViewRectSenderTests
    {
        /// A client in the host's world at unscaled time 10.
        static readonly RectSendFacts InWorld = new RectSendFacts
        {
            Client = true,
            InWorld = true,
            Controller = true,
            Now = new Seconds(10f),
        };

        static readonly ViewRect Screen = new ViewRect(1200, 800);

        public class Due
        {
            [Fact]
            public void Checks_at_once()
            {
                var sender = new ViewRectSender();

                sender.Due(InWorld).ShouldBeTrue();
            }

            [Fact]
            public void Sets_the_next_check_two_seconds_on()
            {
                var sender = new ViewRectSender();

                sender.Due(InWorld);

                sender.NextCheck.Value.ShouldBe(12f);
            }

            [Fact]
            public void Waits_just_before_the_next_check()
            {
                var sender = new ViewRectSender();
                sender.Due(InWorld);
                var later = InWorld with { Now = new Seconds(11.9f) };

                sender.Due(later).ShouldBeFalse();
            }

            [Fact]
            public void Checks_exactly_at_the_next_check()
            {
                var sender = new ViewRectSender();
                sender.Due(InWorld);
                var later = InWorld with { Now = new Seconds(12f) };

                sender.Due(later).ShouldBeTrue();
            }

            [Fact]
            public void Skips_on_the_host()
            {
                var sender = new ViewRectSender();
                var host = InWorld with { Client = false };

                sender.Due(host).ShouldBeFalse();
            }

            [Fact]
            public void Skips_outside_the_host_s_world()
            {
                var sender = new ViewRectSender();
                var menu = InWorld with { InWorld = false };

                sender.Due(menu).ShouldBeFalse();
            }

            [Fact]
            public void Skips_with_no_controller()
            {
                var sender = new ViewRectSender();
                var loading = InWorld with { Controller = false };

                sender.Due(loading).ShouldBeFalse();
            }

            [Fact]
            public void Keeps_the_next_check_when_skipped()
            {
                var sender = new ViewRectSender();
                var menu = InWorld with { InWorld = false };

                sender.Due(menu);

                sender.NextCheck.Value.ShouldBe(0f);
            }
        }

        public class Sends
        {
            [Fact]
            public void Sends_a_new_rect()
            {
                var sender = new ViewRectSender();

                sender.Sends(Screen).ShouldBeTrue();
            }

            [Fact]
            public void Counts_it_sent()
            {
                var sender = new ViewRectSender();

                sender.Sends(Screen);

                sender.Sent.ShouldBe(Screen);
            }

            [Fact]
            public void Skips_the_rect_already_sent()
            {
                var sender = new ViewRectSender();
                sender.Sends(Screen);

                sender.Sends(Screen).ShouldBeFalse();
            }

            [Fact]
            public void Skips_a_rect_with_no_width()
            {
                var sender = new ViewRectSender();

                sender.Sends(new ViewRect(0, 800)).ShouldBeFalse();
            }
        }

        public class EndSession
        {
            [Fact]
            public void Sends_the_same_rect_again_after_it()
            {
                var sender = new ViewRectSender();
                sender.Sends(Screen);

                sender.EndSession();

                sender.Sends(Screen).ShouldBeTrue();
            }

            [Fact]
            public void Keeps_the_next_check()
            {
                var sender = new ViewRectSender();
                sender.Due(InWorld);

                sender.EndSession();

                sender.NextCheck.Value.ShouldBe(12f);
            }
        }
    }
}
