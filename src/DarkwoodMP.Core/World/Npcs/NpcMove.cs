namespace DarkwoodMP.World.Npcs
{
    /// What a client does with its copy of an NPC the host placed.
    public enum NpcMove
    {
        /// No object has the save id here: leave it, and keep the target.
        Skip,

        /// It can't be moved (dead, gone, or run by NetEnemies): forget
        /// the target.
        Forget,

        /// Close enough: leave it.
        Leave,

        /// Far off: put it at NpcStep.To at once, and register it to the
        /// world grid's node there (Recull).
        Snap,

        /// Move it to NpcStep.To, a step toward the host's.
        Step,
    }
}
