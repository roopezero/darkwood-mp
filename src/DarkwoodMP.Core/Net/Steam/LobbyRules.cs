using System.Collections.Generic;

namespace DarkwoodMP.Net.Steam
{
    /// The Steam lobby's decisions (Lobby): when to host or lose the host,
    /// whether to stay in a lobby we entered, member changes, the status
    /// line, and the +connect_lobby launch argument. Steam ids are
    /// CSteamID.m_SteamID; 0 is CSteamID.Nil.
    public static class LobbyRules
    {
        /// The lobby data keys: the host's mod version and protocol.
        public const string KeyMod = "darkwoodmp";
        public const string KeyProto = "proto";

        /// Steam's k_EChatMemberStateChangeEntered bit.
        public const uint EnteredFlag = 1;

        /// The launch argument Steam adds when an invite starts the game.
        public const string ConnectArg = "+connect_lobby";

        public const string NotConnected = "Not connected";
        public const string Creating = "Creating lobby...";
        public const string Joining = "Joining lobby...";
        public const string Hosting = "Hosting";
        public const string HostLeft = "Host left the game";

        /// Whether Host makes a lobby: not while in one.
        public static bool MayHost(bool inLobby)
        {
            return !inLobby;
        }

        /// Whether LoseHost leaves: a client in a lobby.
        public static bool MayLoseHost(bool inLobby, bool iAmHost)
        {
            return inLobby && !iAmHost;
        }

        /// What to do with a lobby we entered. A failure wins, then the
        /// second telling of the same entry (or of the lobby just refused),
        /// then the version check, which passes a lobby with no "proto".
        public static EnterStep Enter(in EnterFacts entry)
        {
            if (entry.IoFailure || !entry.Success)
            {
                return EnterStep.Failed;
            }

            if (entry.Lobby == entry.Current || entry.Lobby == entry.Refused)
            {
                return EnterStep.Ignore;
            }

            if (!string.IsNullOrEmpty(entry.Proto)
                && entry.Proto != entry.Ours.ToString())
            {
                return EnterStep.OtherVersion;
            }

            return EnterStep.Enter;
        }

        /// The refusal of a host on another version. mod is the lobby's
        /// "darkwoodmp" value, which the shell reads after leaving it.
        public static Verdict OtherVersion(string mod) =>
            Verdict.Refuse(Refusal.OtherModVersion, mod);

        /// Whether we host a lobby we stay in: Steam's owner as we entered
        /// is us. That owner stays the host for good.
        public static bool IsHost(ulong owner, ulong me)
        {
            return owner == me;
        }

        /// The status once in: "Hosting", or "Joined {name}'s game".
        public static string EnteredStatus(bool isHost, string owner)
        {
            if (isHost)
            {
                return Hosting;
            }

            return "Joined " + owner + "'s game";
        }

        /// What a member change (m_rgfChatMemberStateChange) is to us:
        /// another lobby's is ignored, one with the entered bit is a join,
        /// anything else a departure.
        public static LobbyChange Change(
            ulong lobby,
            ulong current,
            uint change)
        {
            if (lobby != current)
            {
                return LobbyChange.Ignore;
            }

            if ((change & EnteredFlag) != 0)
            {
                return LobbyChange.Joined;
            }

            return LobbyChange.Left;
        }

        /// Whether the member who left was our host (for a client): then
        /// HostLost, Status HostLeft, leave, and nothing more.
        public static bool HostLeftUs(bool iAmHost, ulong who, ulong host)
        {
            return !iAmHost && who == host;
        }

        /// Whether Steam made us, a client, the lobby's owner: then
        /// HostLost, Status HostLeft, and leave. Checked after every change
        /// that didn't end in HostLeftUs, joins too.
        public static bool MadeOwner(in OwnerFacts lobby)
        {
            return lobby.InLobby && !lobby.IAmHost && lobby.Owner == lobby.Me;
        }

        /// The status after leaving: NotConnected, unless we lost the host,
        /// when the status (why) stays for the notice.
        public static string LeftStatus(bool hostLost, string status)
        {
            if (hostLost)
            {
                return status;
            }

            return NotConnected;
        }

        /// The lobbies the launch arguments ask to join: each
        /// "+connect_lobby ID" with a number after it, in order. The shell
        /// joins each, so the last wins.
        public static List<ulong> LaunchLobbies(string[] args)
        {
            var lobbies = new List<ulong>();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == ConnectArg
                    && ulong.TryParse(args[i + 1], out var lobby))
                {
                    lobbies.Add(lobby);
                }
            }

            return lobbies;
        }
    }
}
