using DarkwoodMP.World.Story;

namespace DarkwoodMP.Tests.World.Story
{
    public class CrawlTallyTests
    {
        const ulong Host = 1;

        /// The host of two joiners, playing the epilogue, pan not released.
        static readonly PanFacts Playing = new PanFacts
        {
            Playing = true,
            IsHost = true,
            Host = Host,
            Peers = new ulong[] { 2, 3 },
        };

        static CrawlTally AllCrawled()
        {
            var tally = new CrawlTally();
            tally.Crawled(Host);
            tally.Crawled(2);
            tally.Crawled(3);
            return tally;
        }

        public class Crawled
        {
            [Fact]
            public void Takes_the_first_death_as_the_first()
            {
                var tally = new CrawlTally();

                tally.Crawled(2).ShouldBe(CrawlStep.First);
                tally.AnyDeath.ShouldBeTrue();
            }

            [Fact]
            public void Counts_a_later_death()
            {
                var tally = new CrawlTally();
                tally.Crawled(2);

                tally.Crawled(3).ShouldBe(CrawlStep.Counted);
            }

            [Fact]
            public void Counts_each_player_once()
            {
                var tally = new CrawlTally();
                tally.Crawled(2);

                tally.Crawled(2).ShouldBe(CrawlStep.Repeat);
            }
        }

        public class ReleasesPan
        {
            [Fact]
            public void Releases_it_once_everyone_crawled()
            {
                AllCrawled().ReleasesPan(Playing).ShouldBeTrue();
            }

            [Fact]
            public void Waits_for_a_peer_still_crawling()
            {
                var tally = new CrawlTally();
                tally.Crawled(Host);
                tally.Crawled(2);

                tally.ReleasesPan(Playing).ShouldBeFalse();
            }

            [Fact]
            public void Waits_for_the_hosts_own_player()
            {
                var tally = new CrawlTally();
                tally.Crawled(2);
                tally.Crawled(3);

                tally.ReleasesPan(Playing).ShouldBeFalse();
            }

            [Fact]
            public void Releases_it_for_the_host_alone_with_no_peers()
            {
                var tally = new CrawlTally();
                tally.Crawled(Host);
                var facts = Playing with { Peers = new ulong[0] };

                tally.ReleasesPan(facts).ShouldBeTrue();
            }

            [Fact]
            public void Waits_for_a_death()
            {
                var facts = Playing with { Peers = null };

                new CrawlTally().ReleasesPan(facts).ShouldBeFalse();
            }

            [Fact]
            public void Doesnt_release_it_twice()
            {
                var facts = Playing with { Released = true };

                AllCrawled().ReleasesPan(facts).ShouldBeFalse();
            }

            [Fact]
            public void Doesnt_release_it_out_of_the_epilogue()
            {
                var facts = Playing with { Playing = false };

                AllCrawled().ReleasesPan(facts).ShouldBeFalse();
            }

            [Fact]
            public void Doesnt_release_it_on_a_joiner()
            {
                var facts = Playing with { IsHost = false };

                AllCrawled().ReleasesPan(facts).ShouldBeFalse();
            }
        }

        public class Reset
        {
            [Fact]
            public void Forgets_every_death()
            {
                var tally = AllCrawled();

                tally.Reset();

                tally.AnyDeath.ShouldBeFalse();
                tally.Crawled(2).ShouldBe(CrawlStep.First);
            }
        }
    }
}
