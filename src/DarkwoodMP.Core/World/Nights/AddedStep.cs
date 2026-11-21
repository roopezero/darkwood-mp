namespace DarkwoodMP.World.Nights
{
    /// What the host does with an object Core.AddPrefab made.
    public enum AddedStep : byte
    {
        /// Nothing: not a night object (or an enemy, NetEnemies' to send).
        Nothing = 0,

        /// A night event's copy of its GameEvents: tag each step
        /// (scenario, event, group, step), replacing an older tag.
        Tag,

        /// A tagged spawn step's object: give it an id, record it, and
        /// send it as Made while sharing.
        Record,
    }
}
