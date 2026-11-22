namespace DarkwoodMP.Net
{
    /// The name the player goes by in a session (Plugin.UpdateLocalName).
    public static class PlayerNameRules
    {
        /// The name when every source is blank.
        public const string Fallback = "Player";

        /// The first name that isn't blank (null, empty or only white
        /// space): the General.PlayerName setting, then the Steam persona
        /// name (null until Steam's backend is up), then the OS user name
        /// (Environment.UserName). Only the name chosen is trimmed; with
        /// none, Fallback.
        public static string Choose(string setting, string steam, string os)
        {
            string name = setting;
            if (string.IsNullOrWhiteSpace(name))
            {
                name = steam;
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                name = os;
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                return Fallback;
            }

            return name.Trim();
        }
    }
}
