namespace DarkwoodMP.World.Skills
{
    /// The words for each SkillNote, as the plugin's code says them today.
    public static class SkillNotes
    {
        /// The note's words; empty for None.
        public static string Text(SkillNote note)
        {
            switch (note)
            {
                case SkillNote.NotInWorld:
                    return "not in a world";
                case SkillNote.Waiting:
                    return "waiting for the host";
                case SkillNote.Busy:
                    return "busy";
                case SkillNote.Watching:
                    return "watching";
                case SkillNote.NoSkills:
                    return "no skills";
                default:
                    return "";
            }
        }
    }
}
