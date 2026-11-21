using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DarkwoodMP.Net;
using DarkwoodMP.World.Joining;
using Newtonsoft.Json;
using UnityEngine;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Shares the host's world with the players who join, as goasis hands its
    /// pack to them.
    ///
    /// Host: a client whose Hello carries another world hash than the host's
    /// gets SaveOffer: the hash, the profile and the files' names and sizes.
    /// The offered snapshot is kept for that client, so the host saving
    /// meanwhile doesn't change what it's downloading. The client asks for it a
    /// piece at a time (NeedSave) and gets each as SaveData.
    ///
    /// Client: the world goes into
    /// BepInEx/cache/DarkwoodMP/worlds/&lt;hash&gt;/, laid out as 1_4Save is
    /// (prof&lt;id&gt;/sav.dat ...), plus profile.json. Once it has all come
    /// and its hash checks out, the client sends Hello again with that hash, is
    /// let in, and loads it with saves redirected there (SaveRedirect).
    /// The world the client last downloaded is offered in Hello while it's
    /// unchanged, so rejoining a host that hasn't saved since needn't download
    /// it again.
    /// </summary>
    public sealed partial class WorldShare
    {
        private const int PieceSize = OfferRules.PieceSize;
        private const int PiecesInFlight = OfferRules.PiecesInFlight;
        private const int KeepWorlds = 3;

        private readonly Session _session;
        private readonly MonoBehaviour _runner;
        private readonly string _cacheDir;

        public WorldShare(
            Session session, MonoBehaviour runner, string cacheRoot)
        {
            _session = session;
            _runner = runner;
            _cacheDir = Path.Combine(cacheRoot, "DarkwoodMP", "worlds");
            Directory.CreateDirectory(_cacheDir);

            session.BeforeWelcome = HostBeforeWelcome;
            session.ClientWorldHash = ClientHelloHash;
            session.Register(MsgType.SaveOffer, OnSaveOffer);
            session.Register(MsgType.NeedSave, OnNeedSave);
            session.Register(MsgType.SaveData, OnSaveData);
            session.OnWelcomed += OnWelcomed;
            session.OnPeerDisconnected += id =>
            {
                _offered.Remove(id);
                _waiting.Remove(id);
                _staleTold.Remove(id);
                if (_watchPeer == id)
                {
                    EndWatch("gone");
                }
            };

            session.OnSessionEnded += () =>
            {
                _offered.Clear();
                _waiting.Clear();
                _staleTold.Clear();
                _download = null;
                _hostWorldKey = 0;
                EndWatch("the session ended");
            };

            session.OnJoinTimedOut += why =>
            {
                _download = null;
                LoadStatus = why;
            };

            session.Register(MsgType.WorldReload, (from, r) =>
            {
                if (from != _session.HostId || _session.IsHost)
                {
                    return;
                }

                Plugin.Notice("The host's world changed: loading it again");
                INetBackend backend = _session.Backend;
                if (backend != null)
                {
                    backend.Rejoin();
                }
            });

            session.Register(MsgType.JoinWait, (from, r) =>
            {
                if (from != _session.HostId || _session.IsHost
                    || _session.Connected)
                {
                    return;
                }

                string why = JoinWaitMessage.Read(r);
                if (LoadStatus != why)
                {
                    Plugin.Log.LogInfo($"Waiting to join: {why}");
                }

                LoadStatus = why;
                if (_download != null)
                {
                    Plugin.Log.LogInfo(
                        "The host no longer offers the world we were "
                            + "downloading");
                    _download = null;
                }

                if (why == Preparing)
                {
                    // it's on its way: the loading screen says so
                    JoinScreen.Begin();
                }

                _runner.StartCoroutine(HelloAgain());
            });

            session.Register(MsgType.HostAway, (from, r) =>
            {
                if (from != _session.HostId || _session.IsHost)
                {
                    return;
                }

                INetBackend active = _session.Backend;
                if (active != null)
                {
                    active.Leave();
                }

                LeaveHostWorld(
                    "The host went back to the main menu and stopped the "
                        + "server.");
            });

            session.OnHostLost += why => LeaveHostWorld(
                $"Lost the host: {why}");
            session.OnRefused += why =>
            {
                if (JoinScreen.Shown)
                {
                    LoadStatus = $"The host refused you: {why}";
                }
            };
        }

        // ---- the host's world changing under the players --------------------

        private int _hostWorldKey;

        /// <summary>
        /// Going back to the main menu (Core.returnToMainMenu) ends the
        /// session: the host's stops its server, a joiner's leaves the host's
        /// game.
        /// </summary>
        public void ReturnToMainMenu()
        {
            HostToMainMenu();
            ClientToMainMenu();
        }

        /// <summary>
        /// Host, going back to the main menu (Core.returnToMainMenu: the pause
        /// menu's Main menu, the permadeath screen, the credits): the world the
        /// players are in is gone, so the server stops and they're sent to
        /// their own main menus.
        /// </summary>
        public void HostToMainMenu()
        {
            if (!_session.IsHost)
            {
                return;
            }

            Plugin.Log.LogInfo("Back to the main menu: the server stops");
            StopServer(_session.Begin(MsgType.HostAway));
        }

        /// <summary>
        /// Joiner, going back to the main menu: leave the host's game (our
        /// character is sent first, while the world is still whole), so the
        /// menu's Multiplayer window is ready to join this server or another
        /// one again.
        /// </summary>
        // LoadWorld is leaving our world for the host's: not a player's going
        // to the menu
        private bool _joiningWorld;
        public void ClientToMainMenu()
        {
            if (!_session.IsClient || _joiningWorld)
            {
                return;
            }

            Plugin.Log.LogInfo(
                "Back to the main menu: leaving the host's game");
            _session.Backend.Leave();
        }

        /// <summary>
        /// Host: send the players a last message (HostAway, GameEnd) and stop
        /// the server once they've gone.
        /// </summary>
        public void StopServer(PacketWriter farewell)
        {
            if (!_session.IsHost)
            {
                return;
            }

            if (_session.Peers.Count == 0)
            {
                _session.Backend.Leave();
                return;
            }

            _session.Broadcast(farewell, Channel.Control, reliable: true);
            _runner.StartCoroutine(StopServerSoon(_session.Backend));
        }

        /// <summary>
        /// Client: what the co-op panel says once we're back at the main menu.
        /// </summary>
        public void SetLoadStatus(string why) => LoadStatus = why;

        /// <summary>
        /// The players leave as HostAway reaches them. Closing their
        /// connections from here first could lose it: a socket closed with
        /// their updates still unread is reset, not closed.
        /// </summary>
        private IEnumerator StopServerSoon(INetBackend backend)
        {
            float until = Time.realtimeSinceStartup + 1f;
            while (_session.Peers.Count > 0
                && Time.realtimeSinceStartup < until)
            {
                yield return null;
            }

            if (_session.Backend == backend && backend.InSession
                && backend.IsHost)
            {
                backend.Leave();
            }
        }

        /// <summary>
        /// Client: the host is gone. Leave its world (our copy of it) for the
        /// main menu.
        /// </summary>
        private void LeaveHostWorld(string why)
        {
            LoadStatus = why;
            Plugin.Log.LogInfo(why);
            if (SaveRedirect.Active)
            {
                _runner.StartCoroutine(ToMainMenu());
            }
        }

        private IEnumerator ToMainMenu()
        {
            // A world still loading is let finish first: leaving in the middle
            // of it isn't something the game does.
            float until = Time.realtimeSinceStartup + 120f;
            while (!InWorld && !MainMenuReady()
                && Time.realtimeSinceStartup < until)
            {
                yield return null;
            }

            if (_session.Active || !InWorld || !SaveRedirect.Active)
            {
                yield break; // joined again meanwhile, or already out
            }

            if (Core.mainMenu)
            {
                Singleton<MainMenu>.Instance.close(); // the pause menu
            }

            Core.returnToMainMenu();
            yield return null;
            while (!MainMenuReady())
            {
                yield return null;
            }

            Plugin.Log.LogInfo("Back at the main menu");
            Plugin.Instance.OpenPanel(); // it says why (LoadStatus)
        }

        /// <summary>
        /// Host: when its world changes (a new chapter, a save loaded again),
        /// the players in it load it again. Going back to the main menu stops
        /// the server instead (HostToMainMenu).
        /// </summary>
        public void Update()
        {
            if (_session.IsHost)
            {
                UpdateWatch();
            }
            else
            {
                UpdateDownload();
            }

            if (!_session.IsHost)
            {
                _hostWorldKey = 0;
                return;
            }

            if (!WorldReady)
            {
                return;
            }

            // tracked with nobody in, too: a joiner let in after a change has
            // the new world already
            int key = ActiveScene;
            if (JoinGateRules.Reloads(
                _hostWorldKey, key, _session.Peers.Count))
            {
                Plugin.Log.LogInfo(
                    "Your world changed: the players in it load it again");
                SharedIds.ResetHost();
                _session.Broadcast(
                    _session.Begin(MsgType.WorldReload), Channel.Control,
                    reliable: true);
            }

            _hostWorldKey = key;
        }

        /// <summary>Client: ask to join again after a JoinWait.</summary>
        private IEnumerator HelloAgain()
        {
            yield return new WaitForSecondsRealtime(3f);
            // Not while a world is coming in (the offer came meanwhile): that
            // Hello would only be offered it again.
            if (_session.Active && !_session.IsHost && !_session.Connected
                && _download == null && !_unpacking)
            {
                _session.SendHello();
            }
        }
    }
}
