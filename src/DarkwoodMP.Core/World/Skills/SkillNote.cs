namespace DarkwoodMP.World.Skills
{
    /// Why our own game won't learn a skill or open the Skills panel. These
    /// are local: never sent, but shown or logged (SkillNotes.Text).
    public enum SkillNote
    {
        None,

        /// No player, no world, or the game is loading.
        NotInWorld,

        /// A choice is waiting for the host's answer.
        Waiting,

        /// Paused, in a game menu, dying or held.
        Busy,

        /// Watching another player (Spectator).
        Watching,

        /// The game's skill tiers aren't there.
        NoSkills,
    }
}
