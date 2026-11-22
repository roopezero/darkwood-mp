namespace DarkwoodMP.World.KeptCharacters
{
    /// One MsgType.CharacterSave's payload: a client's character now, and
    /// the chapter of the world its game is in.
    public readonly struct CharacterSaveBody
    {
        /// Player.SaveState as JSON; written as "" when null.
        public string Player { get; init; }

        /// The inventory's Inventory.SaveState as JSON.
        public string Inventory { get; init; }

        /// The hotbar's Inventory.SaveState as JSON.
        public string Hotbar { get; init; }

        /// The chapter of the world the client's game is in (0: none).
        public int Chapter { get; init; }
    }
}
