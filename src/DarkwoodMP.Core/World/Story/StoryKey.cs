namespace DarkwoodMP.World.Story
{
    /// What a story key names, by its first character (StoryKeys.KindOf).
    public enum StoryKey : byte
    {
        /// Any other key of two characters or more: nothing is applied, and
        /// the baseline takes the value.
        Unknown = 0,

        /// "k", the host's karma.
        Karma,

        /// "j" and two more characters: kind, ':', name.
        Journal,

        /// "f:", a flag.
        Flag,

        /// "n:", an NPC's state.
        Npc,

        /// "d:", a dialogue.
        Dialogue,

        /// Too short for its prefix: the shell's Apply throws on it.
        Malformed,
    }
}
