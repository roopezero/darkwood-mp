namespace DarkwoodMP.World.Story
{
    /// What a player crawling to their death does to the tally
    /// (EpilogueSync.Crawled).
    public enum CrawlStep : byte
    {
        /// They were counted already: nothing.
        Repeat = 0,

        /// The first death: start the pan's timeout, then check the pan.
        First,

        /// Counted: check the pan.
        Counted,
    }
}
