namespace DarkwoodMP.World.Switches
{
    /// What an item's sounds are doing as the game starts them again
    /// (ItemSounds.playStart, from Item.turnOn).
    public readonly struct ItemSoundFacts
    {
        /// Its loop (ItemSounds.loopSoundAO) plays: the item's loopSound,
        /// under the item.
        public bool LoopPlays { get; init; }

        /// Its start sound (ItemSounds.startSoundAO) plays, which the loop
        /// follows: the item's startSound, under the item.
        public bool StartPlays { get; init; }
    }
}
