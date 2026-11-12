using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using DarkwoodMP.Combat;
using DarkwoodMP.Net;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Shared progression: every player has the same essence and the same number of levels to take, and each picks
    /// their own skills.
    ///
    /// Darkwood counts essence from the start of the game (Player.experience) against cumulative requirements
    /// (Player.levelRequirements[level]). Cooking essence at a stove (InvItemClass.consumeExp) fills the bar; a full
    /// bar gives a skill point, the level (SkillsMenu.showHideTiers raises Player.currentLevel) and the skill choice.
    ///
    /// Here the host keeps the shared essence and the shared level (the most levels anyone has taken). A client's
    /// cooking is a request (EssenceAdd) that the host adds to the shared essence, or refuses while the shared bar is
    /// full. Each player's Player.currentLevel is their own (it is in their character, with their skills), and their
    /// Player.experience is set to the shared essence, but no further than their own next level: so a player behind
    /// the shared level sees a full bar at the stove, and takes the level there. Everyone ends up with the same levels
    /// and points; the skills are each player's.
    ///
    /// The stove only gives the level and its skill point (TakeLevelAtStove, in place of the game's skill choice):
    /// the points are spent on the Skills panel, anywhere (World.SkillTree, SkillsPanel).
    ///
    /// A skill reset at the stove (a button while the stove's menu is open) takes back all of a player's chosen
    /// skills, good and negative, and gives back their points to spend anew; the levels stay. It can be done only in
    /// the morning, from dawn until the player first walks out of their hideout (Controller.isAfterNight, the same
    /// time AfterNight holds the clock for), and once a day (Controller.day). The host checks both for a client: its
    /// PlayerState says whether it is in its morning. A new player's character starts with no skills
    /// (World.Characters), and takes the shared levels at the stove.
    ///
    /// The host writes the shared essence, level and reset days beside its save (prof&lt;id&gt;/darkwoodmp_progress.json)
    /// when its game saves. It starts sharing once someone joins a world that has no such file yet, from the host's
    /// own essence and level.
    ///
    /// Playing alone (no session), the stove's reset works the same way, with the player as the only one: the first
    /// reset starts the file from the player's own essence and level, and from then on the game's essence is kept
    /// there, so the levels taken back can be taken again at the stove (one at a time, as the bar fills each time).
    /// </summary>
    public static class Progression
    {
        private static Session S => Session.Instance;

        /// <summary>Playing alone: no co-op session, in a world.</summary>
        private static bool Solo => (S == null || !S.Active) && WorldShare.InWorld && !NetEnemies.ClientMode;

        private sealed class Saved
        {
            public int Exp;
            public int Level;
            public Dictionary<string, int> Resets = new Dictionary<string, int>(); // hash of player key ("host": the host) -> day
        }

        /// <summary>The shared essence and level (host: its own; client: what the host last said).</summary>
        public static int SharedExp { get; private set; }
        public static int SharedLevel { get; private set; }
        private static bool _known;       // client: the host has sent the state
        private static Saved _saved;      // host
        private static string _savedPath; // host: which world _saved is for
        private static float _nextSync;

        public static void Register(Session s)
        {
            s.Register(MsgType.ProgressState, OnState);
            s.Register(MsgType.EssenceAdd, OnEssence);
            s.Register(MsgType.LevelTaken, OnLevelTaken);
            s.Register(MsgType.SkillReset, OnSkillReset);
            SkillTree.Register(s);
            s.OnSessionEnded += () => { _known = false; _saved = null; _savedPath = null; };
        }

        // ---- levels ---------------------------------------------------------------------------------

        private static int Req(Player p, int level)
        {
            var reqs = p.levelRequirements;
            if (reqs == null || reqs.Count == 0) return 0;
            return reqs[Mathf.Clamp(level, 0, reqs.Count - 1)];
        }

        /// <summary>The essence a player with this own level sees: the shared essence, up to their next level.</summary>
        private static int Display(Player p, int ownLevel) => Math.Min(SharedExp, Req(p, ownLevel));

        /// <summary>The most levels the shared essence allows: level L needs Req(L - 1).</summary>
        private static int Allowed(Player p)
        {
            int max = p.levelRequirements != null ? p.levelRequirements.Count : 0, l = 0;
            while (l < max && SharedExp >= Req(p, l)) l++;
            return l;
        }

        /// <summary>The most levels a player can have now: the shared level, or what the shared essence allows.</summary>
        internal static int MostLevels(Player p) => p == null ? SharedLevel : Math.Max(SharedLevel, Allowed(p));

        private static bool BarFull(Player p) => SharedLevel > 7 || SharedExp >= Req(p, SharedLevel);

        /// <summary>Set our player's essence to what the shared essence shows them, and refresh an open stove menu.</summary>
        private static void Apply(Player p)
        {
            if (p == null) return;
            int want = Display(p, p.currentLevel);
            if (p.experience == want) return;
            p.experience = want;
            var menu = Singleton<UI>.Instance != null ? Singleton<UI>.Instance.LevelingMenu : null;
            var skills = Singleton<SkillsMenu>.Instance;
            try
            {
                if (menu != null && p.inLevelingMenu && menu.openedAndTweened && menu.LevelingLiquid != null && (skills == null || !skills.opened))
                    menu.updateLiquid();
            }
            catch (Exception e) { Plugin.Log.LogWarning($"[progress] couldn't refresh the stove: {e.Message}"); }
        }

        // ---- host -----------------------------------------------------------------------------------

        private static string SavedPath()
        {
            var sm = Singleton<SaveManager>.Instance;
            if (sm == null || Core.currentProfile == null) return null;
            return Path.Combine(Path.Combine(sm.baseSaveDirectory, "prof" + Core.currentProfile.id), "darkwoodmp_progress.json");
        }

        /// <summary>Host: the shared progression of this world, loaded or started (when someone is playing with us); null: none.</summary>
        private static Saved HostSaved(bool start, bool alone = false)
        {
            var s = S;
            var p = Player.Instance;
            bool solo = Solo;
            if ((!solo && (s == null || !s.IsHost)) || !WorldShare.InWorld || WorldShare.Loading || p == null || NetEnemies.ClientMode) return null;
            string path = SavedPath();
            if (path == null) return null;
            if (path != _savedPath)
            {
                _saved = null;
                _savedPath = path;
                try { if (File.Exists(path)) _saved = JsonConvert.DeserializeObject<Saved>(File.ReadAllText(path)); }
                catch (Exception e) { Plugin.Log.LogWarning($"[progress] couldn't read {path}: {e.Message}"); }
                if (_saved != null)
                {
                    if (_saved.Resets == null) _saved.Resets = new Dictionary<string, int>();
                    SharedExp = _saved.Exp; SharedLevel = _saved.Level;
                    Plugin.Log.LogInfo($"[progress] this world's shared essence {SharedExp}, level {SharedLevel}");
                }
            }
            if (_saved == null && start && (solo ? alone : s.Peers.Count > 0))
            {
                _saved = new Saved { Exp = p.experience, Level = p.currentLevel };
                SharedExp = _saved.Exp; SharedLevel = _saved.Level;
                Plugin.Log.LogInfo($"[progress] shared progression starts from yours: essence {SharedExp}, level {SharedLevel}");
            }
            return _saved;
        }

        private static void SendState(ulong to)
        {
            var s = S;
            var w = s.Begin(MsgType.ProgressState);
            w.W.Write(SharedExp);
            w.W.Write((byte)Mathf.Clamp(SharedLevel, 0, 255));
            if (to == 0) s.Broadcast(w, Channel.Control, reliable: true);
            else s.SendTo(to, w, Channel.Control, reliable: true);
        }

        /// <summary>Host: someone's character was just sent; send them the shared progression after it.</summary>
        public static void HostSendTo(ulong peer)
        {
            if (HostSaved(start: true) != null) SendState(peer);
        }

        /// <summary>Host: take in our own player's essence and level, tell everyone when they changed, and show ours.</summary>
        private static void HostSync()
        {
            var saved = HostSaved(start: true);
            var p = Player.Instance;
            if (saved == null || p == null) return;
            int exp = Math.Max(SharedExp, p.experience), level = Math.Max(SharedLevel, p.currentLevel);
            if (exp != SharedExp || level != SharedLevel)
            {
                SharedExp = saved.Exp = exp;
                SharedLevel = saved.Level = level;
                if (WorldActions.Debug) Plugin.Log.LogInfo($"[progress] shared essence {SharedExp}, level {SharedLevel}");
                if (!Solo) SendState(0);
            }
            Apply(p);
        }

        /// <summary>Host: the game saved; write the shared progression beside it.</summary>
        public static void OnHostSaved()
        {
            if (WorldShare.Snapshotting || NetEnemies.ClientMode || _saved == null || _savedPath == null || _savedPath != SavedPath()) return;
            try { File.WriteAllText(_savedPath, JsonConvert.SerializeObject(_saved)); }
            catch (Exception e) { Plugin.Log.LogWarning($"[progress] couldn't write {_savedPath}: {e.Message}"); }
        }

        private static void OnEssence(ulong from, PacketReader r)
        {
            int amount = r.R.ReadInt32();
            var p = Player.Instance;
            if (!S.IsHost || !Authority.Handling || HostSaved(start: true) == null || p == null) return;
            if (amount <= 0 || amount > 100000) { SendState(from); Authority.Refuse($"implausible essence {amount}"); return; }
            if (BarFull(p)) { SendState(from); Authority.Refuse("the shared essence bar is full: someone has to take the level at a stove"); return; }
            SharedExp += amount;
            _saved.Exp = SharedExp;
            Plugin.Log.LogInfo($"[progress] {S.NameOf(from)} cooked {amount} essence: shared {SharedExp}");
            SendState(0);
            Apply(p);
        }

        private static void OnLevelTaken(ulong from, PacketReader r)
        {
            int level = r.R.ReadByte();
            var p = Player.Instance;
            if (!S.IsHost || HostSaved(start: true) == null || p == null) return;
            int allowed = Allowed(p);
            if (level > allowed) Plugin.Log.LogInfo($"[progress] {S.NameOf(from)} says level {level}; the shared essence allows {allowed}");
            level = Math.Min(level, allowed);
            if (level <= SharedLevel) return;
            SharedLevel = _saved.Level = level;
            Plugin.Log.LogInfo($"[progress] {S.NameOf(from)} took level {level}: everyone can take it at a stove");
            SendState(0);
        }

        private static string KeyHash(string key)
        {
            using (var sha = SHA256.Create())
                return WorldFiles.Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(key ?? ""))).Substring(0, 32);
        }

        private static int Today => Singleton<Controller>.Instance != null ? Singleton<Controller>.Instance.day : 0;

        public const string NotMorning = "Skills can only be reset in the morning, before you leave your hideout";

        /// <summary>Whether this player (0: ours) is in their morning: after dawn, not yet out of their hideout (Controller.isAfterNight).</summary>
        internal static bool InMorning(ulong peer)
        {
            if (peer == 0) return AfterNight.Local;
            var sync = Sync.PlayerSync.Instance;
            return sync != null && sync.Remotes.TryGetValue(peer, out var rp) && rp.Last != null && rp.Last.AfterNight;
        }

        /// <summary>Host: whether this player (key hash, or "host"; peer 0: ours) may reset now; if so, it counts as today's.</summary>
        private static bool TakeReset(string who, ulong peer, out string why)
        {
            why = null;
            if (!InMorning(peer)) { why = NotMorning; return false; }
            var saved = HostSaved(start: true, alone: who == "host");
            if (saved == null) { why = "nobody shares the progression"; return false; }
            if (saved.Resets.TryGetValue(who, out int day) && day == Today) { why = "you already reset your skills today"; return false; }
            saved.Resets[who] = Today;
            return true;
        }

        private static void OnSkillReset(ulong from, PacketReader r)
        {
            if (!S.IsHost || !Authority.Handling || !S.Keys.TryGetValue(from, out var key)) return;
            if (!TakeReset(KeyHash(key), from, out string why)) { Authority.Refuse(why); return; }
            Plugin.Log.LogInfo($"[progress] {S.NameOf(from)} reset their skills (day {Today})");
        }

        // ---- client ---------------------------------------------------------------------------------

        private static bool ClientActive => NetEnemies.ClientMode && WorldShare.InWorld && _known;

        private static void OnState(ulong from, PacketReader r)
        {
            if (S == null || from != S.HostId || !NetEnemies.ClientMode) return;
            SharedExp = r.R.ReadInt32();
            SharedLevel = r.R.ReadByte();
            bool first = !_known;
            _known = true;
            if (first) Plugin.Log.LogInfo($"[progress] shared essence {SharedExp}, level {SharedLevel}; yours {Player.Instance?.currentLevel}");
            Apply(Player.Instance);
        }

        /// <summary>Client: tell the host our own level (after a level, a reset, our character's arrival).</summary>
        private static void SendLevel(Player p)
        {
            if (!NetEnemies.ClientMode || p == null) return;
            var w = S.Begin(MsgType.LevelTaken);
            w.W.Write((byte)Mathf.Clamp(p.currentLevel, 0, 255));
            S.SendToHost(w, Channel.Control, reliable: true);
        }

        /// <summary>Client: our character came from the host (World.Characters).</summary>
        public static void AfterCharacter(Player p)
        {
            SendLevel(p);
            if (_known) Apply(p);
        }

        /// <summary>Either side: our player cooked essence (amount: what it added to Player.experience).</summary>
        internal static void Cooked(int amount)
        {
            var p = Player.Instance;
            if (amount <= 0 || p == null) return;
            if (ClientActive)
            {
                SharedExp += amount; // predicted; the host's ProgressState follows
                var w = S.Begin(MsgType.EssenceAdd);
                w.W.Write(amount);
                Authority.Request(w, $"cook {amount} essence");
            }
            else if ((S != null && S.IsHost) || Solo) HostSync();
        }

        /// <summary>Either side: our player took a level (SkillsMenu.confirmSkills).</summary>
        internal static void LevelTaken()
        {
            var p = Player.Instance;
            if (ClientActive) { SendLevel(p); Apply(p); }
            else if ((S != null && S.IsHost) || Solo) HostSync();
        }

        /// <summary>Call every frame: the host takes in what its own game did by itself (picked-up essence, a loaded save).</summary>
        public static void Update()
        {
            if (Time.unscaledTime < _nextSync) return;
            _nextSync = Time.unscaledTime + 1f;
            var s = S;
            // Back at the main menu: forget the world's file, so a reload (maybe of an older save) reads it again.
            if (!WorldShare.InWorld && (s == null || !s.Active) && _savedPath != null) { _saved = null; _savedPath = null; }
            if (s != null && s.IsHost && Hideouts.Together) HostSync();
            else if (Solo && _saved != null) HostSync();
            else if (ClientActive) Apply(Player.Instance);
        }

        // ---- the stove's level ----------------------------------------------------------------------

        /// <summary>
        /// The stove's full bar (LevelingMenu.checkIfWantToLevel -> SkillsMenu.showHideTiers): take the level and its
        /// skill point, without the game's skill choice (the point is spent on the Skills panel). The rest is what
        /// SkillsMenu.confirmSkills does after a choice: the bar starts again (and gives the next level if it's still
        /// full), and levels 2, 3, 5, 6 and 7 bring their dream (only the host's in co-op, World.DreamSync).
        /// </summary>
        internal static void TakeLevelAtStove()
        {
            var p = Player.Instance;
            var menu = Singleton<UI>.Instance != null ? Singleton<UI>.Instance.LevelingMenu : null;
            if (p == null || menu == null) return;
            p.currentLevel++;
            p.gainSaturation(5f);
            var d = Singleton<Dreams>.Instance;
            bool client = d != null && NetEnemies.ClientMode && Hideouts.Together, was = d != null && d.disabled;
            if (client) d.disabled = true; // a client's level brings no dream of its own
            try
            {
                p.gainExp(0);
                menu.resetLiquid();
                try { AudioController.Play("UI_placeItem"); } catch { }
                if (d != null && !d.disabled)
                {
                    int l = p.currentLevel;
                    if (l == 2 && !d.hadDreamAtLvl2) { d.wantToDream = true; d.hadDreamAtLvl2 = true; d.startTransition.dreamToTransitionTo = "dream_bunker_underground_01"; }
                    if (l == 3 && !d.hadDreamAtLvl3) { d.wantToDream = true; d.hadDreamAtLvl3 = true; }
                    if (l == 5 && !d.hadDreamAtLvl5) { d.wantToDream = true; d.hadDreamAtLvl5 = true; }
                    if (l == 6 && !d.hadDreamAtLvl6) { d.wantToDream = true; d.hadDreamAtLvl6 = true; }
                    if (l == 7 && !d.hadDreamAtLvl7) { d.wantToDream = true; d.hadDreamAtLvl7 = true; }
                }
                if (d != null && d.wantToDream && (!Core.randomGeneration || d.hasUnspawnedRandomPresetForChapter(Singleton<WorldGenerator>.Instance.chapterID)))
                {
                    Core.forbidInputs = true;
                    if (Singleton<CamMain>.Instance != null) Singleton<CamMain>.Instance.gameObject.SetActive(true);
                    Singleton<UI>.Instance.InventorySelectionPrompt.Hide();
                    d.startTransition.transition();
                    menu.hide(forceHide: true);
                }
                else if (d != null) d.wantToDream = false;
            }
            finally { if (client) d.disabled = was; }
            DreamSync.AfterConfirm(d);
            LevelTaken();
            Plugin.Log.LogInfo($"[progress] took level {p.currentLevel} at the stove: {SkillTree.Points(p)} skill points to spend");
            p.displayMessage($"A skill point to spend: press {SkillsPanel.KeyName} for the Skills");
        }

        // ---- skill reset ----------------------------------------------------------------------------

        /// <summary>
        /// Take back all of a player's chosen skills (good and negative). keepLevel: the levels stay, and the points
        /// come back to spend again (a reset); otherwise Player.currentLevel goes to 0 (a new character, which takes
        /// the shared levels at the stove).
        /// </summary>
        public static void ClearSkills(Player p, bool keepLevel = false)
        {
            var ps = p.skills;
            if (ps == null) return;
            var all = new HashSet<PlayerSkill>();
            foreach (var sk in ps.skills) if (sk != null) all.Add(sk);
            foreach (var sk in ps.progressionSkills) if (sk != null) all.Add(sk);
            var menu = Singleton<SkillsMenu>.Instance;
            var tiers = SkillTree.Tiers;
            if (tiers != null)
                foreach (var t in tiers)
                {
                    foreach (var sk in t.skills) if (sk != null) all.Add(sk);
                    foreach (var sk in t.negativeSkills) if (sk != null) all.Add(sk);
                }
            int n = 0;
            foreach (var sk in all)
                if (sk.chosen) { sk.initialize(destBool: false); n++; }
            ps.skills.RemoveAll(sk => sk == null || all.Contains(sk));
            if (menu != null && menu.chosenSkills != null) menu.chosenSkills.Clear();
            ps.SkillPoints = 0;
            ps.refreshAll();
            if (!keepLevel) p.currentLevel = 0;
            Plugin.Log.LogInfo($"[progress] skills reset: {n} taken back, level {p.currentLevel}");
        }

        private static bool CanResetHere(Player p)
        {
            var menu = Singleton<UI>.Instance != null ? Singleton<UI>.Instance.LevelingMenu : null;
            var skills = Singleton<SkillsMenu>.Instance;
            return p != null && p.inLevelingMenu && menu != null && menu.openedAndTweened && !menu.wantToLevel && (skills == null || !skills.opened);
        }

        private static bool _waitingReset;

        private static void ResetHere()
        {
            var p = Player.Instance;
            if (!CanResetHere(p)) return;
            if (NetEnemies.ClientMode)
            {
                if (_waitingReset) return;
                _waitingReset = true;
                Authority.Request(S.Begin(MsgType.SkillReset), "reset skills", (ok, reason, _) =>
                {
                    _waitingReset = false;
                    if (ok) DoReset(Player.Instance);
                    else { Player.Instance?.displayMessage(reason); Plugin.Log.LogInfo($"[progress] reset refused: {reason}"); }
                }, inventory: false);
                return;
            }
            if (!TakeReset("host", 0, out string why)) { p.displayMessage(why); Plugin.Log.LogInfo($"[progress] reset refused: {why}"); return; }
            DoReset(p);
        }

        private static void DoReset(Player p)
        {
            if (p == null) return;
            ClearSkills(p, keepLevel: true);
            if (NetEnemies.ClientMode) { SendLevel(p); Apply(p); }
            else HostSync();
            p.displayMessage($"Skills reset: {SkillTree.Points(p)} skill points to spend ({SkillsPanel.KeyName})");
        }

        private static GUIStyle _style;

        /// <summary>At our stove: the levels waiting, the points to spend, and the reset button in the morning.</summary>
        public static void OnGUI()
        {
            var p = Player.Instance;
            if (!WorldShare.InWorld || !CanResetHere(p)) return;
            if (_style == null) _style = new GUIStyle(GUI.skin.button) { fontSize = 16 };
            int waiting = (Hideouts.Together || Solo) ? Math.Max(0, SharedLevel - p.currentLevel) : 0;
            int points = SkillTree.Points(p);
            float w = 360f, h = 34f, x = (Screen.width - w) / 2f, y = Screen.height - 3f * h;
            if (waiting > 0) GUI.Label(new Rect(x, y - 2 * h, w, h), $"Levels to take: {waiting}", _style);
            if (points > 0) GUI.Label(new Rect(x, y - h, w, h), $"Skill points to spend: {points} ({SkillsPanel.KeyName}: Skills)", _style);
            if (InMorning(0) && GUI.Button(new Rect(x, y, w, h), _waitingReset ? "Asking the host..." : "Reset skills (mornings, once a day)", _style)) ResetHere();
        }

        /// <summary>For tests: reset our skills at the stove as the button does.</summary>
        internal static void TestReset() => ResetHere();
        internal static bool TestCanReset => CanResetHere(Player.Instance);
        internal static bool TestWaitingReset => _waitingReset;

        /// <summary>For tests: give our player this many levels and the essence they need (and a skill chosen for each).</summary>
        internal static void TestGiveLevels(int n, bool chooseSkills = true)
        {
            var p = Player.Instance;
            var tiers = SkillTree.Tiers;
            if (p == null || tiers == null) return;
            int given = 0;
            if (chooseSkills)
                foreach (var t in tiers)
                    foreach (var sk in t.skills)
                        if (given < n && sk != null && !sk.chosen) { sk.initialize(destBool: true); given++; }
            p.skills.refreshAll();
            p.currentLevel += n;
            p.experience = Req(p, p.currentLevel - 1);
        }

        /// <summary>For tests: our level, essence, the shared numbers and how many skills are chosen.</summary>
        internal static string TestState()
        {
            var p = Player.Instance;
            if (p == null) return "no player";
            int chosen = 0;
            if (p.skills != null) foreach (var sk in p.skills.skills) if (sk != null && sk.chosen) chosen++;
            return $"level {p.currentLevel}, essence {p.experience} (next level at {Req(p, p.currentLevel)}), shared essence {SharedExp} level {SharedLevel}, " +
                   $"skills chosen {chosen}, points {SkillTree.Points(p)}, morning {InMorning(0)}, kept in a file {_saved != null}, " +
                   $"reset today {(_saved != null && _saved.Resets.TryGetValue("host", out int d) && d == Today)}";
        }
    }

    /// <summary>The stove's full bar gives the level and its point, not the game's skill choice (Progression.TakeLevelAtStove).</summary>
    [HarmonyPatch(typeof(SkillsMenu), nameof(SkillsMenu.showHideTiers))]
    static class StoveLevelPatch
    {
        static bool Prefix(SkillsMenu __instance, bool onlyShow)
        {
            if (onlyShow || __instance.opened) return true;
            Progression.TakeLevelAtStove();
            return false;
        }
    }

    [HarmonyPatch(typeof(InvItemClass), nameof(InvItemClass.consumeExp))]
    static class ConsumeExpPatch
    {
        static void Prefix(out int __state) => __state = Player.Instance != null ? Player.Instance.experience : 0;
        static void Postfix(int __state)
        {
            if (Player.Instance == null || WorldActions.Replaying) return;
            int added = Player.Instance.experience - __state;
            if (added > 0) Progression.Cooked(added);
        }
    }

    [HarmonyPatch(typeof(SaveManager), nameof(SaveManager.Save))]
    static class ProgressSavedPatch
    {
        static void Postfix() => Progression.OnHostSaved();
    }
}
