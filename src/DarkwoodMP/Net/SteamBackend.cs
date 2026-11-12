using System;
using Steamworks;

namespace DarkwoodMP.Net
{
    /// <summary>
    /// Steam lobbies + Steam Datagram Relay P2P. Only constructed once Steam is confirmed running,
    /// so the Steamworks types it references are never touched on GOG builds.
    /// </summary>
    public sealed class SteamBackend : INetBackend, IGuiSection
    {
        public readonly Lobby Lobby = new Lobby();
        private readonly SteamTransport _transport = new SteamTransport();

        public string Name => "Steam";
        public string Status => Lobby.Status;
        public bool InSession => Lobby.InLobby;
        public bool IsHost => Lobby.IsHost;
        public ulong HostId => Lobby.Owner.m_SteamID;
        public ulong LocalId => SteamUser.GetSteamID().m_SteamID;
        public string SuggestedPlayerName => SteamFriends.GetPersonaName();
        // A Steam lobby is friends-only; nobody needs a password.
        public string HostPassword => "";
        public string JoinPassword => "";
        public int MaxPlayers => Protocol.MaxPlayers;

        public void Host() => Lobby.Host();

        /// <summary>The lobby members' Steam names, comma-separated (for logs; keeps Steamworks types out of callers).</summary>
        public string MemberNames() => string.Join(", ", Lobby.Members().ConvertAll(m => SteamFriends.GetFriendPersonaName(m)));

        public void Kick(ulong peer) => _transport.CloseSession(new CSteamID(peer));

        public event Action OnEntered;
        public event Action OnLeft;
        public event Action<ulong> OnPeerLost;
        public event Action<ulong, byte[], int> OnMessage;

        public SteamBackend()
        {
            _transport.Init();
            Lobby.Init();
            _transport.ShouldAccept = id => Lobby.InLobby && (IsHost ? Lobby.IsMember(id) : id == Lobby.Owner);
            _transport.OnMessage += (from, data, len, ch) => OnMessage?.Invoke(from.m_SteamID, data, len);
            Lobby.OnEntered += () => OnEntered?.Invoke();
            Lobby.OnLeft += () => { _transport.ClearPending(); OnLeft?.Invoke(); };
            // A player whose connection died without leaving the lobby (its PC lost power, its network dropped):
            // the lobby only notices much later. Only for peers that got in, or a host that let us in: a session
            // can also fail while it's first being set up, and Hello is sent again then.
            _transport.OnSessionLost += who =>
            {
                var session = Session.Instance;
                if (!Lobby.InLobby || session == null) return;
                if (IsHost && session.Peers.Contains(who.m_SteamID))
                {
                    Plugin.Log.LogInfo($"Lost the connection to #{who.m_SteamID}");
                    _transport.CloseSession(who);
                    OnPeerLost?.Invoke(who.m_SteamID);
                }
                else if (!IsHost && who == Lobby.Owner && session.Connected) Lobby.LoseHost("Lost the connection to the host");
            };
            Lobby.OnMemberLeft += who =>
            {
                _transport.CloseSession(who);
                OnPeerLost?.Invoke(who.m_SteamID);
            };
        }

        public void DrawGui(Session session, Flow f)
        {
            if (!InSession)
            {
                if (session.Active) { Ui.Text(f, "In a direct game.", MenuSkin.Dim); return; }
                if (Plugin.CantHostReason == null && Ui.Button(f, "Host with Steam (friends join by invite)")) Lobby.Host();
                Ui.Text(f, "To join: accept a friend's Steam invite, or Join Game in the Steam friends list.", MenuSkin.Dim);
                return;
            }
            Ui.Text(f, Status, MenuSkin.Bright);
            if (Ui.Button(f, "Invite Steam friends")) Lobby.OpenInviteDialog();
            if (!IsHost && session.Connected && Ui.Button(f, "Reload the host's world (if things look out of sync)"))
            {
                Rejoin();              // sends your character first
            }
            if (Ui.Button(f, "Leave")) Leave();
        }

        public bool Send(ulong to, byte[] data, int length, int channel, bool reliable) =>
            _transport.Send(new CSteamID(to), data, length, channel, reliable);

        public void Poll() => _transport.Poll();
        public void Rejoin()
        {
            if (IsHost || !InSession) return;
            var lobby = Lobby.Id;
            Leave();
            Lobby.Join(lobby);
        }

        public bool HostLost => Lobby.HostLost;

        private string _presStatus, _presGroup, _presSize;

        /// <summary>
        /// Steam rich presence. "status" is a plain string that friends see in "View game info". The line under a
        /// name in the friends list would need "steam_display", which only takes tokens that the game's developer
        /// uploads to Steamworks; Darkwood has none, so it isn't set. In a Steam lobby, "steam_player_group" (the
        /// lobby) and "steam_player_group_size" let Steam show the players together. Steam is only called for a
        /// value that changed; an empty value removes the key.
        /// </summary>
        public void SetPresence(string status, int players)
        {
            string group = Lobby.InLobby ? Lobby.Id.m_SteamID.ToString() : "";
            string size = Lobby.InLobby ? players.ToString() : "";
            if (status != _presStatus) { SteamFriends.SetRichPresence("status", status); _presStatus = status; }
            if (group != _presGroup) { SteamFriends.SetRichPresence("steam_player_group", group); _presGroup = group; }
            if (size != _presSize) { SteamFriends.SetRichPresence("steam_player_group_size", size); _presSize = size; }
        }

        /// <summary>Our own "status" as Steam has it (for the log).</summary>
        public string PresenceReadBack() => SteamFriends.GetFriendRichPresence(SteamUser.GetSteamID(), "status");

        public void Leave()
        {
            World.Characters.Instance?.SaveNow();
            Lobby.Leave();
        }

        public void Dispose()
        {
            Lobby.Dispose();
            _transport.Dispose();
        }
    }
}
