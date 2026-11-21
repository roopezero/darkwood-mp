using System;
using System.Collections.Generic;
using System.IO;
using DarkwoodMP.Combat;
using DarkwoodMP.Net;
using DarkwoodMP.World.Progress;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Shared progression: every player has the same essence and the same
    /// number of levels to take, and each picks their own skills.
    ///
    /// Darkwood counts essence from the start of the game (Player.experience)
    /// against cumulative requirements (Player.levelRequirements[level]).
    /// Cooking essence at a stove (InvItemClass.consumeExp) fills the bar; a
    /// full bar gives a skill point, the level (SkillsMenu.showHideTiers raises
    /// Player.currentLevel) and the skill choice.
    ///
    /// Here the host keeps the shared essence and the shared level (the most
    /// levels anyone has taken). A client's cooking is a request (EssenceAdd)
    /// that the host adds to the shared essence, or refuses while the shared
    /// bar is full. Each player's Player.currentLevel is their own (it is in
    /// their character, with their skills), and their Player.experience is set
    /// to the shared essence, but no further than their own next level: so a
    /// player behind the shared level sees a full bar at the stove, and takes
    /// the level there. Everyone ends up with the same levels and points; the
    /// skills are each player's.
    ///
    /// The stove only gives the level and its skill point (TakeLevelAtStove, in
    /// place of the game's skill choice):
    /// the points are spent on the Skills panel, anywhere (World.SkillTree,
    /// SkillsPanel).
    ///
    /// A skill reset at the stove (a button while the stove's menu is open)
    /// takes back all of a player's chosen skills, good and negative, and gives
    /// back their points to spend anew; the levels stay. It can be done only in
    /// the morning, from dawn until the player first walks out of their hideout
    /// (Controller.isAfterNight, the same time AfterNight holds the clock for),
    /// and once a day (Controller.day). The host checks both for a client: its
    /// PlayerState says whether it is in its morning. A new player's character
    /// starts with no skills (World.Characters), and takes the shared levels at
    /// the stove.
    ///
    /// The host writes the shared essence, level and reset days beside its save
    /// (prof&lt;id&gt;/darkwoodmp_progress.json) when its game saves. It starts
    /// sharing once someone joins a world that has no such file yet, from the
    /// host's own essence and level.
    ///
    /// Playing alone (no session), the stove's reset works the same way, with
    /// the player as the only one: the first reset starts the file from the
    /// player's own essence and level, and from then on the game's essence is
    /// kept there, so the levels taken back can be taken again at the stove
    /// (one at a time, as the bar fills each time).
    /// </summary>
    public static class Progression
    {
        private static Session S => Session.Instance;

        /// <summary>Playing alone: no co-op session, in a world.</summary>
        private static bool Solo => ProgressSideRules.Solo(Side());

        private sealed class Saved
        {
            public int Exp;
            public int Level;
            // hash of player key ("host": the host) -> day
            public Dictionary<string, int> Resets =
                new Dictionary<string, int>();
        }

        /// <summary>
        /// The shared essence and level (host: its own; client: what the host
        /// last said).
        /// </summary>
        public static int SharedExp { get; private set; }
        public static int SharedLevel { get; private set; }
        private static bool _known;       // client: the host has sent the state
        private static Saved _saved;      // host
        private static string _savedPath; // host: which world _saved is for
        private static Seconds _nextSync;

        public static void Register(Session s)
        {
            s.Register(MsgType.ProgressState, OnState);
            s.Register(MsgType.EssenceAdd, OnEssence);
            s.Register(MsgType.LevelTaken, OnLevelTaken);
            s.Register(MsgType.SkillReset, OnSkillReset);
            SkillTree.Register(s);
            s.OnSessionEnded += () =>
            {
                _known = false;
                _saved = null;
                _savedPath = null;
                _waitingReset = false;
            };
        }

        // ---- levels ---------------------------------------------------------

        /// <summary>
        /// The shared numbers and this player's level requirements (none
        /// with no player): the only code that reads them for the rules.
        /// </summary>
        private static ProgressFacts FactsOf(Player p)
        {
            int[] requirements = null;
            if (p != null && p.levelRequirements != null)
            {
                requirements = p.levelRequirements.ToArray();
            }

            return new ProgressFacts
            {
                SharedExp = SharedExp,
                SharedLevel = SharedLevel,
                Requirements = requirements,
                HasPlayer = p != null,
            };
        }

        /// <summary>
        /// Which side of the shared progression our game is on now.
        /// </summary>
        private static SideFacts Side()
        {
            var s = S;
            return new SideFacts
            {
                SessionActive = s != null && s.Active,
                IsHost = s != null && s.IsHost,
                InWorld = WorldShare.InWorld,
                ClientMode = NetEnemies.ClientMode,
                Known = _known,
                Together = Hideouts.Together,
                HasRecord = _saved != null,
                HasPath = _savedPath != null,
                HasPlayer = Player.Instance != null,
            };
        }

        /// <summary>
        /// Our player at the stove's menu.
        /// </summary>
        private static StoveFacts StoveOf(Player p)
        {
            var menu = StoveMenu;
            var skills = Singleton<SkillsMenu>.Instance;
            return new StoveFacts
            {
                HasPlayer = p != null,
                InLevelingMenu = p != null && p.inLevelingMenu,
                HasMenu = menu != null,
                OpenedAndTweened = menu != null && menu.openedAndTweened,
                WantToLevel = menu != null && menu.wantToLevel,
                SkillsMenuOpen = skills != null && skills.opened,
            };
        }

        private static int Req(Player p, int level)
        {
            return ProgressRules.Requirement(FactsOf(p).Requirements, level);
        }

        /// <summary>
        /// The most levels a player can have now: the shared level, or what the
        /// shared essence allows.
        /// </summary>
        internal static int MostLevels(Player p)
        {
            return ProgressRules.MostLevels(FactsOf(p));
        }

        /// <summary>The stove's menu (LevelingMenu), or null.</summary>
        private static LevelingMenu StoveMenu
        {
            get
            {
                if (Singleton<UI>.Instance != null)
                {
                    return Singleton<UI>.Instance.LevelingMenu;
                }

                return null;
            }
        }

        /// <summary>
        /// Set our player's essence to what the shared essence shows them, and
        /// refresh an open stove menu.
        /// </summary>
        private static void Apply(Player p)
        {
            if (p == null)
            {
                return;
            }

            int want = ProgressRules.Display(FactsOf(p), p.currentLevel);
            if (p.experience == want)
            {
                return;
            }

            p.experience = want;
            var menu = StoveMenu;
            var skills = Singleton<SkillsMenu>.Instance;
            try
            {
                if (menu != null && p.inLevelingMenu && menu.openedAndTweened
                    && menu.LevelingLiquid != null
                    && (skills == null || !skills.opened))
                {
                    menu.updateLiquid();
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning(
                    $"[progress] couldn't refresh the stove: {e.Message}");
            }
        }

        // ---- host -----------------------------------------------------------

        private static string SavedPath()
        {
            var sm = Singleton<SaveManager>.Instance;
            if (sm == null || Core.currentProfile == null)
            {
                return null;
            }

            return Path.Combine(
                Path.Combine(
                    sm.baseSaveDirectory, "prof" + Core.currentProfile.id),
                "darkwoodmp_progress.json");
        }

        /// <summary>
        /// Host: the shared progression of this world, loaded or started (when
        /// someone is playing with us); null: none.
        /// </summary>
        private static Saved HostSaved(bool start, bool alone = false)
        {
            var s = S;
            var p = Player.Instance;
            bool solo = Solo;
            string path = SavedPath();
            var record = new RecordFacts
            {
                Solo = solo,
                IsHost = s != null && s.IsHost,
                InWorld = WorldShare.InWorld,
                Loading = WorldShare.Loading,
                HasPlayer = p != null,
                ClientMode = NetEnemies.ClientMode,
                HasPath = path != null,
            };

            if (!RecordRules.Keeps(record))
            {
                return null;
            }

            if (path != _savedPath)
            {
                _saved = null;
                _savedPath = path;
                try
                {
                    if (File.Exists(path))
                    {
                        _saved = JsonConvert.DeserializeObject<Saved>(
                            File.ReadAllText(path));
                    }
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning(
                        $"[progress] couldn't read {path}: {e.Message}");
                }

                if (_saved != null)
                {
                    if (_saved.Resets == null)
                    {
                        _saved.Resets = new Dictionary<string, int>();
                    }

                    SharedExp = _saved.Exp;
                    SharedLevel = _saved.Level;
                    Plugin.Log.LogInfo(
                        $"[progress] this world's shared essence {SharedExp}, "
                            + $"level {SharedLevel}");
                }
            }

            int peers = 0;
            if (s != null)
            {
                peers = s.Peers.Count;
            }

            var starting = new RecordFacts
            {
                Solo = solo,
                HasRecord = _saved != null,
                Start = start,
                Alone = alone,
                Peers = peers,
            };

            if (RecordRules.Starts(starting))
            {
                _saved =
                    new Saved { Exp = p.experience, Level = p.currentLevel };
                SharedExp = _saved.Exp;
                SharedLevel = _saved.Level;
                Plugin.Log.LogInfo(
                    "[progress] shared progression starts from yours: essence "
                        + $"{SharedExp}, level {SharedLevel}");
            }

            return _saved;
        }

        private static void SendState(ulong to)
        {
            var s = S;
            var w = s.Begin(MsgType.ProgressState);
            Progress.ProgressState.Write(
                w,
                new ProgressNumbers { Exp = SharedExp, Level = SharedLevel });
            if (to == 0)
            {
                s.Broadcast(w, Channel.Control, reliable: true);
            }
            else
            {
                s.SendTo(to, w, Channel.Control, reliable: true);
            }
        }

        /// <summary>
        /// Host: someone's character was just sent; send them the shared
        /// progression after it.
        /// </summary>
        public static void HostSendTo(ulong peer)
        {
            if (HostSaved(start: true) != null)
            {
                SendState(peer);
            }
        }

        /// <summary>
        /// Host: take in our own player's essence and level, tell everyone when
        /// they changed, and show ours.
        /// </summary>
        private static void HostSync()
        {
            var saved = HostSaved(start: true);
            var p = Player.Instance;
            if (saved == null || p == null)
            {
                return;
            }

            var shared =
                new ProgressNumbers { Exp = SharedExp, Level = SharedLevel };
            var merged = ProgressRules.Merge(
                shared,
                new ProgressNumbers
                {
                    Exp = p.experience,
                    Level = p.currentLevel,
                });

            if (merged != shared)
            {
                SharedExp = saved.Exp = merged.Exp;
                SharedLevel = saved.Level = merged.Level;
                if (WorldActions.Debug)
                {
                    Plugin.Log.LogInfo(
                        $"[progress] shared essence {SharedExp}, level "
                            + $"{SharedLevel}");
                }

                if (!Solo)
                {
                    SendState(0);
                }
            }

            Apply(p);
        }

        /// <summary>
        /// Host: the game saved; write the shared progression beside it.
        /// </summary>
        public static void OnHostSaved()
        {
            var write = new WriteFacts
            {
                Snapshotting = WorldShare.Snapshotting,
                ClientMode = NetEnemies.ClientMode,
                HasRecord = _saved != null,
                HasPath = _savedPath != null,
                PathCurrent = _savedPath == SavedPath(),
            };

            if (!RecordRules.Writes(write))
            {
                return;
            }

            try
            {
                File.WriteAllText(
                    _savedPath, JsonConvert.SerializeObject(_saved));
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning(
                    $"[progress] couldn't write {_savedPath}: {e.Message}");
            }
        }

        private static void OnEssence(ulong from, PacketReader r)
        {
            int amount = Progress.EssenceAdd.Read(r);
            var p = Player.Instance;
            if (!Handles(p))
            {
                return;
            }

            var verdict = ProgressRules.MayAddEssence(FactsOf(p), amount);
            if (verdict.Refused)
            {
                SendState(from);
                Coop.Current.Authority.Refuse(verdict);
                return;
            }

            SharedExp += amount;
            _saved.Exp = SharedExp;
            Plugin.Log.LogInfo(
                $"[progress] {S.NameOf(from)} cooked {amount} essence: shared "
                    + $"{SharedExp}");
            SendState(0);
            Apply(p);
        }

        private static void OnLevelTaken(ulong from, PacketReader r)
        {
            int level = Progress.LevelTaken.Read(r);
            var p = Player.Instance;
            if (!Handles(p))
            {
                return;
            }

            var facts = FactsOf(p);
            int allowed = ProgressRules.Allowed(facts);
            if (level > allowed)
            {
                Plugin.Log.LogInfo(
                    $"[progress] {S.NameOf(from)} says level {level}; the "
                        + $"shared essence allows {allowed}");
            }

            level = ProgressRules.AcceptedLevel(facts, level);
            if (!ProgressRules.Raises(facts, level))
            {
                return;
            }

            SharedLevel = _saved.Level = level;
            Plugin.Log.LogInfo(
                $"[progress] {S.NameOf(from)} took level {level}: everyone can "
                    + "take it at a stove");
            SendState(0);
        }

        /// <summary>
        /// Host: whether it takes in a client's EssenceAdd or LevelTaken.
        /// The record (which can start here) is read only as the host.
        /// </summary>
        private static bool Handles(Player p)
        {
            bool host = S.IsHost;
            bool record = host && HostSaved(start: true) != null;
            return ProgressRules.Handles(host, record, p != null);
        }

        private static int Today
        {
            get
            {
                if (Singleton<Controller>.Instance != null)
                {
                    return Singleton<Controller>.Instance.day;
                }

                return 0;
            }
        }

        /// <summary>
        /// Whether this player (0: ours) is in their morning: after dawn, not
        /// yet out of their hideout (Controller.isAfterNight).
        /// </summary>
        internal static bool InMorning(ulong peer)
        {
            if (peer == 0)
            {
                return Mornings.AfterNight.Local;
            }

            var sync = Plugin.Instance.Host.Players;
            return sync != null && sync.Remotes.TryGetValue(peer, out var rp)
                && rp.Last != null && rp.Last.AfterNight;
        }

        /// <summary>
        /// Host: whether this player (key hash, or "host"; peer 0: ours) may
        /// reset now; if so, it counts as today's.
        /// </summary>
        private static Verdict TakeReset(string who, ulong peer)
        {
            var morning = SkillResetRules.Morning(InMorning(peer));
            if (morning.Refused)
            {
                return morning;
            }

            var saved =
                HostSaved(start: true, alone: SkillResetRules.Alone(who));
            int day = 0;
            bool hasDay =
                saved != null && saved.Resets.TryGetValue(who, out day);
            var reset = new ResetFacts
            {
                HasRecord = saved != null,
                HasDay = hasDay,
                Day = day,
                Today = Today,
            };

            var verdict = SkillResetRules.Today(reset);
            if (verdict.Refused)
            {
                return verdict;
            }

            saved.Resets[who] = Today;
            return verdict;
        }

        private static void OnSkillReset(ulong from, PacketReader r)
        {
            bool host = S.IsHost;
            string key = null;
            bool hasKey = host && S.Keys.TryGetValue(from, out key);
            if (!SkillResetRules.Handles(host, hasKey))
            {
                return;
            }

            var verdict = TakeReset(SkillResetRules.Who(key), from);
            if (verdict.Refused)
            {
                Coop.Current.Authority.Refuse(verdict);
                return;
            }

            Plugin.Log.LogInfo(
                $"[progress] {S.NameOf(from)} reset their skills (day "
                    + $"{Today})");
        }

        // ---- client ---------------------------------------------------------

        /// <summary>
        /// The host, or playing alone: our game keeps the shared progression.
        /// </summary>
        private static void OnState(ulong from, PacketReader r)
        {
            if (S == null || from != S.HostId || !NetEnemies.ClientMode)
            {
                return;
            }

            var shared = Progress.ProgressState.Read(r);
            SharedExp = shared.Exp;
            SharedLevel = shared.Level;
            bool first = !_known;
            _known = true;
            if (first)
            {
                Player player = Player.Instance;
                int? yours = null;
                if (player != null)
                {
                    yours = player.currentLevel;
                }

                Plugin.Log.LogInfo(
                    $"[progress] shared essence {SharedExp}, level "
                        + $"{SharedLevel}; yours {yours}");
            }

            Apply(Player.Instance);
        }

        /// <summary>
        /// Client: tell the host our own level (after a level, a reset, our
        /// character's arrival).
        /// </summary>
        private static void SendLevel(Player p)
        {
            if (!NetEnemies.ClientMode || p == null)
            {
                return;
            }

            var w = S.Begin(MsgType.LevelTaken);
            Progress.LevelTaken.Write(w, p.currentLevel);
            S.SendToHost(w, Channel.Control, reliable: true);
        }

        /// <summary>
        /// Client: our character came from the host (World.Characters).
        /// </summary>
        public static void AfterCharacter(Player p)
        {
            SendLevel(p);
            if (_known)
            {
                Apply(p);
            }
        }

        /// <summary>
        /// Either side: our player cooked essence (amount: what it added to
        /// Player.experience).
        /// </summary>
        internal static void Cooked(int amount)
        {
            var route = ProgressSideRules.Cooked(Side(), amount);
            if (route == ProgressRoute.Client)
            {
                // predicted; the host's ProgressState follows
                SharedExp += amount;
                var w = S.Begin(MsgType.EssenceAdd);
                Progress.EssenceAdd.Write(w, amount);
                Coop.Current.Authority.Request(w, $"cook {amount} essence");
            }
            else if (route == ProgressRoute.Host)
            {
                HostSync();
            }
        }

        /// <summary>
        /// Either side: our player took a level (SkillsMenu.confirmSkills).
        /// </summary>
        internal static void LevelTaken()
        {
            var p = Player.Instance;
            var route = ProgressSideRules.LevelTaken(Side());
            if (route == ProgressRoute.Client)
            {
                SendLevel(p);
                Apply(p);
            }
            else if (route == ProgressRoute.Host)
            {
                HostSync();
            }
        }

        /// <summary>
        /// Call every frame: the host takes in what its own game did by itself
        /// (picked-up essence, a loaded save).
        /// </summary>
        public static void Update()
        {
            var now = new Seconds(Time.unscaledTime);
            if (!ProgressSideRules.Due(now, _nextSync))
            {
                return;
            }

            _nextSync = ProgressSideRules.Next(now);
            var tick = ProgressSideRules.Update(Side());
            // Back at the main menu: forget the world's file, so a reload
            // (maybe of an older save) reads it again.
            if (tick.Forgets)
            {
                _saved = null;
                _savedPath = null;
            }

            if (tick.Route == ProgressRoute.Host)
            {
                HostSync();
            }
            else if (tick.Route == ProgressRoute.Client)
            {
                Apply(Player.Instance);
            }
        }

        // ---- the stove's level ----------------------------------------------

        /// <summary>
        /// The stove's full bar (LevelingMenu.checkIfWantToLevel ->
        /// SkillsMenu.showHideTiers): take the level and its skill point,
        /// without the game's skill choice (the point is spent on the Skills
        /// panel). The rest is what SkillsMenu.confirmSkills does after a
        /// choice: the bar starts again (and gives the next level if it's still
        /// full), and levels 2, 3, 5, 6 and 7 bring their dream (only the
        /// host's in co-op, World.DreamSync).
        /// </summary>
        internal static void TakeLevelAtStove()
        {
            var p = Player.Instance;
            var menu = StoveMenu;
            if (p == null || menu == null)
            {
                return;
            }

            p.currentLevel++;
            p.gainSaturation(StoveRules.Saturation);
            var d = Singleton<Dreams>.Instance;
            bool client = d != null && NetEnemies.ClientMode
                && Hideouts.Together,
                was = d != null && d.disabled;
            if (client)
            {
                // a client's level brings no dream of its own
                d.disabled = true;
            }

            try
            {
                p.gainExp(0);
                menu.resetLiquid();
                try
                {
                    AudioController.Play("UI_placeItem");
                }
                catch
                {
                }

                int l = p.currentLevel;
                if (d != null
                    && StoveRules.BringsDream(l, HadDream(d, l), d.disabled))
                {
                    d.wantToDream = true;
                    MarkDream(d, l);
                    if (l == StoveRules.FirstDreamLevel)
                    {
                        d.startTransition.dreamToTransitionTo =
                            StoveRules.FirstDream;
                    }
                }

                if (d != null && Transitions(d))
                {
                    Core.forbidInputs = true;
                    if (Singleton<CamMain>.Instance != null)
                    {
                        Singleton<CamMain>.Instance.gameObject.SetActive(true);
                    }

                    Singleton<UI>.Instance.InventorySelectionPrompt.Hide();
                    d.startTransition.transition();
                    menu.hide(forceHide: true);
                }
                else if (d != null)
                {
                    d.wantToDream = false;
                }
            }
            finally
            {
                if (client)
                {
                    d.disabled = was;
                }
            }

            DreamSync.AfterConfirm(d);
            LevelTaken();
            Plugin.Log.LogInfo(
                $"[progress] took level {p.currentLevel} at the stove: "
                    + $"{SkillTree.Points(p)} skill points to spend");
            p.displayMessage(
                $"A skill point to spend: press {SkillsPanel.KeyName} for the "
                    + "Skills");
        }

        /// <summary>
        /// Whether the level's dream was had (Dreams.hadDreamAtLvlN).
        /// </summary>
        private static bool HadDream(Dreams d, int level)
        {
            switch (level)
            {
                case 2:
                    return d.hadDreamAtLvl2;
                case 3:
                    return d.hadDreamAtLvl3;
                case 5:
                    return d.hadDreamAtLvl5;
                case 6:
                    return d.hadDreamAtLvl6;
                case 7:
                    return d.hadDreamAtLvl7;
                default:
                    return false;
            }
        }

        /// <summary>The level's dream counts as had.</summary>
        private static void MarkDream(Dreams d, int level)
        {
            switch (level)
            {
                case 2:
                    d.hadDreamAtLvl2 = true;
                    break;
                case 3:
                    d.hadDreamAtLvl3 = true;
                    break;
                case 5:
                    d.hadDreamAtLvl5 = true;
                    break;
                case 6:
                    d.hadDreamAtLvl6 = true;
                    break;
                case 7:
                    d.hadDreamAtLvl7 = true;
                    break;
            }
        }

        /// <summary>
        /// Whether the wanted dream's transition starts; the game is asked
        /// for a preset only in a random world.
        /// </summary>
        private static bool Transitions(Dreams d)
        {
            bool random = Core.randomGeneration;
            bool preset = d.wantToDream && random
                && d.hasUnspawnedRandomPresetForChapter(
                    Singleton<WorldGenerator>.Instance.chapterID);
            return StoveRules.Transitions(d.wantToDream, random, preset);
        }

        // ---- skill reset ----------------------------------------------------

        /// <summary>
        /// Take back all of a player's chosen skills (good and negative).
        /// keepLevel: the levels stay, and the points come back to spend again
        /// (a reset); otherwise Player.currentLevel goes to 0 (a new character,
        /// which takes the shared levels at the stove).
        /// </summary>
        public static void ClearSkills(Player p, bool keepLevel = false)
        {
            var ps = p.skills;
            if (ps == null)
            {
                return;
            }

            var all = new HashSet<PlayerSkill>();
            foreach (var sk in ps.skills)
            {
                if (sk != null)
                {
                    all.Add(sk);
                }
            }

            foreach (var sk in ps.progressionSkills)
            {
                if (sk != null)
                {
                    all.Add(sk);
                }
            }

            var menu = Singleton<SkillsMenu>.Instance;
            var tiers = SkillTree.Tiers;
            if (tiers != null)
            {
                foreach (var t in tiers)
                {
                    foreach (var sk in t.skills)
                    {
                        if (sk != null)
                        {
                            all.Add(sk);
                        }
                    }

                    foreach (var sk in t.negativeSkills)
                    {
                        if (sk != null)
                        {
                            all.Add(sk);
                        }
                    }
                }
            }

            int n = 0;
            foreach (var sk in all)
            {
                if (sk.chosen)
                {
                    sk.initialize(destBool: false);
                    n++;
                }
            }

            ps.skills.RemoveAll(sk => sk == null || all.Contains(sk));
            if (menu != null && menu.chosenSkills != null)
            {
                menu.chosenSkills.Clear();
            }

            ps.SkillPoints = 0;
            ps.refreshAll();
            if (!keepLevel)
            {
                p.currentLevel = 0;
            }

            Plugin.Log.LogInfo(
                $"[progress] skills reset: {n} taken back, level "
                    + $"{p.currentLevel}");
        }

        private static bool CanResetHere(Player p)
        {
            return SkillResetRules.CanResetHere(StoveOf(p));
        }

        private static bool _waitingReset;

        private static void ResetHere()
        {
            var p = Player.Instance;
            if (!CanResetHere(p))
            {
                return;
            }

            if (NetEnemies.ClientMode)
            {
                if (!SkillResetRules.Asks(_waitingReset))
                {
                    return;
                }

                _waitingReset = true;
                Coop.Current.Authority.Request(
                    S.Begin(MsgType.SkillReset), "reset skills",
                    (ok, reason, _) =>
                {
                    _waitingReset = false;
                    if (ok)
                    {
                        DoReset(Player.Instance);
                    }
                    else
                    {
                        Player instance = Player.Instance;
                        if (instance != null)
                        {
                            instance.displayMessage(reason);
                        }

                        Plugin.Log.LogInfo(
                            $"[progress] reset refused: {reason}");
                    }
                }, inventory: false);

                return;
            }

            var verdict = TakeReset(SkillResetRules.HostWho, 0);
            if (verdict.Refused)
            {
                string why = Refusals.Text(verdict);
                p.displayMessage(why);
                Plugin.Log.LogInfo($"[progress] reset refused: {why}");
                return;
            }

            DoReset(p);
        }

        private static void DoReset(Player p)
        {
            if (p == null)
            {
                return;
            }

            ClearSkills(p, keepLevel: true);
            if (ProgressSideRules.AfterReset(Side()) == ProgressRoute.Client)
            {
                SendLevel(p);
                Apply(p);
            }
            else
            {
                HostSync();
            }

            p.displayMessage(
                $"Skills reset: {SkillTree.Points(p)} skill points to spend "
                    + $"({SkillsPanel.KeyName})");
        }

        /// <summary>At our stove: the levels waiting, the points to spend, and
        /// the reset button in the morning, in the mod's menu style, with a
        /// window box behind them so they read over the world.</summary>
        public static void OnGUI()
        {
            var p = Player.Instance;
            if (!StoveRules.OverlayOpen(
                WorldShare.InWorld, UiState.MenuOver, CanResetHere(p)))
            {
                return;
            }

            int waiting = StoveRules.LevelsToTake(
                Hideouts.Together || Solo, SharedLevel, p.currentLevel);
            int points = SkillTree.Points(p);
            bool morning = InMorning(0);
            var lines = new List<string>(2);
            if (waiting > 0)
            {
                lines.Add($"Levels to take: {waiting}");
            }

            if (points > 0)
            {
                lines.Add(
                    $"Skill points to spend: {points} ({SkillsPanel.KeyName}: "
                        + "Skills)");
            }

            if (!StoveRules.OverlayShows(waiting, points, morning))
            {
                return;
            }

            MenuSkin.Ensure();
            var skin = GUI.skin;
            GUI.skin = MenuSkin.Skin;
            float s = Ui.S, pad = 6 * s, line = PixelFont.Line + 2 * s;
            string button = "Reset skills (mornings, once a day)";
            if (_waitingReset)
            {
                button = "Asking the host...";
            }

            float w =
                PixelFont.Width("Reset skills (mornings, once a day)") + 24 * s;
            foreach (var l in lines)
            {
                w = Math.Max(w, PixelFont.Width(l) + 2 * pad);
            }

            float row = 0f;
            if (morning)
            {
                row = Ui.RowH;
                if (lines.Count > 0)
                {
                    row += pad;
                }
            }

            float h = 2 * pad + lines.Count * line + row;
            float x = Mathf.Round((Screen.width - w) / 2f),
                y = Mathf.Round(Screen.height - h - 40 * s);
            GUI.Box(new Rect(x, y, w, h), GUIContent.none, MenuSkin.Window);
            float ly = y + pad;
            foreach (var l in lines)
            {
                Ui.Label(
                    new Rect(x + pad, ly, w - 2 * pad, line), l,
                    MenuSkin.Bright, TextAnchor.MiddleCenter);
                ly += line;
            }

            if (morning)
            {
                if (lines.Count > 0)
                {
                    ly += pad;
                }

                if (Ui.Button(
                    new Rect(x + pad, ly, w - 2 * pad, Ui.RowH), button,
                    !_waitingReset))
                {
                    ResetHere();
                }
            }

            GUI.skin = skin;
        }

        /// <summary>
        /// For tests: reset our skills at the stove as the button does.
        /// </summary>
        internal static void TestReset() => ResetHere();
        internal static bool TestCanReset => CanResetHere(Player.Instance);
        internal static bool TestWaitingReset => _waitingReset;

        /// <summary>
        /// For tests: give our player this many levels and the essence they
        /// need (and a skill chosen for each).
        /// </summary>
        internal static void TestGiveLevels(int n, bool chooseSkills = true)
        {
            var p = Player.Instance;
            var tiers = SkillTree.Tiers;
            if (p == null || tiers == null)
            {
                return;
            }

            int given = 0;
            if (chooseSkills)
            {
                foreach (var t in tiers)
                {
                    foreach (var sk in t.skills)
                    {
                        if (given < n && sk != null && !sk.chosen)
                        {
                            sk.initialize(destBool: true);
                            given++;
                        }
                    }
                }
            }

            p.skills.refreshAll();
            p.currentLevel += n;
            p.experience = Req(p, p.currentLevel - 1);
        }

        /// <summary>
        /// For tests: our level, essence, the shared numbers and how many
        /// skills are chosen.
        /// </summary>
        internal static string TestState()
        {
            var p = Player.Instance;
            if (p == null)
            {
                return "no player";
            }

            int chosen = 0;
            if (p.skills != null)
            {
                foreach (var sk in p.skills.skills)
                {
                    if (sk != null && sk.chosen)
                    {
                        chosen++;
                    }
                }
            }

            return $"level {p.currentLevel}, essence {p.experience} (next "
                + $"level at {Req(p, p.currentLevel)}), shared essence "
                + $"{SharedExp} level {SharedLevel}, " +
                   $"skills chosen {chosen}, points {SkillTree.Points(p)}, "
                + $"morning {InMorning(0)}, kept in a file {_saved != null}, " +
                   "reset today "
                + (_saved != null
                    && _saved.Resets.TryGetValue("host", out int d)
                    && d == Today);
        }
    }

    /// <summary>
    /// The stove's full bar gives the level and its point, not the game's skill
    /// choice (Progression.TakeLevelAtStove).
    /// </summary>
    [HarmonyPatch(typeof(SkillsMenu), nameof(SkillsMenu.showHideTiers))]
    static class StoveLevelPatch
    {
        static bool Prefix(SkillsMenu __instance, bool onlyShow)
        {
            if (StoveRules.RunsGame(onlyShow, __instance.opened))
            {
                return true;
            }

            Progression.TakeLevelAtStove();
            return false;
        }
    }

    [HarmonyPatch(typeof(InvItemClass), nameof(InvItemClass.consumeExp))]
    static class ConsumeExpPatch
    {
        static void Prefix(out int __state)
        {
            if (Player.Instance != null)
            {
                __state = Player.Instance.experience;
            }
            else
            {
                __state = 0;
            }
        }

        static void Postfix(int __state)
        {
            Player p = Player.Instance;
            int added = 0;
            if (p != null)
            {
                added = p.experience - __state;
            }

            if (ProgressRules.Cooked(p != null, Coop.Guards.Replaying, added))
            {
                Progression.Cooked(added);
            }
        }
    }

    [HarmonyPatch(typeof(SaveManager), nameof(SaveManager.Save))]
    static class ProgressSavedPatch
    {
        static void Postfix() => Progression.OnHostSaved();
    }
}
