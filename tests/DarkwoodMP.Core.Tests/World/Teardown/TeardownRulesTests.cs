using DarkwoodMP.World.Teardown;

namespace DarkwoodMP.Tests.World.Teardown
{
    public class TeardownRulesTests
    {
        /// A scan with graph updates queued.
        static readonly ScanFacts Queued = new ScanFacts
        {
            Playing = true,
            Graphs = true,
            UpdatesQueued = true,
        };

        public class Flushes
        {
            [Fact]
            public void Finishes_queued_updates()
            {
                TeardownRules.Flushes(Queued).ShouldBeTrue();
            }

            [Fact]
            public void Finishes_queued_work_items()
            {
                var flushes = TeardownRules.Flushes(Queued with
                {
                    UpdatesQueued = false,
                    WorkQueued = true,
                });

                flushes.ShouldBeTrue();
            }

            [Fact]
            public void Does_nothing_with_nothing_queued()
            {
                var flushes = TeardownRules.Flushes(Queued with
                {
                    UpdatesQueued = false,
                });

                flushes.ShouldBeFalse();
            }

            [Fact]
            public void Does_nothing_when_not_playing()
            {
                var flushes = TeardownRules.Flushes(Queued with
                {
                    Playing = false,
                });

                flushes.ShouldBeFalse();
            }

            [Fact]
            public void Does_nothing_without_graphs()
            {
                var flushes = TeardownRules.Flushes(Queued with
                {
                    Graphs = false,
                });

                flushes.ShouldBeFalse();
            }

            [Fact]
            public void Does_nothing_while_scanning()
            {
                var flushes = TeardownRules.Flushes(Queued with
                {
                    Scanning = true,
                });

                flushes.ShouldBeFalse();
            }

            [Fact]
            public void Does_nothing_inside_a_work_item()
            {
                var flushes = TeardownRules.Flushes(Queued with
                {
                    InWorkItem = true,
                });

                flushes.ShouldBeFalse();
            }
        }
    }
}
