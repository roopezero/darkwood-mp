using System;
using System.Collections.Generic;
using DarkwoodMP.Combat;
using HarmonyLib;
using UnityEngine;
using RW = Rewired;

namespace DarkwoodMP.World
{
    /// <summary>
    /// The Skills panel: a key (the SkillsKey setting, K by default; none of the game's default keys use K) opens it
    /// anywhere in the world, on the right. It doesn't open the backpack; the player can open that beside the panel
    /// with its own key. It shows the skill points to spend and the game's four tiers of skills with their own icons,
    /// names and descriptions, and takes a good skill (and a negative one when the game's rules want one) for a point
    /// (World.SkillTree). Each skill's whole cell in the grid takes the mouse, so there is no dead space between the
    /// icons, and the details under the grid have a fixed height (room for the longest description), so the window doesn't
    /// change size, or get a scrollbar that narrows everything, while the mouse moves over it. Learned skills have a bright frame and a check mark. Clicks on the panel don't reach the world:
    /// while the mouse is over it, the game's mouse actions read as not pressed (SkillsPanelMouse). Escape closes the
    /// panel before the game's pause menu sees it (SkillsPanelEscape).
    /// </summary>
    public static class SkillsPanel
    {
        private static UiWindow _win;
        private static string _good, _negative;  // chosen on the panel, not learned yet
        private static string _hover;
        private static string _message;          // why the last Learn didn't go
        private static readonly Dictionary<string, Rect[]> _rects = new Dictionary<string, Rect[]>(); // last drawn: icon, cell (screen, top left)
        private static float _detailW = -1f;     // the width the details' height was measured for
        private static float _detailH;

        public static bool Open => _win != null && _win.Open;

        public static string KeyName
        {
            get
            {
                var k = Plugin.Cfg != null ? Plugin.Cfg.SkillsKey.Value : KeyCode.K;
                return k.ToString();
            }
        }

        /// <summary>The panel's title: the game's own word for them.</summary>
        private static string Title => "Skills";

        private static UiWindow Win
        {
            get
            {
                if (_win != null) return _win;
                _win = new UiWindow(0x5d17, Title)
                {
                    MinSize = new Vector2(300, 260),
                    FitContent = true,
                    DefaultRect = () =>
                    {
                        // On the right, clear of the backpack (on the left) when the player opens it too.
                        return UiWindow.RightSide(360 * UiWindow.S, 420 * UiWindow.S);
                    },
                    Body = Draw,
                    Footer = DrawFooter,
                    FooterHeight = 26,
                    OnClose = Closed,
                };
                return _win;
            }
        }

        private static bool CanOpen(Player p, out string why)
        {
            why = null;
            if (p == null || !WorldShare.InWorld || WorldShare.Loading || Core.mainMenu || Core.loadingGame) { why = "not in a world"; return false; }
            if (Core.Paused || Core.forbidInputs || p.inMenu() || p.dying || p.health <= 0f) { why = "busy"; return false; }
            if (Spectator.Active) { why = "watching"; return false; }
            if (SkillTree.Tiers == null) { why = "no skills"; return false; }
            return true;
        }

        /// <summary>Open it (with the backpack), or close it.</summary>
        public static void Toggle()
        {
            if (Open) { Win.Close(); return; }
            var p = Player.Instance;
            if (!CanOpen(p, out string why)) { Plugin.Log.LogInfo($"[skills] the panel can't open now ({why})"); return; }
            _good = _negative = _message = null;
            _rects.Clear();
            Win.ResetPlace();
            Win.Show();
            Plugin.Log.LogInfo($"[skills] panel open: level {p.currentLevel}, {SkillTree.Points(p)} points to spend, backpack open {p.Inventory != null && p.Inventory.open}");
        }

        private static void Closed() => Plugin.Log.LogInfo("[skills] panel closed");

        internal static void CloseFromEscape()
        {
            Plugin.Log.LogInfo("[skills] Escape: the panel closes");
            Win.Close();
        }

        /// <summary>Every frame: the key, and closing when a game menu opens, the game pauses or the world goes.</summary>
        public static void Update(bool coopPanelOpen)
        {
            var p = Player.Instance;
            if (Open && (!WorldShare.InWorld || Core.mainMenu || p == null || p.inMenu() || Core.Paused || p.dying))
            {
                _win.Open = false;
                Plugin.Log.LogInfo("[skills] panel closed (a game menu, the pause menu, or the world went)");
            }
            // Escape is taken in SkillsPanelEscape, before the game's pause menu sees it.
            if (coopPanelOpen || GUIUtility.keyboardControl != 0) return;
            var key = Plugin.Cfg != null ? Plugin.Cfg.SkillsKey.Value : KeyCode.K;
            if (key != KeyCode.None && Input.GetKeyDown(key))
            {
                Toggle();
            }
        }

        public static void OnGUI()
        {
            if (!Open) return;
            MenuSkin.Ensure();
            var skin = GUI.skin;
            GUI.skin = MenuSkin.Skin;
            Win.Title = Title;
            Win.OnGUI();
            GUI.skin = skin;
        }

        // ---- drawing --------------------------------------------------------------------------------

        private static float Icon => 40 * UiWindow.S;

        private static void Draw(Flow f)
        {
            var p = Player.Instance;
            var tiers = SkillTree.Tiers;
            if (p == null || tiers == null) { Ui.Text(f, "No skills here."); return; }
            var chosen = SkillTree.Chosen(p);
            int level = p.currentLevel, points = SkillTree.Points(level, chosen);
            bool needNeg = SkillTree.NeedsNegative(level, chosen);
            if (Event.current.type == EventType.Layout) _hover = null;

            var top = f.Row(PixelFont.Line + 4 * UiWindow.S, 0, 0);
            Ui.Label(top[0], $"Skill points: {points}", points > 0 ? MenuSkin.Bright : MenuSkin.Text);
            Ui.Label(top[1], $"Level {level}", MenuSkin.Dim, TextAnchor.MiddleRight);
            f.Space(4);

            // The four tiers side by side, as the game's skill choice shows them: good skills, then the negative ones.
            int n = tiers.Count, rows = 0, negRows = 0;
            foreach (var t in tiers) { rows = Math.Max(rows, t.skills.Count); negRows = Math.Max(negRows, t.negativeSkills.Count); }
            float colW = f.Width / Math.Max(1, n), icon = Mathf.Min(Icon, colW - 8 * UiWindow.S), gap = 6 * UiWindow.S;
            var head = f.Next(PixelFont.Line * 2 + 2 * UiWindow.S);
            for (int i = 0; i < n; i++)
            {
                var t = tiers[i];
                bool open = t.level <= level;
                var c = new Rect(head.x + i * colW, head.y, colW, PixelFont.Line);
                PixelFont.Draw(c, SkillTree.TierName(i), open ? MenuSkin.Bright : MenuSkin.Dim, TextAnchor.MiddleCenter, PixelFont.Kind.Title);
                Ui.Label(new Rect(c.x, c.yMax + 2 * UiWindow.S, colW, PixelFont.Line), open ? "open" : $"level {t.level}", MenuSkin.Dim, TextAnchor.MiddleCenter);
            }
            f.Space(2);
            var grid = f.Next(rows * (icon + gap));
            for (int i = 0; i < n; i++)
                for (int k = 0; k < tiers[i].skills.Count; k++)
                {
                    var sk = tiers[i].skills[k];
                    if (sk == null) continue;
                    var cell = new Rect(grid.x + i * colW, grid.y + k * (icon + gap), colW, icon + gap);
                    bool can = SkillTree.CantTake(level, chosen, sk.name) == null;
                    DrawSkill(cell, icon, sk, chosen.Contains(sk.name), can, sk.name == _good, false);
                }
            if (negRows > 0)
            {
                var lr = f.Next(PixelFont.Line + 4 * UiWindow.S);
                GUI.Box(new Rect(lr.x, lr.y + lr.height / 2f, lr.width, UiWindow.S), GUIContent.none, MenuSkin.Line);
                float lw = PixelFont.Width("Negative") + 12 * UiWindow.S;
                GUI.Box(new Rect(lr.x + (lr.width - lw) / 2f, lr.y, lw, lr.height), GUIContent.none, MenuSkin.Plain);
                Ui.Label(lr, "Negative", MenuSkin.Warn, TextAnchor.MiddleCenter);
                var ngrid = f.Next(negRows * (icon + gap));
                for (int i = 0; i < n; i++)
                    for (int k = 0; k < tiers[i].negativeSkills.Count; k++)
                    {
                        var sk = tiers[i].negativeSkills[k];
                        if (sk == null) continue;
                        var cell = new Rect(ngrid.x + i * colW, ngrid.y + k * (icon + gap), colW, icon + gap);
                        bool can = needNeg && SkillTree.CantTakeNegative(level, chosen, sk.name) == null;
                        DrawSkill(cell, icon, sk, chosen.Contains(sk.name), can, sk.name == _negative, true);
                    }
            }

            // What the skill under the mouse (or the one chosen) does.
            f.Space(2);
            GUI.Box(f.Next(UiWindow.S), GUIContent.none, MenuSkin.Line);
            f.Space(2);
            // Room for the longest description, so the window keeps its size whichever skill is shown.
            float detailTop = f.Y, detailH = DetailHeight(tiers, f.Width);
            string show = _hover ?? _good ?? _negative;
            if (show != null)
            {
                SkillTree.TierOf(show, out bool neg);
                PixelFont.Draw(f.Next(PixelFont.Line + 2 * UiWindow.S), SkillName(show), neg ? MenuSkin.Warn : MenuSkin.Bright, TextAnchor.MiddleLeft, PixelFont.Kind.Title);
                string state = chosen.Contains(show) ? "Learned"
                    : neg ? (needNeg ? SkillTree.CantTakeNegative(level, chosen, show) ?? "Taken with a good skill" : "Not needed now")
                    : SkillTree.CantTake(level, chosen, show) ?? (needNeg ? "Comes with a negative skill" : "Can be learned");
                Ui.Label(f, Capital(state), MenuSkin.Dim);
                Ui.Text(f, SkillDesc(show));
            }
            else Ui.Text(f, points > 0 ? "Choose a skill to learn." : "Take a level at a stove for a skill point.", MenuSkin.Dim);
            // Never shorter than what was drawn, so a description that takes more room than measured can't make the body grow while hovering.
            if (f.Y - detailTop > _detailH) _detailH = f.Y - detailTop;
            f.Y = Mathf.Max(f.Y, detailTop + _detailH);
        }

        /// <summary>The details' height for the skill with the longest description, at this width.</summary>
        private static float DetailHeight(List<SkillTiers.Tier> tiers, float width)
        {
            if (Mathf.Abs(width - _detailW) < 0.5f) return _detailH;
            int lines = 2;
            foreach (var t in tiers)
                foreach (var list in new[] { t.skills, t.negativeSkills })
                    foreach (var sk in list)
                        if (sk != null) lines = Math.Max(lines, PixelFont.Wrap(SkillDesc(sk.name), width).Count);
            float S = UiWindow.S, gap = 4 * S;   // Flow.Gap
            _detailW = width;
            // As Draw lays them out: the name (Line + 2S and a gap), the state (Line and a gap), the text (Ui.Text).
            _detailH = (PixelFont.Line + 2 * S + gap) + (PixelFont.Line + gap) + lines * (PixelFont.Line + S) + gap - S;
            return _detailH;
        }

        private static void DrawFooter(Rect r)
        {
            var p = Player.Instance;
            if (p == null) return;
            var chosen = SkillTree.Chosen(p);
            int level = p.currentLevel;
            bool needNeg = SkillTree.NeedsNegative(level, chosen);
            var c = Ui.Split(r, 0, 90);
            string status = _message;
            if (status == null)
            {
                if (SkillTree.Waiting) status = "Asking the host...";
                else if (_good == null) status = SkillTree.Points(level, chosen) > 0 ? "" : "No skill points to spend";
                else if (needNeg && _negative == null) status = "Choose a negative skill to go with it";
                else status = "";
            }
            Ui.Label(c[0], PixelFont.Fit(status, c[0].width), _message != null ? MenuSkin.Warn : MenuSkin.Dim);
            bool ready = _good != null && !SkillTree.Waiting && SkillTree.Check(level, chosen, _good, needNeg ? _negative : null) == null;
            if (Ui.Button(c[1], "Learn", ready)) LearnChosen();
        }

        private static void LearnChosen()
        {
            var p = Player.Instance;
            bool needNeg = p != null && SkillTree.NeedsNegative(p.currentLevel, SkillTree.Chosen(p));
            if (SkillTree.Learn(_good, needNeg ? _negative : null, out string why)) { _good = _negative = _message = null; }
            else _message = Capital(why);
        }

        // The frames' colours: a learned skill's (bright, red for a negative one), the one chosen to learn, the mouse's.
        private static readonly Color LearnedFrame = new Color(0.86f, 0.80f, 0.62f), LearnedNegFrame = new Color(0.80f, 0.32f, 0.26f),
            ChosenFrame = new Color(0.62f, 0.62f, 0.62f), HoverFrame = new Color(0.40f, 0.40f, 0.40f);

        /// <summary>
        /// One skill: the icon centred in its cell, and the whole cell takes the mouse (the hover, the details and
        /// the click), so the space between the icons isn't dead. A learned skill is drawn in full colour in a bright
        /// frame with a check mark in its corner; one chosen to learn has a grey frame; the others are dimmed.
        /// </summary>
        private static void DrawSkill(Rect cell, float icon, PlayerSkill sk, bool have, bool can, bool picked, bool negative)
        {
            float S = UiWindow.S;
            var r = new Rect(cell.x + (cell.width - icon) / 2f, cell.y + (cell.height - icon) / 2f, icon, icon);
            var mouse = TestMouse.HasValue ? GUIUtility.ScreenToGUIPoint(TestMouse.Value) : Event.current.mousePosition;
            bool hover = cell.Contains(mouse);
            if (hover) _hover = sk.name;
            var frame = new Rect(r.x - 3 * S, r.y - 3 * S, r.width + 6 * S, r.height + 6 * S);
            GUI.Box(frame, GUIContent.none, MenuSkin.Sunken);
            Color tint = have ? Color.white : can ? (hover ? new Color(0.85f, 0.85f, 0.85f) : new Color(0.55f, 0.55f, 0.55f)) : new Color(0.25f, 0.25f, 0.25f);
            if (negative) tint = new Color(1f, 0.4f, 0.4f) * (have ? 1f : can ? (hover ? 0.9f : 0.65f) : 0.3f);
            tint.a = 1f;
            if (!SkillIcons.Draw(r, sk.name, tint))
                PixelFont.Draw(r, PixelFont.Fit(SkillName(sk.name), r.width), tint, TextAnchor.MiddleCenter);
            if (Event.current.type == EventType.Repaint)
            {
                if (have) Outline(frame, 2 * S, negative ? LearnedNegFrame : LearnedFrame);
                else if (picked) Outline(frame, 2 * S, ChosenFrame);
                else if (hover && can) Outline(frame, S, HoverFrame);
                if (have) CheckMark(new Vector2(frame.xMax, frame.y), S, negative ? LearnedNegFrame : LearnedFrame);
                _rects[sk.name] = new[] { ToScreen(r), ToScreen(cell) };
            }
            if (GUI.Button(cell, GUIContent.none, GUIStyle.none) && can && !have)
            {
                _message = null;
                if (negative) _negative = _negative == sk.name ? null : sk.name;
                else _good = _good == sk.name ? null : sk.name;
                try { AudioController.Play("UI_click"); } catch { }
            }
        }

        private static Rect ToScreen(Rect r)
        {
            var a = GUIUtility.GUIToScreenPoint(r.min);
            return new Rect(a, r.size);
        }

        private static void Fill(Rect r, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }

        private static void Outline(Rect r, float t, Color c)
        {
            Fill(new Rect(r.x, r.y, r.width, t), c);
            Fill(new Rect(r.x, r.yMax - t, r.width, t), c);
            Fill(new Rect(r.x, r.y, t, r.height), c);
            Fill(new Rect(r.xMax - t, r.y, t, r.height), c);
        }

        /// <summary>A small pixel check mark on a dark square, its top right corner at this point.</summary>
        private static void CheckMark(Vector2 topRight, float S, Color c)
        {
            float u = Mathf.Max(1f, Mathf.Round(S * 1.5f)), w = 11 * u, h = 9 * u;
            var box = new Rect(topRight.x - w, topRight.y, w, h);
            Fill(box, new Color(0.06f, 0.06f, 0.06f, 1f));
            Outline(box, Mathf.Max(1f, S), c);
            // The tick: down from the left to the bottom, then up to the right, two pixels thick.
            int[] ys = { 4, 5, 6, 5, 4, 3, 2 };
            for (int i = 0; i < ys.Length; i++)
                Fill(new Rect(box.x + (2 + i) * u, box.y + (ys[i] - 1) * u, u, 2 * u), c);
        }

        private static string Capital(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        private static readonly Dictionary<string, string> _names = new Dictionary<string, string>(), _descs = new Dictionary<string, string>();

        public static string SkillName(string name) => Lang(_names, name + "_name", name);
        public static string SkillDesc(string name) => Lang(_descs, name + "_desc", "");

        /// <summary>The game's text (sheet Skills), without tk2d's colour codes.</summary>
        private static string Lang(Dictionary<string, string> cache, string key, string fallback)
        {
            if (cache.TryGetValue(key, out var s)) return s;
            try { s = global::Language.HasKey(key, "Skills") ? global::Language.Get(key, "Skills") : fallback; }
            catch { s = fallback; }
            s = Clean(s ?? fallback);
            cache[key] = s;
            return s;
        }

        private static string Clean(string s)
        {
            var sb = new System.Text.StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                // tk2d inline colours: ^cRGBA (hex) or ^CRRGGBBAA.
                if (s[i] == '^' && i + 1 < s.Length && (s[i + 1] == 'c' || s[i + 1] == 'C'))
                {
                    i += 1 + (s[i + 1] == 'c' ? 4 : 8);
                    continue;
                }
                sb.Append(s[i] == '\r' ? ' ' : s[i]);
            }
            return sb.ToString().Trim();
        }

        // ---- tests ----------------------------------------------------------------------------------

        /// <summary>For tests: the key pressed (as Update sees it).</summary>
        internal static void TestPressKey() => Toggle();

        /// <summary>For tests: choose and learn on the panel, as the clicks do; the result (null: learned).</summary>
        internal static string TestLearn(string good, string negative)
        {
            _good = good; _negative = negative; _message = null;
            LearnChosen();
            return _message;
        }

        /// <summary>For tests: show this skill's details as if the mouse were over it.</summary>
        internal static void TestPick(string good, string negative) { _good = good; _negative = negative; }

        /// <summary>For tests: the mouse as the panel sees it (screen pixels from the top left); null: the real mouse.</summary>
        internal static Vector2? TestMouse;

        /// <summary>For tests: the skill the mouse is over (as the last frame drew it).</summary>
        internal static string TestHover => _hover;

        /// <summary>For tests: the good skill chosen on the panel (by a click), not learned yet.</summary>
        internal static string TestChosen => _good;

        /// <summary>For tests: a skill's icon and its cell (the hover and click area) as last drawn, in screen pixels from the top left.</summary>
        internal static bool TestRects(string skill, out Rect icon, out Rect cell)
        {
            icon = cell = default;
            if (!_rects.TryGetValue(skill, out var rs)) return false;
            icon = rs[0]; cell = rs[1];
            return true;
        }

        /// <summary>
        /// For tests: the test mouse goes over every skill's cell in turn, three frames each, and every frame the window's
        /// size, whether its body scrolls, its content's height and the first skill's cell must stay as they were with
        /// the mouse over nothing. Returns the number of frames where something moved (logged as FAIL lines).
        /// </summary>
        internal static System.Collections.IEnumerator TestHoverSweep(Action<int> done)
        {
            TestMouse = new Vector2(-100, -100);
            yield return null; yield return null;
            var skills = new List<string>(_rects.Keys);
            string first = skills.Count > 0 ? skills[0] : null;
            Rect firstCell = first != null ? _rects[first][1] : default;
            Rect win = Win.Rect; bool scrolls = Win.TestScrolls; float content = Win.TestContentHeight;
            Plugin.Log.LogInfo($"[skills] hover sweep over {skills.Count} skills: window {win.width}x{win.height}, scrolls {scrolls}, content {content}");
            int bad = 0, frames = 0;
            foreach (var sk in skills)
            {
                var cell = _rects[sk][1];
                TestMouse = cell.center;
                for (int i = 0; i < 3; i++)
                {
                    yield return null;
                    frames++;
                    var fc = first != null ? _rects[first][1] : default;
                    if (Win.Rect.size != win.size || Win.TestScrolls != scrolls || Mathf.Abs(Win.TestContentHeight - content) > 0.5f || fc != firstCell)
                    {
                        bad++;
                        Plugin.Log.LogInfo($"[skills] FAIL hovering {sk} ({_hover ?? "nothing"}): window {Win.Rect.width}x{Win.Rect.height}, scrolls {Win.TestScrolls}, content {Win.TestContentHeight}, first cell {fc} (was {firstCell})");
                    }
                }
            }
            TestMouse = null;
            Plugin.Log.LogInfo($"[skills] hover sweep: {frames} frames, {(bad == 0 ? "PASS nothing moved" : $"FAIL {bad} frames moved")}");
            done?.Invoke(bad);
        }

        /// <summary>For tests: the panel's window, in screen pixels from the top left.</summary>
        internal static Rect TestWindow => Win.Rect;

        // ---- the game's input while the panel is open ------------------------------------------------

        private static readonly HashSet<string> MouseActions = new HashSet<string> { "Primary Action", "Aim", "Secondary Attack", "Attack" };
        private static readonly bool[] _pressedOnPanel = new bool[3];
        private static int _mouseFrame = -1;
        private static bool _overPanel;
        /// <summary>For tests: reads of the game's mouse actions answered "not pressed" because of the panel.</summary>
        internal static int TestEaten;

        /// <summary>The mouse over the panel's window, and the mouse buttons pressed there and still held (once a frame).</summary>
        private static void MouseState()
        {
            if (_mouseFrame == Time.frameCount) return;
            _mouseFrame = Time.frameCount;
            var m = Input.mousePosition;
            _overPanel = Win.Rect.Contains(new Vector2(m.x, Screen.height - m.y));
            for (int b = 0; b < 3; b++)
            {
                if (Input.GetMouseButtonDown(b) && _overPanel) _pressedOnPanel[b] = true;
                else if (!Input.GetMouseButton(b)) _pressedOnPanel[b] = false;
            }
        }

        /// <summary>
        /// Whether the game's read of this action should be "not pressed": a mouse action pressed over the panel, or
        /// held after a press that started there. A press that started in the world (holding aim) goes on as it was.
        /// </summary>
        internal static bool EatsAction(string action, bool down)
        {
            if (!Open || action == null || !MouseActions.Contains(action)) return false;
            MouseState();
            bool held = _pressedOnPanel[0] || _pressedOnPanel[1] || _pressedOnPanel[2];
            bool eat = held || (down && _overPanel);
            if (eat) TestEaten++;
            return eat;
        }
    }

    /// <summary>The game's mouse actions read as not pressed while they are pressed on the Skills panel (SkillsPanel.EatsAction).</summary>
    [HarmonyPatch(typeof(RW.Player), nameof(RW.Player.GetButtonDown), new[] { typeof(string) })]
    internal static class SkillsPanelMouse
    {
        private static bool Prefix(string actionName, ref bool __result)
        {
            if (!SkillsPanel.EatsAction(actionName, true)) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(RW.Player), nameof(RW.Player.GetButton), new[] { typeof(string) })]
    internal static class SkillsPanelMouseHeld
    {
        private static bool Prefix(string actionName, ref bool __result)
        {
            if (!SkillsPanel.EatsAction(actionName, false)) return true;
            __result = false;
            return false;
        }
    }

    /// <summary>Escape with the Skills panel open closes the panel, not the backpack or into the pause menu.</summary>
    [HarmonyPatch(typeof(InputScript), nameof(InputScript.onPressEsc))]
    internal static class SkillsPanelEscape
    {
        private static bool Prefix()
        {
            if (!SkillsPanel.Open || Core.forbidInputs) return true; // a film to skip goes first
            SkillsPanel.CloseFromEscape();
            return false;
        }
    }

    /// <summary>The game's skill icons (tk2d sprites "skill_NAME" in the UISkill prefab's collection), drawn in IMGUI.</summary>
    internal static class SkillIcons
    {
        private static tk2dSpriteCollectionData _coll;
        private static bool _tried;
        private static readonly Dictionary<string, int> _ids = new Dictionary<string, int>();

        public static bool Draw(Rect r, string skill, Color tint)
        {
            if (!_tried)
            {
                _tried = true;
                try
                {
                    var go = Resources.Load("Prefabs/UI/UISkill") as GameObject;
                    var spr = go != null ? go.GetComponent<tk2dSprite>() : null;
                    _coll = spr != null && spr.Collection != null ? spr.Collection.inst : null;
                    if (_coll == null) Plugin.Log.LogWarning("[skills] no skill icons (UISkill's sprites): names only");
                }
                catch (Exception e) { Plugin.Log.LogWarning($"[skills] no skill icons: {e.Message}"); }
            }
            if (_coll == null) return false;
            if (!_ids.TryGetValue(skill, out int id))
            {
                id = _coll.GetSpriteIdByName("skill_" + skill, -1);
                _ids[skill] = id;
                if (id < 0) Plugin.Log.LogInfo($"[skills] no icon for {skill}");
                else
                {
                    var d0 = _coll.spriteDefinitions[id];
                    Plugin.Log.LogInfo($"[skills] icon for {skill}: flipped {d0.flipped}, uvs {string.Join(" ", Array.ConvertAll(d0.uvs, u => $"({u.x:0.###},{u.y:0.###})"))}");
                }
            }
            if (id < 0 || Event.current.type != EventType.Repaint) return id >= 0;
            var def = _coll.spriteDefinitions[id];
            var mat = def.materialInst != null ? def.materialInst : def.material;
            var tex = mat != null ? mat.mainTexture : null;
            if (tex == null || def.uvs == null || def.uvs.Length < 4) return false;
            float u0 = float.MaxValue, v0 = float.MaxValue, u1 = float.MinValue, v1 = float.MinValue;
            foreach (var uv in def.uvs) { u0 = Mathf.Min(u0, uv.x); v0 = Mathf.Min(v0, uv.y); u1 = Mathf.Max(u1, uv.x); v1 = Mathf.Max(v1, uv.y); }
            var old = GUI.color;
            GUI.color = tint * old;
            if (def.flipped == tk2dSpriteDefinition.FlipMode.None)
                GUI.DrawTextureWithTexCoords(r, tex, new Rect(u0, v0, u1 - u0, v1 - v0), true);
            else
            {
                // Packed turned a quarter: draw it turned back about the icon's centre.
                var m = GUI.matrix;
                GUIUtility.RotateAroundPivot(-90f, r.center);
                GUI.DrawTextureWithTexCoords(r, tex, new Rect(u0, v0, u1 - u0, v1 - v0), true);
                GUI.matrix = m;
            }
            GUI.color = old;
            return true;
        }
    }
}
