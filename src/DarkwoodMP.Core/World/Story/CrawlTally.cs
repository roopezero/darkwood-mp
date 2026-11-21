using System.Collections.Generic;

namespace DarkwoodMP.World.Story
{
    /// Host: who has crawled to their death in the burning forest, our own
    /// player too (EpilogueSync._crawled and _firstDeath). One per
    /// epilogue; Reset clears it.
    public sealed class CrawlTally
    {
        readonly HashSet<ulong> _crawled = new HashSet<ulong>();

        /// Someone has crawled to their death (_firstDeath is set).
        public bool AnyDeath { get; private set; }

        /// Count a player's death (EpilogueSync.Crawled).
        public CrawlStep Crawled(ulong who)
        {
            if (!_crawled.Add(who))
            {
                return CrawlStep.Repeat;
            }

            if (!AnyDeath)
            {
                AnyDeath = true;
                return CrawlStep.First;
            }

            return CrawlStep.Counted;
        }

        /// Whether the pan is released for everyone now
        /// (EpilogueSync.CheckPan): not released yet, Playing, on the host,
        /// someone dead, and the host's own player and every peer counted.
        public bool ReleasesPan(in PanFacts facts)
        {
            if (facts.Released
                || !facts.Playing
                || !facts.IsHost
                || !AnyDeath)
            {
                return false;
            }

            if (!_crawled.Contains(facts.Host))
            {
                return false;
            }

            if (facts.Peers == null)
            {
                return true;
            }

            foreach (var peer in facts.Peers)
            {
                if (!_crawled.Contains(peer))
                {
                    return false;
                }
            }

            return true;
        }

        /// Forget every death (EpilogueSync.Reset).
        public void Reset()
        {
            _crawled.Clear();
            AnyDeath = false;
        }
    }
}
