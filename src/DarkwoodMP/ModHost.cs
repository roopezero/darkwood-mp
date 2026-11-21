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

        /// The workbenches' storage rows, which grow with each upgrade also
        /// without co-op.
        public readonly World.Workbenches.WorkbenchStorage Workbenches =
            new World.Workbenches.WorkbenchStorage();

        /// What a player can place, built once from the item database.
        public readonly World.Inventories.Placeables Placeables =
            new World.Inventories.Placeables();

        /// One generator for the random ids of shared objects
        /// (HostIds.NewId), so ids made in the same millisecond differ.
        public readonly Random IdRandom = new Random();

        /// The village cellar's full oxygen tank, shared once filled.
        public readonly World.SharedTank Tank = new World.SharedTank();

        /// Host: who is owed their part of fired once-only triggers, kept
        /// across sessions and world reloads and written beside the save.
        public readonly World.Owed.OwedRecordFile Owed =
            new World.Owed.OwedRecordFile();

        /// One stack size for every stackable item.
        public readonly World.StackSizes Stacks;

        /// The generators the game started (Generator.Start), also alone: a
        /// world loaded before hosting has them too (Fuel checks them).
        public readonly HashSet<Generator> Generators =
            new HashSet<Generator>();

        /// The host's stand-ins for the other players, for its AI; also the
        /// test dummy's in a game played alone.
        public readonly Combat.Proxies Proxies = new Combat.Proxies();

        /// The host's culling grid kept entered round every player. The
        /// test dummy needs it with no session.
        public readonly World.Culling.SharedCulling Culling =
            new World.Culling.SharedCulling();

        /// Each enemy's threat for each player, while there are stand-ins.
        public readonly Combat.Threats Threats;

        /// The stand-in an enemy checking out a location chases.
        public readonly Combat.JoinerChases Chases = new Combat.JoinerChases();

        /// The host's night attackers and worms for every player, and each
        /// hideout's budget; the test dummy needs them alone too.
        internal readonly Combat.NightSpawner Nights =
            new Combat.NightSpawner();

        /// Keeps a dialogue closable when its NPC goes away, also alone.
        public readonly World.Talk.DialogueGuard Dialogues =
            new World.Talk.DialogueGuard();

        /// Doors opened here while their leaf was switched off, also alone;
        /// a session forgets them when it ends (CoopSession).
        public readonly World.Doors.DoorLeaf Leaves =
            new World.Doors.DoorLeaf();

        /// What the host's enemies and our player's weapon spawned, also
        /// alone (the test dummy is hurt with no session).
        internal readonly Combat.AreaSpawns Spawns = new Combat.AreaSpawns();

        /// The sprite collections by name hash, for the puppets' looks; kept
        /// as long as the game runs, as the game keeps them.
        public readonly Sync.Players.SpriteCollections Sprites =
            new Sync.Players.SpriteCollections();

        /// Lights outside the pathfinding grid, also when playing alone.
        public readonly Sync.Players.LightGrid Lights =
            new Sync.Players.LightGrid();

        /// <summary>The active characters (Combat.AllCharacters).</summary>
        internal readonly Combat.AllCharacters Characters =
            new Combat.AllCharacters();

        /// <summary>Our player's noise (Combat.PlayerNoise).</summary>
        internal readonly Combat.PlayerNoise Noise = new Combat.PlayerNoise();

        /// <summary>
        /// Which part of a scripted event runs now, kept also alone
        /// (World.Scripted.EventParts).
        /// </summary>
        internal readonly World.Scripted.EventParts Parts =
            new World.Scripted.EventParts();

        /// <param name="opensPort">whether hosting asks the router to
        /// forward the port (Host.OpenPort)</param>
        public ModHost(Func<bool> opensPort)
        {
            Threats = new Combat.Threats(Proxies);
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
            Session.OnSessionEnded += Spawns.Clear;
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
        public Sync.Players.PlayerSync Players { get; private set; }

        public void AddPlayers()
        {
            Players = new Sync.Players.PlayerSync(Session);
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
            Parts.Dispose();
            Stacks.Dispose();
            Session.Dispose();
        }
    }
}
