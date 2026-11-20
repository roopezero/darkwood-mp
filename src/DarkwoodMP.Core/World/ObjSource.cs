namespace DarkwoodMP.World
{
    /// Where Resolve takes an object from.
    public enum ObjSource : byte
    {
        Nothing,

        /// The table of objects made while playing together.
        Spawned,

        /// Tied to an id the host gave out.
        Shared,

        /// Found as Placed by the announced name and position, then tied
        /// when TiesAnnounced says so.
        Announced,

        /// The enemies' table, by EnemyIdOf.
        Enemy,

        /// Names nothing: a save id this game doesn't share. Warned.
        Unshared,

        /// The save's table, by SaveIdOf.
        SaveId,

        /// The object pinned to the key, even without the component.
        Pinned,

        /// The nearest of that name in the scan table (Nearest), scanned
        /// again when ScanAgain says so.
        Nearest,
    }
}
