namespace DarkwoodMP.World.Inventories
{
    /// What SlotShare.Set looks at about the item our player holds on the
    /// cursor when another game's contents go into a slot.
    public readonly struct HeldFacts
    {
        /// The slot gets an item (the contents aren't "").
        public bool Filling { get; init; }

        /// Our player holds an item (Controller.pickedUpItem isn't null).
        public bool Holding { get; init; }

        /// It came from this slot (pickedUpItem.slot is the slot).
        public bool FromThisSlot { get; init; }

        /// Our player is in the world (Player.Instance isn't null).
        public bool PlayerHere { get; init; }
    }
}
