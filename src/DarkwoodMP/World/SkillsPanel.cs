using System;
using System.Collections.Generic;
using DarkwoodMP.Combat;
using HarmonyLib;
using UnityEngine;
using RW = Rewired;

namespace DarkwoodMP.World
{
    /// <summary>
    /// The Skills panel: a key (the SkillsKey setting, K by default; none of
    /// the game's default keys use K) opens it anywhere in the world, on the
    /// right. It doesn't open the backpack; the player can open that beside the
    /// panel with its own key. It shows the skill points to spend and the
    /// game's four tiers of skills with their own icons, names and
    /// descriptions, and takes a good skill (and a negative one when the game's
    /// rules want one) for a point (World.SkillTree). Each skill's whole cell
    /// in the grid takes the mouse, so there is no dead space between the
    /// icons, and the details under the grid have a fixed height (room for the
    /// longest description), so the window doesn't change size, or get a
    /// scrollbar that narrows everything, while the mouse moves over it.
    /// Learned skills have a bright frame and a check mark. Clicks on the panel
    /// don't reach the world: while the mouse is over it, the game's mouse
    /// actions read as not pressed (SkillsPanelMouse). Escape closes the panel
    /// before the game's pause menu sees it (SkillsPanelEscape).
    /// </summary>
    public static class SkillsPanel
    {
        private static UiWindow _win;
        // chosen on the panel, not learned yet
        private static string _good, _negative;
        private static string _hover;
        private static string _message;          // why the last Learn didn't go
        // last drawn: icon, cell (screen, top left)
        private static readonly Dictionary<string, Rect[]> _rects =
            new Dictionary<string, Rect[]>();
        // the width the details' height was measured for
        private static float _detailW = -1f;
        private static float _detailH;
        // the Learn button as last drawn (screen, top left), for tests
        private static Rect _learnRect;

        public static bool Open => _win != null && _win.Open;

        private static KeyCode Key
        {
            get
            {
                if (Plugin.Cfg != null)
                {
                    return Plugin.Cfg.SkillsKey.Value;
                }
                return KeyCode.K;
            }
        }
        public static string KeyName => Key.ToString();

        private const string Title = "Skills";

        private static UiWindow Win
        {
            get
            {
                if (_win != null)
                {
                    return _win;
                }
                _win = new UiWindow(0x5d17, Title)
                {
                    MinSize = new Vector2(300, 260),
                    FitContent = true,
                    // On the right, clear of the backpack (on the left) when
                    // the player opens it too.
                    DefaultRect = () => UiWindow.RightSide(
                        360 * UiWindow.S, 420 * UiWindow.S),
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
            if (p == null || !WorldShare.InWorld || WorldShare.Loading
                || Core.mainMenu || Core.loadingGame)
            {
                why = "not in a world";
                return false;
            }
            if (Core.Paused || Core.forbidInputs || p.inMenu() || p.dying
                || p.health <= 0f)
            {
                why = "busy";
                return false;
            }
            if (Spectator.Active)
            {
                why = "watching";
                return false;
            }
            if (SkillTree.Tiers == null)
            {
                why = "no skills";
                return false;
            }
            return true;
        }

        /// <summary>Open it (with the backpack), or close it.</summary>
        public static void Toggle()
        {
            if (Open)
            {
                Win.Close();
                return;
            }
            var p = Player.Instance;
            if (!CanOpen(p, out string why))
            {
                Plugin.Log.LogInfo(
                    $"[skills] the panel can't open now ({why})");
                return;
            }
            _good = _negative = _message = null;
            _rects.Clear();
            Win.ResetPlace();
            Win.Show();
            Plugin.Log.LogInfo(
                $"[skills] panel open: level {p.currentLevel}, "
                    + $"{SkillTree.Points(p)} points to spend, backpack open "
                    + $"{p.Inventory != null && p.Inventory.open}");
        }

        private static void Closed() =>
            Plugin.Log.LogInfo("[skills] panel closed");

        internal static void CloseFromEscape()
        {
            Plugin.Log.LogInfo("[skills] Escape: the panel closes");
            Win.Close();
        }

        /// <summary>
        /// Every frame: the key, and closing when a game menu opens, the game
        /// pauses or the world goes.
        /// </summary>
        public static void Update(bool coopPanelOpen)
        {
            var p = Player.Instance;
            if (Open
                && (!WorldShare.InWorld || Core.mainMenu || p == null
                    || p.inMenu() || Core.Paused || p.dying))
            {
                _win.Open = false;
                Plugin.Log.LogInfo(
                    "[skills] panel closed (a game menu, the pause menu, or "
                        + "the world went)");
            }
            // Escape is taken in SkillsPanelEscape, before the game's pause
            // menu sees it.
            if (coopPanelOpen || GUIUtility.keyboardControl != 0)
            {
                return;
            }
            var key = Key;
            if (key != KeyCode.None && Input.GetKeyDown(key))
            {
                Toggle();
            }
        }

        public static void OnGUI()
        {
            if (!Open)
            {
                return;
            }
            MenuSkin.Ensure();
            var skin = GUI.skin;
            GUI.skin = MenuSkin.Skin;
            Win.OnGUI();
            GUI.skin = skin;
        }

        // ---- drawing --------------------------------------------------------

        private static float Icon => 40 * UiWindow.S;

        private static void Draw(Flow f)
        {
            var p = Player.Instance;
            var tiers = SkillTree.Tiers;
            if (p == null || tiers == null)
            {
                Ui.Text(f, "No skills here.");
                return;
            }
            var chosen = SkillTree.Chosen(p);
            int level = p.currentLevel,
                points = SkillTree.Points(level, chosen);
            bool needNeg =
                _good != null && SkillTree.NeedsNegative(chosen, _good);
            if (Event.current.type == EventType.Layout)
            {
                _hover = null;
            }

            var top = f.Row(PixelFont.Line + 4 * UiWindow.S, 0, 0);
            Color color;
            if (points > 0)
            {
                color = MenuSkin.Bright;
            }
            else
            {
                color = MenuSkin.Text;
            }
            Ui.Label(top[0], $"Skill points: {points}", color);
            Ui.Label(
                top[1], $"Level {level}", MenuSkin.Dim, TextAnchor.MiddleRight);
            f.Space(4);

            // The four tiers side by side, as the game's skill choice shows
            // them: good skills, then the negative ones.
            int n = tiers.Count, rows = 0, negRows = 0;
            foreach (var t in tiers)
            {
                rows = Math.Max(rows, t.skills.Count);
                negRows = Math.Max(negRows, t.negativeSkills.Count);
            }
            float colW = f.Width / Math.Max(1, n),
                icon = Mathf.Min(Icon, colW - 8 * UiWindow.S),
                gap = 6 * UiWindow.S;
            var head = f.Next(PixelFont.Line * 2 + 2 * UiWindow.S);
            for (int i = 0; i < n; i++)
            {
                var t = tiers[i];
                bool open = t.level <= level;
                var c =
                    new Rect(head.x + i * colW, head.y, colW, PixelFont.Line);
                string tier = SkillTree.TierName(i);
                Color tierColor;
                if (open)
                {
                    tierColor = MenuSkin.Bright;
                }
                else
                {
                    tierColor = MenuSkin.Dim;
                }
                PixelFont.Draw(
                    c, tier, tierColor, TextAnchor.MiddleCenter,
                    PixelFont.Kind.Title);
                var under = new Rect(
                    c.x, c.yMax + 2 * UiWindow.S, colW, PixelFont.Line);
                string opens;
                if (open)
                {
                    opens = "open";
                }
                else
                {
                    opens = $"level {t.level}";
                }
                Ui.Label(under, opens, MenuSkin.Dim, TextAnchor.MiddleCenter);
            }
            f.Space(2);
            var grid = f.Next(rows * (icon + gap));
            for (int i = 0; i < n; i++)
            {
                for (int k = 0; k < tiers[i].skills.Count; k++)
                {
                    var sk = tiers[i].skills[k];
                    if (sk == null)
                    {
                        continue;
                    }
                    var cell = new Rect(
                        grid.x + i * colW, grid.y + k * (icon + gap), colW,
                        icon + gap);
                    bool can =
                        SkillTree.CantTake(level, chosen, sk.name) == null;
                    DrawSkill(
                        cell, icon, sk, chosen.Contains(sk.name), can,
                        sk.name == _good, false);
                }
            }
            if (negRows > 0)
            {
                var lr = f.Next(PixelFont.Line + 4 * UiWindow.S);
                GUI.Box(
                    new Rect(lr.x, lr.y + lr.height / 2f, lr.width, UiWindow.S),
                    GUIContent.none, MenuSkin.Line);
                float lw = PixelFont.Width("Negative") + 12 * UiWindow.S;
                GUI.Box(
                    new Rect(lr.x + (lr.width - lw) / 2f, lr.y, lw, lr.height),
                    GUIContent.none, MenuSkin.Plain);
                Ui.Label(
                    lr, "Negative", MenuSkin.Warn, TextAnchor.MiddleCenter);
                var ngrid = f.Next(negRows * (icon + gap));
                for (int i = 0; i < n; i++)
                {
                    for (int k = 0; k < tiers[i].negativeSkills.Count; k++)
                    {
                        var sk = tiers[i].negativeSkills[k];
                        if (sk == null)
                        {
                            continue;
                        }
                        var cell = new Rect(
                            ngrid.x + i * colW, ngrid.y + k * (icon + gap),
                            colW, icon + gap);
                        // With a skill chosen, the negative skills of its tier
                        // when it needs one; else any a tier still owes.
                        bool can = (_good == null || needNeg)
                            && SkillTree.CantTakeNegative(
                                level, chosen, sk.name, _good) == null;
                        DrawSkill(
                            cell, icon, sk, chosen.Contains(sk.name), can,
                            sk.name == _negative, true);
                    }
                }
            }

            // What the skill under the mouse (or the one chosen) does.
            f.Space(2);
            GUI.Box(f.Next(UiWindow.S), GUIContent.none, MenuSkin.Line);
            f.Space(2);
            // Room for the longest description, so the window keeps its size
            // whichever skill is shown.
            MeasureDetails(tiers, f.Width);
            float detailTop = f.Y;
            string show = _hover;
            if (show == null)
            {
                show = _good;
            }
            if (show == null)
            {
                show = _negative;
            }
            if (show != null)
            {
                SkillTree.TierOf(show, out bool neg);
                Rect line = f.Next(PixelFont.Line + 2 * UiWindow.S);
                string title = SkillName(show);
                Color nameColor;
                if (neg)
                {
                    nameColor = MenuSkin.Warn;
                }
                else
                {
                    nameColor = MenuSkin.Bright;
                }
                PixelFont.Draw(
                    line, title, nameColor, TextAnchor.MiddleLeft,
                    PixelFont.Kind.Title);
                string state;
                if (chosen.Contains(show))
                {
                    state = "Learned";
                }
                else if (neg)
                {
                    state = SkillTree.CantTakeNegative(level, chosen, show);
                    if (state == null)
                    {
                        state = "Taken with the first skill of its tier";
                    }
                }
                else
                {
                    state = SkillTree.CantTake(level, chosen, show);
                    if (state == null)
                    {
                        if (SkillTree.NeedsNegative(chosen, show))
                        {
                            state = "Comes with a negative skill of its tier";
                        }
                        else
                        {
                            state = "Can be learned";
                        }
                    }
                }
                Ui.Label(f, Capital(state), MenuSkin.Dim);
                Ui.Text(f, SkillDesc(show));
            }
            else
            {
                string text = "Take a level at a stove for a skill point.";
                if (points > 0)
                {
                    text = "Choose a skill to learn.";
                }
                Ui.Text(f, text, MenuSkin.Dim);
            }
            // Never shorter than what was drawn, so a description that takes
            // more room than measured can't make the body grow while hovering.
            if (f.Y - detailTop > _detailH)
            {
                _detailH = f.Y - detailTop;
            }
            f.Y = Mathf.Max(f.Y, detailTop + _detailH);
        }

        /// <summary>
        /// Sets _detailH: the details' height for the skill with the longest
        /// description, at this width.
        /// </summary>
        private static void MeasureDetails(
            List<SkillTiers.Tier> tiers, float width)
        {
            if (Mathf.Abs(width - _detailW) < 0.5f)
            {
                return;
            }
            int lines = 2;
            foreach (var t in tiers)
            {
                foreach (var list in new[] { t.skills, t.negativeSkills })
                {
                    foreach (var sk in list)
                    {
                        if (sk != null)
                        {
                            lines = Math.Max(
                                lines,
                                PixelFont
                                    .Wrap(SkillDesc(sk.name), width).Count);
                        }
                    }
                }
            }
            float S = UiWindow.S, gap = 4 * S;   // Flow.Gap
            _detailW = width;
            // As Draw lays them out: the name (Line + 2S and a gap), the state
            // (Line and a gap), the text (Ui.Text).
            _detailH = (PixelFont.Line + 2 * S + gap) + (PixelFont.Line + gap)
                + lines * (PixelFont.Line + S) + gap - S;
        }

        private static void DrawFooter(Rect r)
        {
            var p = Player.Instance;
            if (p == null)
            {
                return;
            }
            var chosen = SkillTree.Chosen(p);
            int level = p.currentLevel;
            bool needNeg =
                _good != null && SkillTree.NeedsNegative(chosen, _good);
            var c = Ui.Split(r, 0, 90);
            string status = _message;
            if (status == null)
            {
                if (SkillTree.Waiting)
                {
                    status = "Asking the host...";
                }
                else if (_good == null)
                {
                    if (SkillTree.Points(level, chosen) > 0)
                    {
                        status = "";
                    }
                    else
                    {
                        status = "No skill points to spend";
                    }
                }
                else if (needNeg && _negative == null)
                {
                    status =
                        "Choose a negative skill of its tier to go with it";
                }
                else
                {
                    status = "";
                }
            }
            Rect cell = c[0];
            string fitted = PixelFont.Fit(status, c[0].width);
            Color tint;
            if (_message != null)
            {
                tint = MenuSkin.Warn;
            }
            else
            {
                tint = MenuSkin.Dim;
            }
            Ui.Label(cell, fitted, tint);
            bool ready = false;
            if (_good != null && !SkillTree.Waiting)
            {
                string negative = null;
                if (needNeg)
                {
                    negative = _negative;
                }
                ready = SkillTree.Check(level, chosen, _good, negative) == null;
            }
            if (Event.current.type == EventType.Repaint)
            {
                _learnRect = ToScreen(c[1]);
            }
            if (Ui.Button(c[1], "Learn", ready))
            {
                LearnChosen();
            }
        }

        private static void LearnChosen()
        {
            var p = Player.Instance;
            bool needNeg = p != null && _good != null
                && SkillTree.NeedsNegative(SkillTree.Chosen(p), _good);
            string negative = null;
            if (needNeg)
            {
                negative = _negative;
            }
            if (SkillTree.Learn(_good, negative, out string why))
            {
                _good = _negative = _message = null;
            }
            else
            {
                _message = Capital(why);
            }
        }

        // The frames' colours: a learned skill's (bright, red for a negative
        // one), the one chosen to learn, the mouse's.
        private static readonly Color LearnedFrame = new Color(
            0.86f, 0.80f, 0.62f), LearnedNegFrame = new Color(
                0.80f, 0.32f, 0.26f),
            ChosenFrame = new Color(0.62f, 0.62f, 0.62f),
            HoverFrame = new Color(0.40f, 0.40f, 0.40f);

        /// <summary>
        /// One skill: the icon centred in its cell, and the whole cell takes
        /// the mouse (the hover, the details and the click), so the space
        /// between the icons isn't dead. A learned skill is drawn in full
        /// colour in a bright frame with a check mark in its corner; one chosen
        /// to learn has a grey frame; the others are dimmed.
        /// </summary>
        private static void DrawSkill(
            Rect cell, float icon, PlayerSkill sk, bool have, bool can,
            bool picked, bool negative)
        {
            float S = UiWindow.S;
            var r = new Rect(
                cell.x + (cell.width - icon) / 2f,
                cell.y + (cell.height - icon) / 2f, icon, icon);
            Vector2 mouse;
            if (TestMouse.HasValue)
            {
                mouse = GUIUtility.ScreenToGUIPoint(TestMouse.Value);
            }
            else
            {
                mouse = Event.current.mousePosition;
            }
            bool hover = cell.Contains(mouse);
            if (hover)
            {
                _hover = sk.name;
            }
            var frame = new Rect(
                r.x - 3 * S, r.y - 3 * S, r.width + 6 * S, r.height + 6 * S);
            GUI.Box(frame, GUIContent.none, MenuSkin.Sunken);
            Color tint;
            if (have)
            {
                tint = Color.white;
            }
            else if (can)
            {
                if (hover)
                {
                    tint = new Color(0.85f, 0.85f, 0.85f);
                }
                else
                {
                    tint = new Color(0.55f, 0.55f, 0.55f);
                }
            }
            else
            {
                tint = new Color(0.25f, 0.25f, 0.25f);
            }
            if (negative)
            {
                float brightness = 0.3f;
                if (have)
                {
                    brightness = 1f;
                }
                else if (can)
                {
                    if (hover)
                    {
                        brightness = 0.9f;
                    }
                    else
                    {
                        brightness = 0.65f;
                    }
                }
                tint = new Color(1f, 0.4f, 0.4f) * brightness;
            }
            tint.a = 1f;
            if (!SkillIcons.Draw(r, sk.name, tint))
            {
                PixelFont.Draw(
                    r, PixelFont.Fit(SkillName(sk.name), r.width), tint,
                    TextAnchor.MiddleCenter);
            }
            if (Event.current.type == EventType.Repaint)
            {
                if (have)
                {
                    Color c;
                    if (negative)
                    {
                        c = LearnedNegFrame;
                    }
                    else
                    {
                        c = LearnedFrame;
                    }
                    Outline(frame, 2 * S, c);
                }
                else if (picked)
                {
                    Outline(frame, 2 * S, ChosenFrame);
                }
                else if (hover && can)
                {
                    Outline(frame, S, HoverFrame);
                }
                if (have)
                {
                    Color markColor;
                    if (negative)
                    {
                        markColor = LearnedNegFrame;
                    }
                    else
                    {
                        markColor = LearnedFrame;
                    }
                    CheckMark(new Vector2(frame.xMax, frame.y), S, markColor);
                }
                _rects[sk.name] = new[] { ToScreen(r), ToScreen(cell) };
            }
            if (GUI.Button(cell, GUIContent.none, GUIStyle.none) && can
                && !have)
            {
                _message = null;
                if (negative)
                {
                    if (_negative == sk.name)
                    {
                        _negative = null;
                    }
                    else
                    {
                        _negative = sk.name;
                    }
                }
                else
                {
                    if (_good == sk.name)
                    {
                        _good = null;
                    }
                    else
                    {
                        _good = sk.name;
                    }
                }
                // A negative skill goes with the first skill of its own tier: a
                // pick that doesn't fit the other drops it.
                if (_good != null && _negative != null
                    && SkillTree.TierOf(_good, out _) != SkillTree.TierOf(
                        _negative, out _))
                {
                    if (negative)
                    {
                        _good = null;
                    }
                    else
                    {
                        _negative = null;
                    }
                }
                var p = Player.Instance;
                if (_good != null && _negative != null && p != null
                    && !SkillTree.NeedsNegative(SkillTree.Chosen(p), _good))
                {
                    _negative = null;
                }
                try
                {
                    AudioController.Play("UI_click");
                }
                catch
                {
                }
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

        /// <summary>
        /// A small pixel check mark on a dark square, its top right corner at
        /// this point.
        /// </summary>
        private static void CheckMark(Vector2 topRight, float S, Color c)
        {
            float u = Mathf.Max(1f, Mathf.Round(S * 1.5f)),
                w = 11 * u,
                h = 9 * u;
            var box = new Rect(topRight.x - w, topRight.y, w, h);
            Fill(box, new Color(0.06f, 0.06f, 0.06f, 1f));
            Outline(box, Mathf.Max(1f, S), c);
            // The tick: down from the left to the bottom, then up to the right,
            // two pixels thick.
            int[] ys = { 4, 5, 6, 5, 4, 3, 2 };
            for (int i = 0; i < ys.Length; i++)
            {
                Fill(
                    new Rect(
                        box.x + (2 + i) * u, box.y + (ys[i] - 1) * u, u, 2 * u),
                    c);
            }
        }

        private static string Capital(string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return s;
            }
            return char.ToUpperInvariant(s[0]) + s.Substring(1);
        }

        private static readonly Dictionary<string, string> _names =
            new Dictionary<string, string>(),
            _descs = new Dictionary<string, string>();

        public static string SkillName(string name) =>
            Lang(_names, name + "_name", name);
        public static string SkillDesc(string name) =>
            Lang(_descs, name + "_desc", "");

        /// <summary>
        /// The game's text (sheet Skills), without tk2d's colour codes.
        /// </summary>
        private static string Lang(
            Dictionary<string, string> cache, string key, string fallback)
        {
            if (cache.TryGetValue(key, out var s))
            {
                return s;
            }
            try
            {
                if (global::Language.HasKey(key, "Skills"))
                {
                    s = global::Language.Get(key, "Skills");
                }
                else
                {
                    s = fallback;
                }
            }
            catch
            {
                s = fallback;
            }
            if (s == null)
            {
                s = fallback;
            }
            s = Clean(s);
            cache[key] = s;
            return s;
        }

        private static string Clean(string s)
        {
            var sb = new System.Text.StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                // tk2d inline colours: ^cRGBA (hex) or ^CRRGGBBAA.
                if (s[i] == '^' && i + 1 < s.Length
                    && (s[i + 1] == 'c' || s[i + 1] == 'C'))
                {
                    int skip = 8;
                    if (s[i + 1] == 'c')
                    {
                        skip = 4;
                    }
                    i += 1 + skip;
                    continue;
                }
                char ch;
                if (s[i] == '\r')
                {
                    ch = ' ';
                }
                else
                {
                    ch = s[i];
                }
                sb.Append(ch);
            }
            return sb.ToString().Trim();
        }

        // ---- tests ----------------------------------------------------------

        /// <summary>For tests: the key pressed (as Update sees it).</summary>
        internal static void TestPressKey() => Toggle();

        /// <summary>
        /// For tests: choose and learn on the panel, as the clicks do; the
        /// result (null: learned).
        /// </summary>
        internal static string TestLearn(string good, string negative)
        {
            _good = good;
            _negative = negative;
            _message = null;
            LearnChosen();
            return _message;
        }

        /// <summary>
        /// For tests: whether the Learn button would take this choice now (as
        /// DrawFooter enables it).
        /// </summary>
        internal static bool TestReady(string good, string negative)
        {
            var p = Player.Instance;
            if (p == null || SkillTree.Waiting)
            {
                return false;
            }
            var chosen = SkillTree.Chosen(p);
            bool needNeg = SkillTree.NeedsNegative(chosen, good);
            string needed = null;
            if (needNeg)
            {
                needed = negative;
            }
            return SkillTree.Check(p.currentLevel, chosen, good, needed)
                == null;
        }

        /// <summary>
        /// For tests: show this skill's details as if the mouse were over it.
        /// </summary>
        internal static void TestPick(string good, string negative)
        {
            _good = good;
            _negative = negative;
        }

        /// <summary>
        /// For tests: the mouse as the panel sees it (screen pixels from the
        /// top left); null: the real mouse.
        /// </summary>
        internal static Vector2? TestMouse;

        /// <summary>
        /// For tests: the skill the mouse is over (as the last frame drew it).
        /// </summary>
        internal static string TestHover => _hover;

        /// <summary>
        /// For tests: the good skill chosen on the panel (by a click), not
        /// learned yet.
        /// </summary>
        internal static string TestChosen => _good;

        /// <summary>
        /// For tests: a skill's icon and its cell (the hover and click area) as
        /// last drawn, in screen pixels from the top left.
        /// </summary>
        internal static bool TestRects(
            string skill, out Rect icon, out Rect cell)
        {
            icon = cell = default;
            if (!_rects.TryGetValue(skill, out var rs))
            {
                return false;
            }
            icon = rs[0];
            cell = rs[1];
            return true;
        }

        /// <summary>
        /// For tests: the test mouse goes over every skill's cell in turn,
        /// three frames each, and every frame the window's size, whether its
        /// body scrolls, its content's height and the first skill's cell must
        /// stay as they were with the mouse over nothing. Returns the number of
        /// frames where something moved (logged as FAIL lines).
        /// </summary>
        internal static System.Collections.IEnumerator TestHoverSweep(
            Action<int> done)
        {
            TestMouse = new Vector2(-100, -100);
            yield return null;
            yield return null;
            var skills = new List<string>(_rects.Keys);
            string first = null;
            if (skills.Count > 0)
            {
                first = skills[0];
            }
            Rect firstCell = default;
            if (first != null)
            {
                firstCell = _rects[first][1];
            }
            Rect win = Win.Rect;
            bool scrolls = Win.TestScrolls;
            float content = Win.TestContentHeight;
            Plugin.Log.LogInfo(
                $"[skills] hover sweep over {skills.Count} skills: window "
                    + $"{win.width}x{win.height}, scrolls {scrolls}, content "
                    + $"{content}");
            int bad = 0, frames = 0;
            foreach (var sk in skills)
            {
                var cell = _rects[sk][1];
                TestMouse = cell.center;
                for (int i = 0; i < 3; i++)
                {
                    yield return null;
                    frames++;
                    Rect fc = default;
                    if (first != null)
                    {
                        fc = _rects[first][1];
                    }
                    if (Win.Rect.size != win.size
                        || Win.TestScrolls != scrolls
                        || Mathf.Abs(Win.TestContentHeight - content) > 0.5f
                        || fc != firstCell)
                    {
                        bad++;
                        string hover = _hover;
                        if (hover == null)
                        {
                            hover = "nothing";
                        }
                        Plugin.Log.LogInfo(
                            $"[skills] FAIL hovering {sk} ({hover}): window "
                                + $"{Win.Rect.width}x{Win.Rect.height}, "
                                + $"scrolls {Win.TestScrolls}, content "
                                + $"{Win.TestContentHeight}, first cell {fc} "
                                + $"(was {firstCell})");
                    }
                }
            }
            TestMouse = null;
            string text;
            if (bad == 0)
            {
                text = "PASS nothing moved";
            }
            else
            {
                text = $"FAIL {bad} frames moved";
            }
            Plugin.Log.LogInfo(
                $"[skills] hover sweep: {frames} frames, {text}");
            if (done != null)
            {
                done(bad);
            }
        }

        /// <summary>
        /// For tests: spend every point with the real mouse (RealMouse): for
        /// each, a click on a skill the rules allow, one on its negative skill
        /// if one must go with it, and one on Learn, as a player does. Every
        /// click is logged with what the panel made of it. done(failures).
        /// </summary>
        internal static System.Collections.IEnumerator TestClickAll(
            Action<List<string>> done)
        {
            var fails = new List<string>();
            var p = Player.Instance;
            int n = 0;
            if (!Open)
            {
                Toggle();
            }
            yield return new WaitForSecondsRealtime(1f);
            while (p != null && Open && SkillTree.Points(p) > 0 && n < 10)
            {
                n++;
                var choice = SkillTree.AnyChoice(p);
                if (choice == null)
                {
                    fails.Add(
                        $"{SkillTree.Points(p)} points but nothing to learn");
                    break;
                }
                int before = SkillTree.Points(p);
                foreach (var sk in new[] { choice[0], choice[1] })
                {
                    if (sk == null)
                    {
                        continue;
                    }
                    if (!_rects.TryGetValue(sk, out var rs))
                    {
                        fails.Add($"{sk} not drawn");
                        continue;
                    }
                    bool reached = false;
                    yield return RealMouse.MoveTo(
                        rs[1].center, ok => reached = ok);
                    yield return RealMouse.Click();
                    Vector2 mouse = RealMouse.Now;
                    Rect cell = rs[1];
                    string over = _hover;
                    if (over == null)
                    {
                        over = "nothing";
                    }
                    string picked = _good;
                    if (picked == null)
                    {
                        picked = "nothing";
                    }
                    string pickedNegative = _negative;
                    if (pickedNegative == null)
                    {
                        pickedNegative = "nothing";
                    }
                    string why = _message;
                    if (why == null)
                    {
                        why = "none";
                    }
                    Plugin.Log.LogInfo(
                        $"[skills] real click {n} on {sk} (reached {reached}, "
                            + $"mouse {mouse}, cell {cell}): over {over}, "
                            + $"chosen {picked} with {pickedNegative}, message "
                            + $"{why}");
                }
                float until = Time.realtimeSinceStartup + 8f;
                while (SkillTree.Waiting && Time.realtimeSinceStartup < until)
                {
                    yield return null;
                }
                yield return null;
                yield return RealMouse.MoveTo(_learnRect.center);
                yield return RealMouse.Click();
                yield return new WaitForSecondsRealtime(0.3f);
                until = Time.realtimeSinceStartup + 8f;
                while (SkillTree.Waiting && Time.realtimeSinceStartup < until)
                {
                    yield return null;
                }
                yield return new WaitForSecondsRealtime(0.3f);
                var have = SkillTree.Chosen(p);
                bool ok2 = have.Contains(choice[0])
                    && (choice[1] == null || have.Contains(choice[1]));
                Vector2 pointer = RealMouse.Now;
                Rect button = _learnRect;
                string outcome = "NOT learned";
                if (ok2)
                {
                    outcome = "learned";
                }
                string skill = choice[0];
                string withNegative = "";
                if (choice[1] != null)
                {
                    withNegative = " with " + choice[1];
                }
                int after = SkillTree.Points(p);
                bool panelOpen = Open;
                string stillPicked = _good;
                if (stillPicked == null)
                {
                    stillPicked = "nothing";
                }
                string reason = _message;
                if (reason == null)
                {
                    reason = "none";
                }
                Plugin.Log.LogInfo(
                    $"[skills] real click {n} on Learn (mouse {pointer}, "
                        + $"button {button}): {outcome} {skill}{withNegative}, "
                        + $"points {before} -> {after}, panel open "
                        + $"{panelOpen}, chosen {stillPicked}, message "
                        + $"{reason}");
                if (!ok2)
                {
                    string text = "";
                    if (choice[1] != null)
                    {
                        text = "+" + choice[1];
                    }
                    string good = _good;
                    if (good == null)
                    {
                        good = "nothing";
                    }
                    string negative = _negative;
                    if (negative == null)
                    {
                        negative = "nothing";
                    }
                    string message = _message;
                    if (message == null)
                    {
                        message = "none";
                    }
                    fails.Add(
                        $"choice {n} ({choice[0]}{text}) not learned: chosen "
                            + $"{good} with {negative}, message {message}");
                    break;
                }
            }
            if (!Open)
            {
                fails.Add("the panel closed");
            }
            if (p != null && SkillTree.Points(p) > 0 && fails.Count == 0)
            {
                fails.Add($"{SkillTree.Points(p)} points left");
            }
            if (SkillTree.NegativesWrong(p) is string wrong)
            {
                fails.Add("negative skills: " + wrong);
            }
            if (done != null)
            {
                done(fails);
            }
        }

        /// <summary>
        /// For tests: the panel's window, in screen pixels from the top left.
        /// </summary>
        internal static Rect TestWindow => Win.Rect;

        // ---- the game's input while the panel is open -----------------------

        private static readonly HashSet<string> MouseActions =
            new HashSet<string>
        {
            "Primary Action",
            "Aim",
            "Secondary Attack",
            "Attack"
        };
        private static readonly bool[] _pressedOnPanel = new bool[3];
        private static int _mouseFrame = -1;
        private static bool _overPanel;
        /// <summary>
        /// For tests: reads of the game's mouse actions answered "not pressed"
        /// because of the panel.
        /// </summary>
        internal static int TestEaten;

        /// <summary>
        /// The mouse over the panel's window, and the mouse buttons pressed
        /// there and still held (once a frame).
        /// </summary>
        private static void MouseState()
        {
            if (_mouseFrame == Time.frameCount)
            {
                return;
            }
            _mouseFrame = Time.frameCount;
            var m = Input.mousePosition;
            _overPanel =
                Win.Rect.Contains(new Vector2(m.x, Screen.height - m.y));
            for (int b = 0; b < 3; b++)
            {
                if (Input.GetMouseButtonDown(b) && _overPanel)
                {
                    _pressedOnPanel[b] = true;
                }
                else if (!Input.GetMouseButton(b))
                {
                    _pressedOnPanel[b] = false;
                }
            }
        }

        /// <summary>
        /// Whether the game's read of this action should be "not pressed": a
        /// mouse action pressed over the panel, or held after a press that
        /// started there. A press that started in the world (holding aim) goes
        /// on as it was.
        /// </summary>
        internal static bool EatsAction(string action, bool down)
        {
            if (!Open || action == null || !MouseActions.Contains(action))
            {
                return false;
            }
            MouseState();
            bool held =
                _pressedOnPanel[0] || _pressedOnPanel[1] || _pressedOnPanel[2];
            bool eat = held || (down && _overPanel);
            if (eat)
            {
                TestEaten++;
            }
            return eat;
        }
    }

    /// <summary>
    /// The game's mouse actions read as not pressed while they are pressed on
    /// the Skills panel (SkillsPanel.EatsAction).
    /// </summary>
    [HarmonyPatch(
        typeof(RW.Player), nameof(RW.Player.GetButtonDown),
        new[] { typeof(string) })]
    internal static class SkillsPanelMouse
    {
        private static bool Prefix(string actionName, ref bool __result)
        {
            if (!SkillsPanel.EatsAction(actionName, true))
            {
                return true;
            }
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(
        typeof(RW.Player), nameof(RW.Player.GetButton),
        new[] { typeof(string) })]
    internal static class SkillsPanelMouseHeld
    {
        private static bool Prefix(string actionName, ref bool __result)
        {
            if (!SkillsPanel.EatsAction(actionName, false))
            {
                return true;
            }
            __result = false;
            return false;
        }
    }

    /// <summary>
    /// Escape with the Skills panel open closes the panel, not the backpack or
    /// into the pause menu.
    /// </summary>
    [HarmonyPatch(typeof(InputScript), nameof(InputScript.onPressEsc))]
    internal static class SkillsPanelEscape
    {
        private static bool Prefix()
        {
            if (!SkillsPanel.Open || Core.forbidInputs)
            {
                return true; // a film to skip goes first
            }
            SkillsPanel.CloseFromEscape();
            return false;
        }
    }

    /// <summary>
    /// The game's skill icons (tk2d sprites "skill_NAME" in the UISkill
    /// prefab's collection), drawn in IMGUI.
    /// </summary>
    internal static class SkillIcons
    {
        private static tk2dSpriteCollectionData _coll;
        private static bool _tried;
        private static readonly Dictionary<string, int> _ids =
            new Dictionary<string, int>();

        public static bool Draw(Rect r, string skill, Color tint)
        {
            if (!_tried)
            {
                _tried = true;
                try
                {
                    var go = Resources.Load("Prefabs/UI/UISkill") as GameObject;
                    tk2dSprite spr = null;
                    if (go != null)
                    {
                        spr = go.GetComponent<tk2dSprite>();
                    }
                    if (spr != null && spr.Collection != null)
                    {
                        _coll = spr.Collection.inst;
                    }
                    else
                    {
                        _coll = null;
                    }
                    if (_coll == null)
                    {
                        Plugin.Log.LogWarning(
                            "[skills] no skill icons (UISkill's sprites): "
                                + "names only");
                    }
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning(
                        $"[skills] no skill icons: {e.Message}");
                }
            }
            if (_coll == null)
            {
                return false;
            }
            if (!_ids.TryGetValue(skill, out int id))
            {
                id = _coll.GetSpriteIdByName("skill_" + skill, -1);
                _ids[skill] = id;
                if (id < 0)
                {
                    Plugin.Log.LogInfo($"[skills] no icon for {skill}");
                }
                else
                {
                    var d0 = _coll.spriteDefinitions[id];
                    Plugin.Log.LogInfo(
                        $"[skills] icon for {skill}: flipped {d0.flipped}, uvs "
                            + $"{string.Join(" ", Array.ConvertAll(d0.uvs, u => $"({u.x:0.###},{u.y:0.###})"))}");
                }
            }
            if (id < 0 || Event.current.type != EventType.Repaint)
            {
                return id >= 0;
            }
            var def = _coll.spriteDefinitions[id];
            Material mat;
            if (def.materialInst != null)
            {
                mat = def.materialInst;
            }
            else
            {
                mat = def.material;
            }
            Texture tex = null;
            if (mat != null)
            {
                tex = mat.mainTexture;
            }
            if (tex == null || def.uvs == null || def.uvs.Length < 4)
            {
                return false;
            }
            float u0 = float.MaxValue,
                v0 = float.MaxValue,
                u1 = float.MinValue,
                v1 = float.MinValue;
            foreach (var uv in def.uvs)
            {
                u0 = Mathf.Min(u0, uv.x);
                v0 = Mathf.Min(v0, uv.y);
                u1 = Mathf.Max(u1, uv.x);
                v1 = Mathf.Max(v1, uv.y);
            }
            var old = GUI.color;
            GUI.color = tint * old;
            if (def.flipped == tk2dSpriteDefinition.FlipMode.None)
            {
                GUI.DrawTextureWithTexCoords(
                    r, tex, new Rect(u0, v0, u1 - u0, v1 - v0), true);
            }
            else
            {
                // Packed turned a quarter: draw it turned back about the icon's
                // centre.
                var m = GUI.matrix;
                GUIUtility.RotateAroundPivot(-90f, r.center);
                GUI.DrawTextureWithTexCoords(
                    r, tex, new Rect(u0, v0, u1 - u0, v1 - v0), true);
                GUI.matrix = m;
            }
            GUI.color = old;
            return true;
        }
    }
}
