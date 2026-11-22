namespace DarkwoodMP.World.Inventories
{
    /// What Containers.MadeFrom does with something the game made.
    public enum MadeFromStep : byte
    {
        /// Nothing: not placing, or not ours to share.
        Ignore = 0,

        /// Left by a thrown item: shared at once (ShareThrownSpawn).
        Thrown,

        /// Placed, but no placeable prefab: a warning, not shared.
        NotPlaceable,

        /// Placed: shared by its placeable path (Made).
        Share,
    }
}
