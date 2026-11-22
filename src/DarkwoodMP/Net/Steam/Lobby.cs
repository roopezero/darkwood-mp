using System;
using System.Collections.Generic;
using DarkwoodMP.Net.Steam;
using Steamworks;

namespace DarkwoodMP.Net
{
    /// <summary>
    /// Steam lobby: used for invites (overlay "Join Game"/"Invite to game") and
    /// membership. The lobby owner is the authoritative host.
    /// </summary>
    public sealed class Lobby : IDisposable
    {
        public CSteamID Id { get; private set; } = CSteamID.Nil;
        public bool InLobby => Id != CSteamID.Nil;
        // The host is whoever owned the lobby when we entered it. Steam hands
        // ownership to another member when the owner leaves or crashes, but the
        // game world can't move with it, so the live owner is never trusted
        // after that.
        public CSteamID Owner
        {
            get
            {
                if (InLobby)
                {
                    return _hostId;
                }

                return CSteamID.Nil;
            }
        }

        public bool IsHost => InLobby && _iAmHost;
        private CSteamID _hostId = CSteamID.Nil;
        private bool _iAmHost;
        // the lobby we just left for running another version
        private CSteamID _refused = CSteamID.Nil;
        public string Status { get; private set; } = LobbyRules.NotConnected;

        public event Action OnEntered;
        public event Action OnLeft;
        public event Action<CSteamID> OnMemberLeft;

        private CallResult<LobbyCreated_t> _createResult;
        private CallResult<LobbyEnter_t> _enterResult;
        private Callback<GameLobbyJoinRequested_t> _joinRequested;
        private Callback<LobbyChatUpdate_t> _chatUpdate;
        // The lobby's creator enters it without a JoinLobby call: Steam tells
        // of that only as a callback.
        private Callback<LobbyEnter_t> _entered;

        public void Init()
        {
            _createResult = CallResult<LobbyCreated_t>.Create(OnLobbyCreated);
            _enterResult = CallResult<LobbyEnter_t>.Create(OnLobbyEntered);
            _joinRequested = Callback<GameLobbyJoinRequested_t>.Create(
                r => Join(r.m_steamIDLobby));
            _chatUpdate = Callback<LobbyChatUpdate_t>.Create(OnChatUpdate);
            _entered =
                Callback<LobbyEnter_t>.Create(r => OnLobbyEntered(r, false));

            // Game launched by accepting an invite while not running:
            // "+connect_lobby <id>"
            var args = Environment.GetCommandLineArgs();
            foreach (var lobbyId in LobbyRules.LaunchLobbies(args))
            {
                Plugin.Log.LogInfo(
                    $"Launched with {LobbyRules.ConnectArg} {lobbyId}");
                Join(new CSteamID(lobbyId));
            }
        }

        /// <summary>Client: we left because the host left the lobby.</summary>
        public bool HostLost { get; private set; }

        public void Host()
        {
            if (!LobbyRules.MayHost(InLobby))
            {
                return;
            }

            HostLost = false;
            Status = LobbyRules.Creating;
            _createResult.Set(
                SteamMatchmaking.CreateLobby(
                    ELobbyType.k_ELobbyTypeFriendsOnly, Protocol.MaxPlayers));
        }

        public void Join(CSteamID lobby)
        {
            if (InLobby)
            {
                Leave();
            }

            HostLost = false;
            _refused = CSteamID.Nil;
            Status = LobbyRules.Joining;
            _enterResult.Set(SteamMatchmaking.JoinLobby(lobby));
        }

        public void Leave()
        {
            if (!InLobby)
            {
                return;
            }

            SteamMatchmaking.LeaveLobby(Id);
            Id = CSteamID.Nil;
            _hostId = CSteamID.Nil;
            _iAmHost = false;
            // when the host was lost, Status keeps why, for the notice
            Status = LobbyRules.LeftStatus(HostLost, Status);

            Action handler = OnLeft;
            if (handler != null)
            {
                handler();
            }
        }

        /// <summary>
        /// Client: the host is gone (its connection failed); leave as if it had
        /// left the lobby.
        /// </summary>
        public void LoseHost(string why)
        {
            if (!LobbyRules.MayLoseHost(InLobby, _iAmHost))
            {
                return;
            }

            HostLost = true;
            Status = why;
            Leave();
        }

        public void OpenInviteDialog()
        {
            if (InLobby)
            {
                SteamFriends.ActivateGameOverlayInviteDialog(Id);
            }
        }

        public List<CSteamID> Members()
        {
            var list = new List<CSteamID>();
            if (!InLobby)
            {
                return list;
            }

            int n = SteamMatchmaking.GetNumLobbyMembers(Id);
            for (int i = 0; i < n; i++)
            {
                list.Add(SteamMatchmaking.GetLobbyMemberByIndex(Id, i));
            }

            return list;
        }

        public bool IsMember(CSteamID user) => Members().Contains(user);

        private void OnLobbyCreated(LobbyCreated_t r, bool ioFailure)
        {
            if (ioFailure || r.m_eResult != EResult.k_EResultOK)
            {
                Status = $"Lobby creation failed: {r.m_eResult}";
                Plugin.Log.LogError(Status);
                return;
            }

            var id = new CSteamID(r.m_ulSteamIDLobby);
            SteamMatchmaking.SetLobbyData(
                id, LobbyRules.KeyMod, PluginInfo.Version);
            SteamMatchmaking.SetLobbyData(
                id, LobbyRules.KeyProto, Protocol.Version.ToString());
            // LobbyEnter_t also fires for the creator; OnLobbyEntered handles
            // the rest.
            Plugin.Log.LogInfo($"Lobby created {id}");
        }

        /// <summary>
        /// A lobby Steam says we entered, for LobbyRules.Enter.
        /// </summary>
        private EnterFacts EnterOf(LobbyEnter_t r, bool ioFailure)
        {
            var id = new CSteamID(r.m_ulSteamIDLobby);
            uint success = (uint)EChatRoomEnterResponse
                .k_EChatRoomEnterResponseSuccess;
            return new EnterFacts
            {
                IoFailure = ioFailure,
                Success = r.m_EChatRoomEnterResponse == success,
                Lobby = id.m_SteamID,
                Current = Id.m_SteamID,
                Refused = _refused.m_SteamID,
                Proto = SteamMatchmaking.GetLobbyData(
                    id,
                    LobbyRules.KeyProto),
                Ours = Protocol.Version,
            };
        }

        private void OnLobbyEntered(LobbyEnter_t r, bool ioFailure)
        {
            var step = LobbyRules.Enter(EnterOf(r, ioFailure));
            if (step == EnterStep.Failed)
            {
                EChatRoomEnterResponse response =
                    (EChatRoomEnterResponse)r.m_EChatRoomEnterResponse;
                Status = "Failed to join lobby " + $"({response})";
                Plugin.Log.LogError(Status);
                return;
            }

            // Told twice: as JoinLobby's result and as the callback. After a
            // refusal (below) the second one must not let us in: we've left, so
            // the lobby's data reads empty then and the version check would
            // pass.
            if (step == EnterStep.Ignore)
            {
                return;
            }

            var id = new CSteamID(r.m_ulSteamIDLobby);
            if (step == EnterStep.OtherVersion)
            {
                SteamMatchmaking.LeaveLobby(id);
                _refused = id;
                string mod = SteamMatchmaking.GetLobbyData(
                    id,
                    LobbyRules.KeyMod);
                Status = Refusals.Text(LobbyRules.OtherVersion(mod));
                Plugin.Log.LogError(Status);
                return;
            }

            Id = id;
            _hostId = SteamMatchmaking.GetLobbyOwner(id);
            _iAmHost = LobbyRules.IsHost(
                _hostId.m_SteamID,
                SteamUser.GetSteamID().m_SteamID);
            string owner = "";
            if (!IsHost)
            {
                owner = SteamFriends.GetFriendPersonaName(Owner);
            }

            Status = LobbyRules.EnteredStatus(IsHost, owner);
            Plugin.Log.LogInfo($"Entered lobby {id}, host={IsHost}");
            Action handler = OnEntered;
            if (handler != null)
            {
                handler();
            }
        }

        /// <summary>
        /// Whether Steam made us the lobby's owner, for LobbyRules.MadeOwner.
        /// Steam's owner is read only for a client in the lobby.
        /// </summary>
        private OwnerFacts OwnerOf()
        {
            ulong owner = 0;
            if (InLobby && !_iAmHost)
            {
                owner = SteamMatchmaking.GetLobbyOwner(Id).m_SteamID;
            }

            return new OwnerFacts
            {
                InLobby = InLobby,
                IAmHost = _iAmHost,
                Owner = owner,
                Me = SteamUser.GetSteamID().m_SteamID,
            };
        }

        private void OnChatUpdate(LobbyChatUpdate_t u)
        {
            var change = LobbyRules.Change(
                u.m_ulSteamIDLobby,
                Id.m_SteamID,
                u.m_rgfChatMemberStateChange);
            if (change == LobbyChange.Ignore)
            {
                return;
            }

            var who = new CSteamID(u.m_ulSteamIDUserChanged);
            if (change == LobbyChange.Joined)
            {
                Plugin.Log.LogInfo(
                    $"{SteamFriends.GetFriendPersonaName(who)} joined the "
                        + "lobby");
            }
            else
            {
                Plugin.Log.LogInfo(
                    $"{SteamFriends.GetFriendPersonaName(who)} left the lobby");
                Action<CSteamID> handler = OnMemberLeft;
                if (handler != null)
                {
                    handler(who);
                }

                // Host left: lobby ownership moves in Steam, but the game world
                // can't; end the session.
                if (LobbyRules.HostLeftUs(
                    _iAmHost,
                    who.m_SteamID,
                    _hostId.m_SteamID))
                {
                    LeaveHostLost();
                    return;
                }
            }

            // Steam made this joiner the owner (the host is gone and we missed
            // it): it isn't a host either.
            if (LobbyRules.MadeOwner(OwnerOf()))
            {
                LeaveHostLost();
            }
        }

        /// <summary>Client: leave because the host left the game.</summary>
        private void LeaveHostLost()
        {
            HostLost = true;
            Status = LobbyRules.HostLeft;
            Leave();
        }

        public void Dispose()
        {
            Leave();
            CallResult<LobbyCreated_t> createResult = _createResult;
            if (createResult != null)
            {
                createResult.Dispose();
            }

            CallResult<LobbyEnter_t> enterResult = _enterResult;
            if (enterResult != null)
            {
                enterResult.Dispose();
            }

            Callback<GameLobbyJoinRequested_t> joinRequested = _joinRequested;
            if (joinRequested != null)
            {
                joinRequested.Dispose();
            }

            Callback<LobbyChatUpdate_t> chatUpdate = _chatUpdate;
            if (chatUpdate != null)
            {
                chatUpdate.Dispose();
            }

            Callback<LobbyEnter_t> entered = _entered;
            if (entered != null)
            {
                entered.Dispose();
            }
        }
    }
}
