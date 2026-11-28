namespace DarkwoodMP.World.KeptCharacters
{
    /// Which of our player's states a client sends as its character.
    public enum SentState
    {
        /// What the player has and where it stands now.
        Current = 0,

        /// What the game put away as a dream began (Dreams'
        /// inventorySlotsCopy, hotbarSlotsCopy, positionCopy and
        /// effectsCopy): the bags, place and effects the dream's end gives
        /// back.
        BeforeDream,
    }
}
