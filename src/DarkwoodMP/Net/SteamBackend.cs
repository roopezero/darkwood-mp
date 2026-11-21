using System;
using Steamworks;

namespace DarkwoodMP.Net
{
    /// <summary>
    /// Steam lobbies + Steam Datagram Relay P2P. Only constructed once Steam is
    /// confirmed running, so the Steamworks types it references are never
    /// touched on GOG builds.
    /// </summary>
    public sealed class SteamBackend : INetBackend, IGuiSection
    {
        private readonly Lobby _lobby = new Lobby();
        private readonly SteamTransport _transport = new SteamTransport();

        public string Name => "Steam";
        public string Status => _lobby.Status;
        public bool InSession => _lobby.InLobby;
        public bool IsHost => _lobby.IsHost;
        public ulong HostId => _lobby.Owner.m_SteamID;
        public ulong LocalId => SteamUser.GetSteamID().m_SteamID;
        public string SuggestedPlayerName => SteamFriends.GetPersonaName();
        // A Steam lobby is friends-only; nobody needs a password.
        public string HostPassword => "";
        public string JoinPassword => "";
        public int MaxPlayers => Protocol.MaxPlayers;

        public void Host() => _lobby.Host();

        /// <summary>
        /// The lobby members' Steam names, comma-separated (for logs; keeps
        /// Steamworks types out of callers).
        /// </summary>
        public string MemberNames() => string.Join(
            ", ",
            _lobby.Members().ConvertAll(
                m => SteamFriends.GetFriendPersonaName(m)));

        public void Kick(ulong peer) =>
            _transport.CloseWhenSent(new CSteamID(peer));

        public event Action OnEntered;
        public event Action OnLeft;
        public event Action<ulong> OnPeerLost;
        public event Action<ulong, byte[], int> OnMessage;

        public SteamBackend()
        {
            _transport.Init();
            _lobby.Init();
            _transport.ShouldAccept = id =>
            {
                if (!_lobby.InLobby)
                {
                    return false;
                }

                if (IsHost)
                {
                    return _lobby.IsMember(id);
                }

                return id == _lobby.Owner;
            };

            _transport.OnMessage += (from, data, len, ch) =>
            {
                Action<ulong, byte[], int> handler = OnMessage;
                if (handler != null)
                {
                    handler(from.m_SteamID, data, len);
                }
            };

            _lobby.OnEntered += () =>
            {
                Action entered = OnEntered;
                if (entered != null)
                {
                    entered();
                }
            };

            _lobby.OnLeft += () =>
            {
                _transport.ClearPending();
                Action left = OnLeft;
                if (left != null)
                {
                    left();
                }
            };

            // A player whose connection died without leaving the lobby (its PC
            // lost power, its network dropped): the lobby only notices much
            // later. Only for peers that got in, or a host that let us in: a
            // session can also fail while it's first being set up, and Hello is
            // sent again then.
            _transport.OnSessionLost += who =>
            {
                var session = Session.Instance;
                if (!_lobby.InLobby || session == null)
                {
                    return;
                }

                if (IsHost && session.Peers.Contains(who.m_SteamID))
                {
                    Plugin.Log.LogInfo(
                        $"Lost the connection to #{who.m_SteamID}");
                    _transport.CloseSession(who);
                    Action<ulong> lost = OnPeerLost;
                    if (lost != null)
                    {
                        lost(who.m_SteamID);
                    }
                }
                else if (!IsHost && who == _lobby.Owner && session.Connected)
                {
                    _lobby.LoseHost("Lost the connection to the host");
                }
            };

            // Every member hears of every departure. A client only acts on the
            // host's: for anyone else the host's PlayerLeft does the cleanup
            // (and shows "X left", which needs the name still known then).
            _lobby.OnMemberLeft += who =>
            {
                if (!IsHost && who != _lobby.Owner)
                {
                    return;
                }

                _transport.CloseSession(who);
                Action<ulong> lost = OnPeerLost;
                if (lost != null)
                {
                    lost(who.m_SteamID);
                }
            };
        }

        public void DrawGui(Session session, Flow f)
        {
            if (!InSession)
            {
                if (session.Active)
                {
                    Ui.Text(f, "In a direct game.", MenuSkin.Dim);
                    return;
                }

                if (Plugin.CantHostReason == null
                    && Ui.Button(f, "Host with Steam (friends join by invite)"))
                {
                    _lobby.Host();
                }

                Ui.Text(
                    f,
                    "To join: accept a friend's Steam invite, or Join Game in "
                        + "the Steam friends list.",
                    MenuSkin.Dim);
                return;
            }

            Ui.Text(f, Status, MenuSkin.Bright);
            if (Ui.Button(f, "Invite Steam friends"))
            {
                _lobby.OpenInviteDialog();
            }

            if (!IsHost && session.Connected
                && Ui.Button(
                    f, "Reload the host's world (if things look out of sync)"))
            {
                Rejoin();
            }

            if (Ui.Button(f, "Leave"))
            {
                Leave();
            }
        }

        public bool Send(
            ulong to, byte[] data, int length, int channel, bool reliable) =>
            _transport.Send(new CSteamID(to), data, length, channel, reliable);

        public void Poll() => _transport.Poll();
        public void Rejoin()
        {
            if (IsHost || !InSession)
            {
                return;
            }

            var lobby = _lobby.Id;
            Leave();
            _lobby.Join(lobby);
        }

        public bool HostLost => _lobby.HostLost;

        private string _presStatus, _presGroup, _presSize;

        /// <summary>
        /// Steam rich presence. "status" is a plain string that friends see in
        /// "View game info". The line under a name in the friends list would
        /// need "steam_display", which only takes tokens that the game's
        /// developer uploads to Steamworks; Darkwood has none, so it isn't set.
        /// In a Steam lobby, "steam_player_group" (the lobby) and
        /// "steam_player_group_size" let Steam show the players together. Steam
        /// is only called for a value that changed; an empty value removes the
        /// key.
        /// </summary>
        public void SetPresence(string status, int players)
        {
            string group = "";
            if (_lobby.InLobby)
            {
                group = _lobby.Id.m_SteamID.ToString();
            }

            string size = "";
            if (_lobby.InLobby)
            {
                size = players.ToString();
            }

            if (status != _presStatus)
            {
                SteamFriends.SetRichPresence("status", status);
                _presStatus = status;
            }

            if (group != _presGroup)
            {
                SteamFriends.SetRichPresence("steam_player_group", group);
                _presGroup = group;
            }

            if (size != _presSize)
            {
                SteamFriends.SetRichPresence("steam_player_group_size", size);
                _presSize = size;
            }
        }

        /// <summary>Our own "status" as Steam has it (for the log).</summary>
        public string PresenceReadBack() => SteamFriends.GetFriendRichPresence(
            SteamUser.GetSteamID(), "status");

        public void Leave()
        {
            CoopSession coop = Coop.Current;
            if (coop != null)
            {
                coop.Characters.SaveNow();
            }

            _lobby.Leave();
        }

        public void Dispose()
        {
            _lobby.Dispose();
            _transport.Dispose();
        }
    }
}
