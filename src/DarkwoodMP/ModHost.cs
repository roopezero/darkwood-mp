using DarkwoodMP.Menus;
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

        /// The crouch co-op adds to a disarm. It lives as long as the game,
        /// so a crouch begun in a session still ends when its bar ends after
        /// the session has (the host stopped hosting mid-disarm).
        internal readonly World.DisarmAnimation DisarmCrouch =
            new World.DisarmAnimation();

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

        /// Host: the other players' characters kept beside the save, written
        /// whenever the game saves, also with no session.
        public readonly World.KeptCharacters.CharacterRecords Kept =
            new World.KeptCharacters.CharacterRecords();

        /// <summary>Our player's noise (Combat.PlayerNoise).</summary>
        internal readonly Combat.PlayerNoise Noise = new Combat.PlayerNoise();

        /// <summary>--dwmp-fps N: the frame cap, 0 for none.</summary>
        public int FpsCap;

        /// <summary>
        /// The screen's size before --dwmp-window, put back when a later test
        /// has none.
        /// </summary>
        public (int w, int h, UnityEngine.FullScreenMode mode)? WindowBefore;

        /// <summary>--dwmp-spawn TYPE: what the dummy test spawns.</summary>
        public string SpawnType;

        /// For tests ("--dwmp-test-quick-end"): skip the epilogue and the
        /// credits as soon as they start, also after leaving the session.
        public bool TestQuickEnd;

        /// The game is on its way from the ending (the credits) to the main
        /// menu (Chapters.ToCredits until Chapters.ToMenuAfterEnding gets
        /// there): leaving the host's world mustn't load the menu over it.
        public bool Ending;

        /// "--dwmp-debug-world": log every shared-world message sent and
        /// applied (WorldActions.Debug).
        public bool DebugWorld;

        /// Tests only: count what's done now as our own player's doing
        /// (WorldActions.ByLocalPlayer).
        public bool TestAsPlayer;

        /// <summary>
        /// Whether the game was started for the tests (a --dwmp-test...
        /// argument, the test world, or tools/run-test.sh's test control).
        /// Only then does the Multiplayer window offer the test world and
        /// its tools.
        /// </summary>
        public bool TestMode;

        /// The players' name tags' style, made at the first OnGUI.
        public UnityEngine.GUIStyle NameTagStyle;

        /// The shared essence and levels, and the stove's skill reset: also
        /// playing alone. A session adds its messages (Progression.Join).
        public readonly World.Progress.Progression Progress;

        /// <summary>
        /// Which part of a scripted event runs now, kept also alone
        /// (World.Scripted.EventParts).
        /// </summary>
        internal readonly World.Scripted.EventParts Parts =
            new World.Scripted.EventParts();

        /// Where the game's saves go (SaveRedirect), until the main menu
        /// loads again; made before Teardown, so a scene load ends the
        /// redirect first, as before.
        public readonly World.SaveFolders SaveFolders =
            new World.SaveFolders();

        /// A world being left (WorldTeardown), also alone.
        public readonly World.WorldTeardown Teardown =
            new World.WorldTeardown();

        /// The save book's file as last read (SaveBook), which the Start
        /// Game window reads at the main menu.
        public readonly World.SaveBookFile Book = new World.SaveBookFile();

        /// The game's skill tiers, found once (SkillTree.Tiers).
        internal readonly World.SkillTierCache Tiers =
            new World.SkillTierCache();

        /// Learning skills, also alone; each session handles the host's
        /// side and forgets a choice still waiting when it ends.
        internal readonly World.SkillLearning Learning =
            new World.SkillLearning();

        /// The Skills panel, also alone (World.SkillsWindow).
        internal readonly World.SkillsWindow SkillsPanel =
            new World.SkillsWindow();

        /// The game's pixel fonts for the mod's windows (PixelFont).
        internal readonly Menus.FontFaces Fonts =
            new Menus.FontFaces();

        /// The mod's windows' IMGUI skin (MenuSkin).
        internal readonly Menus.MenuStyles Styles =
            new Menus.MenuStyles();

        /// The mod's windows' hooks into the menus (MenuWindows).
        internal readonly Menus.MenuWindowList Windows =
            new Menus.MenuWindowList();

        /// The main and pause menus' list of entries.
        public readonly Menus.MenuList MenuList =
            new Menus.MenuList();

        /// The main menu's backdrop dimmed behind the mod's windows.
        public readonly Menus.MenuEntry MenuEntry =
            new Menus.MenuEntry();

        /// The menus' words drawn over the game's pictures of them.
        public readonly Menus.MenuWords MenuWords =
            new Menus.MenuWords();

        /// Where the menus' logo and version lines go.
        public readonly Menus.MenuLayout MenuLayout =
            new Menus.MenuLayout();

        /// The game's HUD hidden while the pause menu is up.
        public readonly Menus.PauseHud PauseHud =
            new Menus.PauseHud();

        /// The loading screen's lines, bar and tips.
        public readonly World.Loading.LoadingScreen Loading =
            new World.Loading.LoadingScreen();

        /// The screen a client sees while it joins a game.
        public readonly World.Loading.JoinScreen Join =
            new World.Loading.JoinScreen();

        /// The faster loading of a world, and its check (--dwmp-verify-load).
        public readonly World.Loading.LoadSpeed LoadSpeed =
            new World.Loading.LoadSpeed();

        /// Where a world's loading spends its time (--dwmp-profile-load).
        public readonly World.Loading.LoadProfiler LoadProfiler =
            new World.Loading.LoadProfiler();

        /// The mod's tooltip texts for items and places the game left bare.
        public readonly World.Texts.Tooltips Tooltips =
            new World.Texts.Tooltips();

        /// The yes-or-no question over the menus and windows.
        public readonly ConfirmDialog Confirm = new ConfirmDialog();

        /// Hides the game's cursor while the mod's windows show their own.
        public readonly GameCursor Cursor = new GameCursor();

        /// The mod's per-frame cost (--dwmp-profile-mod).
        public readonly ModProfiler Profiler = new ModProfiler();

        /// DragTest's made-up mouse, which the windows read instead of the
        /// real one while it's set (UiWindow).
        public Func<UnityEngine.Vector2> TestMouse;

        /// A new game started at the epilogue: it goes into the epilogue
        /// once its world is ready (World.Story.EpilogueStart).
        internal readonly World.Story.EpilogueStart EpilogueStart =
            new World.Story.EpilogueStart();

        /// The Start Game window, made by AddWindows.
        public StartGameWindow StartGame { get; private set; }

        /// The Options window, made by AddWindows.
        public OptionsWindow Options { get; private set; }

        /// Plugin.Load, where it made the two windows before, so they hook
        /// into the menus in the same order.
        public void AddWindows()
        {
            StartGame = new StartGameWindow();
            Options = new OptionsWindow();
        }

        /// The test control's connection and commands
        /// (--dwmp-test-control), at the main menu too.
        internal readonly TestControl Control = new TestControl();

        /// The test world's loading, its area and labels (World.TestArea).
        internal readonly World.TestArea TestArea = new World.TestArea();

        /// The world dump's sections (World.StateDump), with those the map
        /// test steps added.
        internal readonly World.DumpSections Dump = new World.DumpSections();

        /// The options window test (--dwmp-test-options).
        internal readonly OptionsTest OptionsTest = new OptionsTest();

        /// The window drag tests (--dwmp-test-drag, --dwmp-test-drag-real).
        internal readonly DragTest Drag = new DragTest();

        /// <param name="opensPort">whether hosting asks the router to
        /// forward the port (Host.OpenPort)</param>
        /// <summary>Whether and how the player starts to host.</summary>
        internal readonly DarkwoodMP.Hosting.HostStart Hosting;

        /// <summary>Finds Steam and works out the player's name.</summary>
        internal readonly Net.SteamStartup SteamStart;

        /// <summary>The Steam presence, the browser's World label.</summary>
        internal readonly Net.Presence.PresenceReport Presence;

        public ModHost(Func<bool> opensPort)
        {
            SteamStart = new Net.SteamStartup(this);
            Hosting = new DarkwoodMP.Hosting.HostStart(this);
            Presence = new Net.Presence.PresenceReport(this);
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
                BeforeLeave = World.KeptCharacters.Characters.SaveBeforeLeaving
            };

            Session.Add(Tcp);
            Progress = new World.Progress.Progression(Session);
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

        /// <summary>The Multiplayer window, made at Plugin.Load.</summary>
        internal Menus.CoopPanel Panel { get; private set; }

        public void AddPanel(UnityEngine.MonoBehaviour runner)
        {
            Panel = new Menus.CoopPanel(this, runner);
        }

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

        /// <summary>
        /// The host's world shared with joiners, and the joiner's download of
        /// it (World.WorldShare). Kept for the whole game: the main menu
        /// joins with it, and its cache of downloaded worlds outlives a
        /// session. Made by Plugin.Load where it was made before, so its
        /// handlers of the session's events run in the same order.
        /// </summary>
        public World.WorldShare Share { get; private set; }

        /// <param name="runner">runs its coroutines (the Plugin)</param>
        /// <param name="cacheRoot">BepInEx's cache folder</param>
        public void AddShare(UnityEngine.MonoBehaviour runner, string cacheRoot)
        {
            Share = new World.WorldShare(Session, runner, cacheRoot);
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
