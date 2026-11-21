using System;
using BepInEx;
using DarkwoodMP.Net;

namespace DarkwoodMP
{
    /// <summary>
    /// What lives as long as the game: the Session object, its backends and
    /// the server browser, which the main menu needs when no session runs.
    /// Plugin owns it, and can't hold these itself: it has to stay loadable
    /// when DarkwoodMP.Core.dll is missing (see CoreCheck), so it has no
    /// field of a core type.
    /// </summary>
    internal sealed class ModHost : IDisposable
    {
        public readonly Session Session;
        public readonly TcpBackend Tcp;
        public readonly ServerBrowser Browser;

        /// <param name="opensPort">whether hosting asks the router to
        /// forward the port (Host.OpenPort)</param>
        public ModHost(Func<bool> opensPort)
        {
            var log = new BepInLog();
            Session = new Session(new UnityClock(), log, PluginInfo.Version)
            {
                LocalKey = PlayerKey.Load(Paths.ConfigPath, log)
            };

            Tcp = new TcpBackend(log, PluginInfo.Version)
            {
                PlayerCount = () => Session.PlayerCount,
                MapPort = opensPort,
                BeforeLeave = World.Characters.SaveBeforeLeaving
            };

            Session.Add(Tcp);
            Browser = new ServerBrowser(Paths.ConfigPath, log);
        }

        /// The Steam backend once Steam is up; null before that and on GOG.
        /// Typed as the interface: no Steamworks type may appear here.
        public INetBackend Steam { get; private set; }

        public void AddSteam(INetBackend steam)
        {
            Session.Add(steam);
            Steam = steam;
        }

        public void Dispose()
        {
            Session.Dispose();
        }
    }
}
