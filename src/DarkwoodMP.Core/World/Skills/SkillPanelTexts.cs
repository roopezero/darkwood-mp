namespace DarkwoodMP.World.Skills
{
    /// The Skills panel's own words, as SkillsPanel says them today.
    public static class SkillPanelTexts
    {
        public const string Choose = "Choose a skill to learn.";

        public const string TakeLevel =
            "Take a level at a stove for a skill point.";

        /// The note's words; empty for None, Message and Refused, whose
        /// words come from elsewhere.
        public static string Text(PanelNote note)
        {
            switch (note)
            {
                case PanelNote.Asking:
                    return "Asking the host...";
                case PanelNote.NoPoints:
                    return "No skill points to spend";
                case PanelNote.ChooseNegative:
                    return "Choose a negative skill of its tier to go with it";
                case PanelNote.Learned:
                    return "Learned";
                case PanelNote.TakenWithFirst:
                    return "Taken with the first skill of its tier";
                case PanelNote.ComesWithNegative:
                    return "Comes with a negative skill of its tier";
                case PanelNote.CanBeLearned:
                    return "Can be learned";
                default:
                    return "";
            }
        }

        /// The hint shown when no skill is under the mouse or picked.
        public static string Hint(int points)
        {
            if (points > 0)
            {
                return Choose;
            }

            return TakeLevel;
        }

        /// The text with its first letter made capital (SkillsPanel.Capital);
        /// null and "" stay as they are.
        public static string Capital(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }

            return char.ToUpperInvariant(text[0]) + text.Substring(1);
        }
    }
}
