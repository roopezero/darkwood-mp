namespace DarkwoodMP.World.Breaking
{
    /// What a game sends about its own player's change to an item.
    public enum ItemSend : byte
    {
        Nothing = 0,

        /// EggSync's EggDestroyed: an egg broke.
        EggDestroyed,

        /// WorldActions' ItemHit, with the damage and the health after.
        ItemHit,

        /// WorldActions' ItemBroken.
        ItemBroken,
    }
}
