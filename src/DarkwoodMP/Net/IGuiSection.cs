namespace DarkwoodMP.Net
{
    /// <summary>A backend that draws its own part of the co-op panel (keeps backend-specific types out of Plugin).</summary>
    public interface IGuiSection
    {
        void DrawGui(Session session, Flow flow);
        /// <summary>A display name to default to (e.g. the Steam persona name), or null.</summary>
        string SuggestedPlayerName { get; }
        /// <summary>Start hosting with this backend (the Start Game window's host box).</summary>
        void Host();
    }
}
