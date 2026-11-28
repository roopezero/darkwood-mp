namespace DarkwoodMP.World.Switches
{
    /// Whether an item's start and loop sounds start again (SwitchSync).
    /// Item.turnOn starts them each time it's called, also on an item that
    /// is on already, and ItemSounds keeps only the newest: the older loop
    /// then plays for good, since a switch-off stops only the one it
    /// knows. The morning after a night calls it again on everything a
    /// hideout's generator powers, the generator among them
    /// (Location.enableAllLights, Item.enable), and a replayed switch-on
    /// can come on top of one the game did itself.
    public static class ItemSoundRules
    {
        /// They start unless the item's own loop or start sound is still
        /// playing: then the one playing goes on.
        public static bool Starts(in ItemSoundFacts sounds) =>
            !sounds.LoopPlays && !sounds.StartPlays;
    }
}
