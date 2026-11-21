using DarkwoodMP.World.Hideout;
using System;
using System.Collections.Generic;
using System.IO;
using DarkwoodMP.Combat;
using DarkwoodMP.Net;
using Newtonsoft.Json;
using UnityEngine;

namespace DarkwoodMP.World.Progress
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
    ///
    /// So one lives as long as the game (ModHost.Progress). A session adds
    /// its messages and forgets the record when it ends (Join).
    /// </summary>
    internal sealed partial class Progression
    {
        private readonly Session _net;

        public Progression(Session net)
        {
            _net = net;
        }

        private Session S => _net;

        /// <summary>Playing alone: no co-op session, in a world.</summary>
        private bool Solo => ProgressSideRules.Solo(Side());

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
        public int SharedExp { get; private set; }
        public int SharedLevel { get; private set; }
        private bool _known;       // client: the host has sent the state
        private Saved _saved;      // host
        private string _savedPath; // host: which world _saved is for
        private Seconds _nextSync;

        /// <summary>
        /// A session started: handle its messages for as long as it runs,
        /// and forget the client's state, the host's record and a waiting
        /// reset when it ends.
        /// </summary>
        public void Join(CoopSession coop)
        {
            coop.Handle(MsgType.ProgressState, OnState);
            coop.Handle(MsgType.EssenceAdd, OnEssence);
            coop.Handle(MsgType.LevelTaken, OnLevelTaken);
            coop.Handle(MsgType.SkillReset, OnSkillReset);
            coop.Hook(Forget);
        }

        private void Forget()
        {
            _known = false;
            _saved = null;
            _savedPath = null;
            _waitingReset = false;
        }

        // ---- levels ---------------------------------------------------------

        /// <summary>
        /// The shared numbers and this player's level requirements (none
        /// with no player): the only code that reads them for the rules.
        /// </summary>
        private ProgressFacts FactsOf(Player p)
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
        private SideFacts Side()
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

        private int Req(Player p, int level)
        {
            return ProgressRules.Requirement(FactsOf(p).Requirements, level);
        }

        /// <summary>
        /// The most levels a player can have now: the shared level, or what the
        /// shared essence allows.
        /// </summary>
        internal int MostLevels(Player p)
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
        private void Apply(Player p)
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


        // ---- client ---------------------------------------------------------

        /// <summary>
        /// Client: the shared essence and level the host sent.
        /// </summary>
        private void OnState(ulong from, PacketReader r)
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
        private void SendLevel(Player p)
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
        public void AfterCharacter(Player p)
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
        internal void Cooked(int amount)
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
        internal void LevelTaken()
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
        public void Update()
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
    }
}
