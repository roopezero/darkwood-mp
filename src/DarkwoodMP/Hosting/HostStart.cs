using System.Collections;
using DarkwoodMP.Net;
using DarkwoodMP.World;
using UnityEngine;

namespace DarkwoodMP.Hosting
{
    /// <summary>
    /// Starting to host: whether the player may, the Start Game window's
    /// host box, and Direct hosting with the settings. Process state, on
    /// ModHost: hosting is offered from the main menu.
    /// </summary>
    internal sealed class HostStart
    {
        private readonly ModHost _host;

        public HostStart(ModHost host)
        {
            _host = host;
        }

        public string CantHostReason
        {
            get
            {
                var verdict = HostRules.MayHost(HostFactsOf());
                if (verdict.Refused)
                {
                    return Refusals.Text(verdict);
                }

                return null;
            }
        }

        /// <summary>
        /// Whether the player may host the world they're in. Read in the
        /// order the rule checks it, so a later fact is only read when the
        /// earlier ones allow hosting.
        /// </summary>
        private HostFacts HostFactsOf()
        {
            if (!WorldShare.InWorld)
            {
                return new HostFacts();
            }

            // After leaving someone's game you're still in your copy of
            // their world (your own saves are untouched).
            WorldShare share = _host.Share;
            if (share != null && share.PlayingHostCopy)
            {
                return new HostFacts { InWorld = true, HostsCopy = true };
            }

            return new HostFacts
            {
                InWorld = true,
                InEpilogue = World.Story.EpilogueSync.InEpilogueAlone(),
            };
        }

        /// <summary>
        /// The host box's wait for the world, then hosting with Steam or
        /// Direct (see Plugin.HostWhenReady).
        /// </summary>
        public IEnumerator WhenWorldReady(bool steam)
        {
            float t0 = Time.realtimeSinceStartup;
            bool loadStarted = false;
            while (true)
            {
                var wait = new HostWaitFacts
                {
                    LoadStarted = loadStarted,
                    Waited = new Seconds(Time.realtimeSinceStartup - t0),
                    MainMenuReady = WorldShare.MainMenuReady(),
                    WorldReady = WorldShare.WorldReady,
                };

                HostWaitStep step = HostRules.Wait(wait);
                if (step == HostWaitStep.WorldReady)
                {
                    break;
                }

                if (step == HostWaitStep.BackAtMenu)
                {
                    Plugin.Log.LogWarning(
                        "[start game] back at the main menu before the world "
                            + "loaded: not hosting");
                    yield break;
                }

                if (step != HostWaitStep.WaitForLoad)
                {
                    loadStarted = true;
                }

                yield return null;
            }

            var begin = new BeginFacts
            {
                InSession = _host.Session.Active,
                Host = HostFactsOf(),
                SteamAsked = steam,
                SteamUp = _host.Steam is IGuiSection,
            };

            HostBegin how = HostRules.Begin(begin);
            if (how.Step == HostBeginStep.CantHost)
            {
                Plugin.Log.LogWarning(
                    $"[start game] can't host: {Refusals.Text(how.Why)}");
            }
            else if (how.Step == HostBeginStep.Steam)
            {
                ((IGuiSection)_host.Steam).Host();
                Plugin.Log.LogInfo("[start game] hosting with Steam");
            }
            else if (how.Step == HostBeginStep.Direct)
            {
                _host.Session.LastRefusal = null;
                bool ok = Direct(null, 0);
                int port = Plugin.Cfg.DirectPort.Value;
                string status = "started";
                if (!ok)
                {
                    status = _host.Tcp.Status;
                }

                Plugin.Log.LogInfo(
                    $"[start game] hosting with Direct on port {port}: "
                        + $"{status}");
            }
        }

        /// <summary>
        /// Whether Direct hosting asks the router to forward the port. A test
        /// game never touches the real router (the tests host over and over);
        /// it opens the port only on the fake router of --dwmp-upnp-test.
        /// </summary>
        public static bool OpensPort() =>
            HostRules.OpensPort(
                PortMapper.TestRouter != null,
                Plugin.Cfg.OpenPort.Value,
                Plugin.Instance.Host.TestMode);

        /// <summary>
        /// Host with Direct on the configured port; a password or player limit
        /// given here is for this time only.
        /// </summary>
        public bool Direct(string password, int maxPlayers)
        {
            string serverName = Plugin.Cfg.ServerName.Value;
            int port = Plugin.Cfg.DirectPort.Value;
            return _host.Tcp.Host(
                serverName,
                port,
                HostRules.PasswordOf(password, Plugin.Cfg.Password.Value),
                HostRules.PlayersOf(maxPlayers, Plugin.Cfg.MaxPlayers.Value));
        }
    }
}
