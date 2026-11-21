namespace DarkwoodMP.World.Skills
{
    /// What a footer line or a skill's details say on the Skills panel.
    public enum PanelNote
    {
        /// Nothing (an empty line).
        None,

        /// The last Learn's reason, which the shell keeps.
        Message,

        Asking,
        NoPoints,
        ChooseNegative,
        Learned,
        TakenWithFirst,
        ComesWithNegative,
        CanBeLearned,

        /// A refusal's words (Refusals.Text of the verdict).
        Refused,
    }
}
