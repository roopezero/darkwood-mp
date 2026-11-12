using System;
using System.Collections.Generic;
using Steamworks;

namespace DarkwoodMP.Net
{
    /// <summary>
    /// Steam lobby: used for invites (overlay "Join Game"/"Invite to game") and membership.
    /// The lobby owner is the authoritative host.
    /// </summary>
    public sealed class Lobby : IDisposable
    {
        public const int MaxPlayers = Protocol.MaxPlayers;
        private const string KeyMod = "darkwoodmp";
        private const string KeyProto = "proto";

        public CSteamID Id { get; private set; } = CSteamID.Nil;
        public bool InLobby => Id != CSteamID.Nil;
        // The host is whoever owned the lobby when we entered it. Steam hands ownership to another member when the
        // owner leaves or crashes, but the game world can't move with it, so the live owner is never trusted after that.
        public CSteamID Owner => InLobby ? _hostId : CSteamID.Nil;
        public bool IsHost => InLobby && _iAmHost;
        private CSteamID _hostId = CSteamID.Nil;
        private bool _iAmHost;
        private CSteamID _refused = CSteamID.Nil; // the lobby we just left for running another version
        public string Status { get; private set; } = "Not connected";

        public event Action OnEntered;
        public event Action OnLeft;
        public event Action<CSteamID> OnMemberJoined;
        public event Action<CSteamID> OnMemberLeft;

        private CallResult<LobbyCreated_t> _createResult;
        private CallResult<LobbyEnter_t> _enterResult;
        private Callback<GameLobbyJoinRequested_t> _joinRequested;
        private Callback<LobbyChatUpdate_t> _chatUpdate;
        // The lobby's creator enters it without a JoinLobby call: Steam tells of that only as a callback.
        private Callback<LobbyEnter_t> _entered;

        public void Init()
        {
            _createResult = CallResult<LobbyCreated_t>.Create(OnLobbyCreated);
            _enterResult = CallResult<LobbyEnter_t>.Create(OnLobbyEntered);
            _joinRequested = Callback<GameLobbyJoinRequested_t>.Create(r => Join(r.m_steamIDLobby));
            _chatUpdate = Callback<LobbyChatUpdate_t>.Create(OnChatUpdate);
            _entered = Callback<LobbyEnter_t>.Create(r => OnLobbyEntered(r, false));

            // Game launched by accepting an invite while not running: "+connect_lobby <id>"
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "+connect_lobby" && ulong.TryParse(args[i + 1], out var lobbyId))
                {
                    Plugin.Log.LogInfo($"Launched with +connect_lobby {lobbyId}");
                    Join(new CSteamID(lobbyId));
                }
            }
        }

        /// <summary>Client: we left because the host left the lobby.</summary>
        public bool HostLost { get; private set; }

        public void Host()
        {
            if (InLobby) return;
            HostLost = false;
            Status = "Creating lobby...";
            _createResult.Set(SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypeFriendsOnly, MaxPlayers));
        }

        public void Join(CSteamID lobby)
        {
            if (InLobby) Leave();
            HostLost = false;
            _refused = CSteamID.Nil;
            Status = "Joining lobby...";
            _enterResult.Set(SteamMatchmaking.JoinLobby(lobby));
        }

        public void Leave()
        {
            if (!InLobby) return;
            SteamMatchmaking.LeaveLobby(Id);
            Id = CSteamID.Nil;
            _hostId = CSteamID.Nil;
            _iAmHost = false;
            if (!HostLost) Status = "Not connected"; // else keep why, for the notice
            OnLeft?.Invoke();
        }

        /// <summary>Client: the host is gone (its connection failed); leave as if it had left the lobby.</summary>
        public void LoseHost(string why)
        {
            if (!InLobby || _iAmHost) return;
            HostLost = true;
            Status = why;
            Leave();
        }

        public void OpenInviteDialog()
        {
            if (InLobby) SteamFriends.ActivateGameOverlayInviteDialog(Id);
        }

        public List<CSteamID> Members()
        {
            var list = new List<CSteamID>();
            if (!InLobby) return list;
            int n = SteamMatchmaking.GetNumLobbyMembers(Id);
            for (int i = 0; i < n; i++) list.Add(SteamMatchmaking.GetLobbyMemberByIndex(Id, i));
            return list;
        }

        public bool IsMember(CSteamID user)
        {
            if (!InLobby) return false;
            foreach (var m in Members()) if (m == user) return true;
            return false;
        }

        private void OnLobbyCreated(LobbyCreated_t r, bool ioFailure)
        {
            if (ioFailure || r.m_eResult != EResult.k_EResultOK)
            {
                Status = $"Lobby creation failed: {r.m_eResult}";
                Plugin.Log.LogError(Status);
                return;
            }
            var id = new CSteamID(r.m_ulSteamIDLobby);
            SteamMatchmaking.SetLobbyData(id, KeyMod, PluginInfo.Version);
            SteamMatchmaking.SetLobbyData(id, KeyProto, Protocol.Version.ToString());
            // LobbyEnter_t also fires for the creator; OnLobbyEntered handles the rest.
            Plugin.Log.LogInfo($"Lobby created {id}");
        }

        private void OnLobbyEntered(LobbyEnter_t r, bool ioFailure)
        {
            if (ioFailure || r.m_EChatRoomEnterResponse != (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess)
            {
                Status = $"Failed to join lobby ({(EChatRoomEnterResponse)r.m_EChatRoomEnterResponse})";
                Plugin.Log.LogError(Status);
                return;
            }
            var id = new CSteamID(r.m_ulSteamIDLobby);
            // Told twice: as JoinLobby's result and as the callback. After a refusal (below) the second one must not
            // let us in: we've left, so the lobby's data reads empty then and the version check would pass.
            if (id == Id || id == _refused) return;
            string proto = SteamMatchmaking.GetLobbyData(id, KeyProto);
            if (!string.IsNullOrEmpty(proto) && proto != Protocol.Version.ToString())
            {
                SteamMatchmaking.LeaveLobby(id);
                _refused = id;
                Status = $"Host runs a different DarkwoodMP version ({SteamMatchmaking.GetLobbyData(id, KeyMod)})";
                Plugin.Log.LogError(Status);
                return;
            }
            Id = id;
            _hostId = SteamMatchmaking.GetLobbyOwner(id);
            _iAmHost = _hostId == SteamUser.GetSteamID();
            Status = IsHost ? "Hosting" : $"Joined {SteamFriends.GetFriendPersonaName(Owner)}'s game";
            Plugin.Log.LogInfo($"Entered lobby {id}, host={IsHost}");
            OnEntered?.Invoke();
        }

        private void OnChatUpdate(LobbyChatUpdate_t u)
        {
            if (new CSteamID(u.m_ulSteamIDLobby) != Id) return;
            var who = new CSteamID(u.m_ulSteamIDUserChanged);
            var change = (EChatMemberStateChange)u.m_rgfChatMemberStateChange;
            if ((change & EChatMemberStateChange.k_EChatMemberStateChangeEntered) != 0)
            {
                Plugin.Log.LogInfo($"{SteamFriends.GetFriendPersonaName(who)} joined the lobby");
                OnMemberJoined?.Invoke(who);
            }
            else
            {
                Plugin.Log.LogInfo($"{SteamFriends.GetFriendPersonaName(who)} left the lobby");
                OnMemberLeft?.Invoke(who);
                // Host left: lobby ownership moves in Steam, but the game world can't; end the session.
                if (!_iAmHost && who == _hostId) { HostLost = true; Status = "Host left the game"; Leave(); return; }
            }
            // Steam made this joiner the owner (the host is gone and we missed it): it isn't a host either.
            if (InLobby && !_iAmHost && SteamMatchmaking.GetLobbyOwner(Id) == SteamUser.GetSteamID())
            {
                HostLost = true; Status = "Host left the game"; Leave();
            }
        }

        public void Dispose()
        {
            Leave();
            _createResult?.Dispose();
            _enterResult?.Dispose();
            _joinRequested?.Dispose();
            _chatUpdate?.Dispose();
            _entered?.Dispose();
        }
    }
}
