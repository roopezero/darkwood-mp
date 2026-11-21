namespace DarkwoodMP.Combat.Nights
{
    /// Which chapter the world is in (NightSpawns.LaterChapter).
    public readonly struct ChapterFacts
    {
        /// Core.randomGeneration.
        public bool RandomGeneration { get; init; }

        /// WorldGenerator.chapterID, read only with RandomGeneration.
        public int GeneratedChapter { get; init; }

        /// The scene's object has a Location.
        public bool HasScene { get; init; }

        /// That Location's chapterId.
        public int SceneChapter { get; init; }
    }
}
