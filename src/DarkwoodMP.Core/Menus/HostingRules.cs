using System.Collections.Generic;

namespace DarkwoodMP.Menus
{
    /// What a player may set for hosting. The same limits are written in
    /// three places in the plugin: Options' Multiplayer tab
    /// (OptionsWindow.DrawMultiplayer), Start Game's host box
    /// (StartGameWindow.DrawHost) and the Direct section of the
    /// Multiplayer window (DirectPanel.Draw).
    public static class HostingRules
    {
        /// The lowest port the field takes (the shell checks "above 0").
        public const int LowestPort = 1;

        /// The highest port the field takes.
        public const int HighestPort = 65535;

        /// The fewest players a game may be set to.
        public const int FewestPlayers = 2;

        /// The most players a game may be set to: the plugin's
        /// Protocol.MaxPlayers, in Net/Packets.cs.
        public const int MostPlayers = 5;

        /// The setting HostWith's value that picks Direct over Steam.
        public const string Direct = "direct";

        /// The stack sizes Options offers; 0 is the game's own.
        static readonly int[] StackChoices = { 0, 10, 20, 50, 99 };

        /// The port once the player typed into its field: what they typed
        /// when it reads as a whole number from 1 to 65535 (int.TryParse),
        /// else the port as it was.
        public static int TypedPort(string typed, int port)
        {
            int read;
            if (int.TryParse(typed, out read)
                && read >= LowestPort
                && read <= HighestPort)
            {
                return read;
            }

            return port;
        }

        /// The "-" of Max players is on while the setting is above 2.
        public static bool MayRemovePlayer(int most) => most > FewestPlayers;

        /// The "+" of Max players is on while the setting is under 5.
        public static bool MayAddPlayer(int most) => most < MostPlayers;

        /// The stack sizes Options lists: 0, 10, 20, 50 and 99, with the
        /// one set now added in order when it's none of them.
        public static List<int> StackSizes(int current)
        {
            var sizes = new List<int>(StackChoices);
            if (!sizes.Contains(current))
            {
                sizes.Add(current);
                sizes.Sort();
            }

            return sizes;
        }

        /// Whether a game is hosted with Steam: Steam is up, and HostWith
        /// isn't exactly "direct" (any other value, or none, is Steam).
        public static bool HostsWithSteam(bool steamUp, string hostWith) =>
            steamUp && hostWith != Direct;
    }
}
