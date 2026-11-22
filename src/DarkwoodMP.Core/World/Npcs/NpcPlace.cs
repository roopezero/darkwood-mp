namespace DarkwoodMP.World.Npcs
{
    /// Where the host has one NPC, as an NpcPos carries it.
    public readonly struct NpcPlace
    {
        /// Its save id, one every game shares.
        public int Id { get; init; }

        /// Where it stands, x and z.
        public Vec2 At { get; init; }
    }
}
