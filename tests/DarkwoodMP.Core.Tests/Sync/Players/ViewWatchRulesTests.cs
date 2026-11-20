using DarkwoodMP.Sync.Players;

namespace DarkwoodMP.Tests.Sync.Players
{
    public class ViewWatchRulesTests
    {
        /// A roof logged at 100.
        static readonly ViewLog Roof = new ViewLog
        {
            Hiders = "Roof",
            At = new Seconds(100f),
        };

        public class Limits
        {
            [Fact]
            public void Pins_the_watch_times()
            {
                var times = new[]
                {
                    ViewWatchRules.Every,
                    ViewWatchRules.Repeat,
                };

                times.ShouldBe(new[] { 0.25f, 30f });
            }
        }

        public class Looks
        {
            [Fact]
            public void Looks_exactly_when_due()
            {
                var looks = ViewWatchRules.Looks(
                    true, new Seconds(1f), new Seconds(1f));

                looks.ShouldBeTrue();
            }

            [Fact]
            public void Waits_until_due()
            {
                var looks = ViewWatchRules.Looks(
                    true, new Seconds(0.99f), new Seconds(1f));

                looks.ShouldBeFalse();
            }

            [Fact]
            public void Never_looks_out_of_a_session()
            {
                var looks = ViewWatchRules.Looks(
                    false, new Seconds(5f), new Seconds(1f));

                looks.ShouldBeFalse();
            }
        }

        public class NextLook
        {
            [Fact]
            public void Looks_four_times_a_second()
            {
                var next = ViewWatchRules.NextLook(new Seconds(1f));

                next.Value.ShouldBe(1.25f);
            }
        }

        public class Logs
        {
            [Fact]
            public void Logs_other_hiders_at_once()
            {
                var logs = ViewWatchRules.Logs(
                    Roof, "Tree", new Seconds(101f));

                logs.ShouldBeTrue();
            }

            [Fact]
            public void Holds_the_same_hiders_just_under_30_seconds()
            {
                var logs = ViewWatchRules.Logs(
                    Roof, "Roof", new Seconds(129.9f));

                logs.ShouldBeFalse();
            }

            [Fact]
            public void Logs_the_same_hiders_again_at_30_seconds()
            {
                var logs = ViewWatchRules.Logs(
                    Roof, "Roof", new Seconds(130f));

                logs.ShouldBeTrue();
            }

            [Fact]
            public void Logs_hiders_again_after_the_view_cleared()
            {
                var cleared = ViewWatchRules.Clear(Roof);

                var logs = ViewWatchRules.Logs(
                    cleared, "Roof", new Seconds(101f));

                logs.ShouldBeTrue();
            }

            [Fact]
            public void Holds_an_empty_set_in_the_first_30_seconds()
            {
                var logs = ViewWatchRules.Logs(
                    default, "", new Seconds(29f));

                logs.ShouldBeFalse();
            }
        }

        public class Clear
        {
            [Fact]
            public void Keeps_when_it_last_logged()
            {
                var cleared = ViewWatchRules.Clear(Roof);

                (cleared.Hiders, cleared.At.Value).ShouldBe(("", 100f));
            }
        }

        public class Logged
        {
            [Fact]
            public void Remembers_the_hiders_and_the_time()
            {
                var log = ViewWatchRules.Logged("Tree", new Seconds(7f));

                (log.Hiders, log.At.Value).ShouldBe(("Tree", 7f));
            }
        }
    }
}
