namespace DarkwoodMP.World.KeptCharacters
{
    /// <summary>
    /// One kept character, as written to its JSON file.
    /// </summary>
    public sealed class CharacterRecord
    {
        public string Name;
        // Player.SaveState
        public string Player;
        // Inventory.SaveState of the player's inventory
        public string Inv;
        // Inventory.SaveState of the hotbar
        public string Hotbar;
        // unix time
        public long Saved;
        // the chapter the joiner's game was in when it was sent (0: not
        // known, from before it was kept) died for good (CharacterDead): it
        // comes back only to watch the others
        public int Chapter;
        public bool Dead;
    }
}
