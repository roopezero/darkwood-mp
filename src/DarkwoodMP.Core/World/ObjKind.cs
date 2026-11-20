namespace DarkwoodMP.World
{
    /// The kinds of name, with the plugin's ObjRef.Kind values (on the
    /// wire, so they never change).
    public enum ObjKind : byte
    {
        None = 0,

        /// From the shared save: its save id.
        Saved = 1,

        /// Made while playing together, or given an id by the host.
        Spawned = 2,

        /// One of the host's enemies: its net id.
        Enemy = 3,

        /// Anything else: its name and position.
        Placed = 4,
    }
}
