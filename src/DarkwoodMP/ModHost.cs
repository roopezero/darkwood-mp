using System;
using System.Collections.Generic;
using BepInEx;
using DarkwoodMP.Net;

namespace DarkwoodMP
{
    /// <summary>
    /// What lives as long as the game: the Session object, its backends and
    /// the server browser, which the main menu needs when no session runs.
    /// Plugin owns it, and can't hold these itself: it has to stay loadable
    /// when DarkwoodMP.Core.dll is missing (see CoreCheck), so it has no
    /// field of a core type. ModHost also starts a CoopSession when a
    /// session starts and ends it when the session ends.
    /// </summary>
    internal sealed class ModHost : IDisposable
    {
        public readonly Session Session;
        public readonly TcpBackend Tcp;
        public readonly ServerBrowser Browser;

        /// The save id counters of the saves this game loaded and wrote.
        public readonly World.SaveIdCounters SaveIds =
            new World.SaveIdCounters();

        /// The tables ObjRef uses with no session: empty, apart from its
        /// scans of the scene.
        public readonly World.ObjNames AloneNames = new World.ObjNames(null);

        /// One generator for the random ids of shared objects
        /// (HostIds.NewId), so ids made in the same millisecond differ.
        public readonly Random IdRandom = new Random();

        /// The village cellar's full oxygen tank, shared once filled.
        public readonly World.SharedTank Tank = new World.SharedTank();

        /// One stack size for every stackable item.
        public readonly World.StackSizes Stacks;

        /// The generators the game started (Generator.Start), also alone: a
        /// world loaded before hosting has them too (Fuel checks them).
        public readonly HashSet<Generator> Generators =
            new HashSet<Generator>();

        /// The sprite collections by name hash, for the puppets' looks; kept
        /// as long as the game runs, as the game keeps them.
        public readonly Sync.SpriteCollections Sprites =
            new Sync.SpriteCollections();

        /// Lights outside the pathfinding grid, also when playing alone.
        public readonly Sync.LightGrid Lights = new Sync.LightGrid();

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
            Stacks = new World.StackSizes();
            // First, so a CoopSession exists before any other handler of
            // the start runs, and is gone before the others' cleanups.
            Session.OnSessionStarted += StartCoop;
            Session.OnSessionEnded += Coop.End;
        }

        /// The Steam backend once Steam is up; null before that and on GOG.
        /// Typed as the interface: no Steamworks type may appear here.
        public INetBackend Steam { get; private set; }

        /// <summary>
        /// The other players' puppets, and our player's look sent to them.
        /// Kept for the whole game: the tests' dummy (--dwmp-dummy) is a
        /// puppet without a session. Made by Plugin.Load where it was made
        /// before, so its handlers of the session's events run in the same
        /// order.
        /// </summary>
        public Sync.PlayerSync Players { get; private set; }

        public void AddPlayers()
        {
            Players = new Sync.PlayerSync(Session);
        }

        public void AddSteam(INetBackend steam)
        {
            Session.Add(steam);
            Steam = steam;
        }

        // A backend can take over from another without the old session
        // ending first (accepting a Steam invite while hosting Direct), so
        // the old CoopSession is ended before the new one hooks in.
        private void StartCoop()
        {
            Coop.End();
            Coop.Begin(new CoopSession(Session));
        }

        public void Dispose()
        {
            Coop.End();
            Stacks.Dispose();
            Session.Dispose();
        }
    }
}
