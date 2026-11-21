namespace DarkwoodMP.Sync.Players
{
    /// When the map of sprite collections by name hash is built again, and
    /// when another player's animation library is loaded for their look
    /// (SpriteCollections.Get).
    public static class SpriteCacheRules
    {
        /// Seconds (unscaled) that must pass between two rebuilds.
        public const float RescanEvery = 0.5f;

        /// When the map was last built at the start, and after a library
        /// loads: long enough ago that the next miss rebuilds it.
        public const float NeverScanned = -999f;

        /// On a miss the map is built again when more than RescanEvery has
        /// passed since the last build (strictly).
        public static bool Rescans(Seconds now, Seconds last) =>
            now.Value - last.Value > RescanEvery;

        /// A collection still missing after that may be in the animation
        /// library their body uses: it's loaded when one is named and it
        /// wasn't tried before in this process, even if that try failed.
        public static bool LoadsLibrary(bool found, string library, bool tried)
        {
            if (found || string.IsNullOrEmpty(library))
            {
                return false;
            }

            return !tried;
        }
    }
}
