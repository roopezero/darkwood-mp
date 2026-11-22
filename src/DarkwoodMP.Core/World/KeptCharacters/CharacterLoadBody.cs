namespace DarkwoodMP.World.KeptCharacters
{
    /// One MsgType.CharacterLoad's payload: which character, its strings
    /// and where the host stands.
    public readonly struct CharacterLoadBody
    {
        /// The kind byte as sent; a value with no name is kept.
        public CharacterKind Kind { get; init; }

        /// Player.SaveState as JSON; written as "" when null.
        public string Player { get; init; }

        /// The inventory's Inventory.SaveState as JSON.
        public string Inventory { get; init; }

        /// The hotbar's Inventory.SaveState as JSON.
        public string Hotbar { get; init; }

        /// Where the host stands (zero: no host player).
        public Vec3 At { get; init; }
    }
}
