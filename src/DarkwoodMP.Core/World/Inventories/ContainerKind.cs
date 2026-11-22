namespace DarkwoodMP.World.Inventories
{
    /// What kind of inventory a container is (the game's Inventory.invType).
    /// Other is every kind the mod doesn't share as a container.
    public enum ContainerKind : byte
    {
        Other = 0,

        /// Inventory.InvType.itemInv: a chest, a crate, a dropped item.
        Item,

        /// Inventory.InvType.deathDrop: what a dead player left.
        DeathDrop,

        /// Inventory.InvType.shop: a trader's stock.
        Shop,
    }
}
