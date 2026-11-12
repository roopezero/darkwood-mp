using HarmonyLib;
using UnityEngine;

namespace DarkwoodMP
{
    /// <summary>
    /// The mod's part in the game's main menu and pause menu, besides their entries (MenuList draws those): the
    /// mod's version under the game's, the layout of the logo and the versions (MenuLayout), the options pages' words
    /// in the game's font (MenuWords), and the pause menu over the world.
    ///
    /// The pause menu doesn't hide the world: instead of the menu's painted backdrop the world shows through, dimmed
    /// by the menu's own black overlay. Alone the world is paused behind it as before; in co-op it goes on
    /// (World.CoopPause) and the menu's music doesn't start.
    /// </summary>
    public static class MenuEntry
    {
        /// <summary>Call every frame while a menu may be up.</summary>
        public static void Update()
        {
            var mm = Singleton<MainMenu>.Instance;
            if (mm == null || mm.Menu0 == null || !mm.Menu0.activeInHierarchy) return;
            MenuWords.Update(mm);
            // The mod's version under the game's, both in the bottom right corner (MenuLayout).
            var v = mm.CurrentVersion;
            if (v != null && !string.IsNullOrEmpty(v.text) && !v.text.Contains(PluginInfo.Name)) { v.text += $"\n{PluginInfo.Name} {PluginInfo.Version}"; v.Commit(); }
            MenuLayout.Apply(mm);
        }

        // ---- the pause menu in co-op ---------------------------------------------------------------

        private static GameObject _hiddenBackdrop;
        private static bool _dimmed;

        internal static void PauseMenuOpened(MainMenu mm)
        {
            if (!World.WorldShare.PauseMenuInWorld) return;
            // The world stays drawn behind the menu, playing alone too (the game turns its camera off).
            var cam = Singleton<CamMain>.Instance;
            if (cam != null && !cam.gameObject.activeSelf) cam.gameObject.SetActive(true);
            var backdrop = mm.transform.Find("Background");
            if (backdrop != null && backdrop.gameObject.activeSelf) { backdrop.gameObject.SetActive(false); _hiddenBackdrop = backdrop.gameObject; }
            if (mm.blackScreen != null && !mm.blackScreen.activeSelf) { mm.blackScreen.SetActive(true); _dimmed = true; }
            // In co-op the game goes on, with its own sound; alone it's paused and the menu's music plays as before.
            if (World.CoopPause.InCoop && mm.menuMusic != null) { mm.menuMusic.Stop(0.3f); mm.menuMusic = null; }
        }

        internal static void PauseMenuClosed(MainMenu mm)
        {
            if (_hiddenBackdrop != null) { _hiddenBackdrop.SetActive(true); _hiddenBackdrop = null; }
            if (_dimmed && mm.blackScreen != null) mm.blackScreen.SetActive(false);
            _dimmed = false;
        }
    }

    /// <summary>
    /// The menu's own words (the options pages' tabs; the first page's entries, which MenuList hides and draws itself)
    /// are pictures of words in one typeface. For one look with the rest of the menu, each picture is hidden and its word, from the game's own translations (the UI sheet), is drawn in that font
    /// instead. The picture stays (hidden), so the game's code that swaps it on hover or fades it still works; the
    /// word follows its fading and the button's hover. A language the font has no letters for keeps the pictures.
    /// </summary>
    public static class MenuWords
    {
        public const string LabelName = "DarkwoodMP_Label";
        public static readonly Vector3 Scale = new Vector3(2.5f, 2.5f, 2.5f);
        public static readonly Color Grey = new Color(0.6f, 0.6f, 0.6f, 1f); // as the pictures' words

        /// <summary>The picture's name (without its "_0_EN" ending) and the UI sheet's key for its word.</summary>
        private static readonly System.Collections.Generic.Dictionary<string, string> Keys = new System.Collections.Generic.Dictionary<string, string>
        {
            ["play"] = "Play", ["options"] = "Options", ["credits"] = "Credits", ["exit"] = "Quit",
            ["continue"] = "Resume", ["help"] = "Help", ["mainMenu"] = "Main_menu",
            ["controls"] = "Controls", ["graphics"] = "Video", ["sound"] = "Sound",
        };

        private sealed class Word { public global::Button Button; public tk2dBaseSprite Picture; public MeshRenderer PictureRenderer; public tk2dTextMesh Text; }
        private static readonly System.Collections.Generic.List<Word> _words = new System.Collections.Generic.List<Word>();
        private static readonly System.Collections.Generic.HashSet<int> _seen = new System.Collections.Generic.HashSet<int>();
        private static float _nextScan;

        public static void Update(MainMenu mm)
        {
            if (Time.unscaledTime >= _nextScan) { _nextScan = Time.unscaledTime + 1f; Scan(mm); }
            for (int i = _words.Count - 1; i >= 0; i--)
            {
                var w = _words[i];
                if (w.Button == null || w.Text == null) { _words.RemoveAt(i); continue; }
                if (w.PictureRenderer != null && w.PictureRenderer.enabled) w.PictureRenderer.enabled = false;
                var c = w.Button.rolledOver ? Color.white : Grey;
                c.a = w.Picture != null ? w.Picture.color.a : 1f;
                if (w.Text.color != c) { w.Text.color = c; w.Text.Commit(); }
            }
        }

        private static void Scan(MainMenu mm)
        {
            var font = mm.CurrentVersion != null ? mm.CurrentVersion.font : null;
            if (font == null) return;
            foreach (var b in mm.GetComponentsInChildren<global::Button>(true))
            {
                if (b == null || !_seen.Add(b.GetInstanceID())) continue;
                try { Redraw(b, font); }
                catch (System.Exception e) { Plugin.Log.LogWarning($"Couldn't redraw the menu's {b.name}: {e.Message}"); }
            }
        }

        private static void Redraw(global::Button b, tk2dFontData font)
        {
            var pic = b.GetComponent<tk2dBaseSprite>();
            if (pic == null || pic.Collection == null || !pic.Collection.spriteCollectionName.StartsWith("MainMenuButtons")) return;
            string sprite = pic.CurrentSprite?.name ?? "";
            int cut = sprite.IndexOf('_');
            if (cut <= 0 || !Keys.TryGetValue(sprite.Substring(0, cut), out var key)) return;
            string word = global::Language.Get(key, "UI");
            if (string.IsNullOrEmpty(word)) return;
            word = word.ToUpper();
            if (!HasLetters(font, word)) return;

            var label = new GameObject(LabelName);
            label.layer = b.gameObject.layer;
            label.transform.SetParent(b.transform, false);
            var text = label.AddComponent<tk2dTextMesh>();
            text.font = font;
            text.anchor = TextAnchor.MiddleCenter;
            text.scale = Scale;
            text.text = word;
            text.color = Grey;
            text.Commit();
            var pr = b.GetComponent<MeshRenderer>();
            var tr = label.GetComponent<MeshRenderer>();
            if (pr != null && tr != null) { tr.sortingLayerID = pr.sortingLayerID; tr.sortingOrder = pr.sortingOrder; pr.enabled = false; }
            _words.Add(new Word { Button = b, Picture = pic, PictureRenderer = pr, Text = text });
        }

        private static bool HasLetters(tk2dFontData font, string word)
        {
            foreach (char ch in word)
            {
                if (ch == ' ') continue;
                if (font.useDictionary ? font.charDict == null || !font.charDict.ContainsKey(ch) : font.chars == null || ch >= font.chars.Length || font.chars[ch] == null || font.chars[ch].advance <= 0f) return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(MainMenu), "OnEnable")]
    static class MenuEntryLayoutPatch
    {
        static void Postfix(MainMenu __instance) => MenuEntry.PauseMenuOpened(__instance);
    }

    /// <summary>The game placed one of the menu's things (PositionMe): lay the menu out again at once, so it never shows in the game's place.</summary>
    [HarmonyPatch(typeof(PositionMe), nameof(PositionMe.init))]
    static class MenuLayoutPatch
    {
        static void Postfix(PositionMe __instance)
        {
            var mm = Singleton<MainMenu>.Instance;
            if (mm == null || mm.Menu0 == null || !__instance.transform.IsChildOf(mm.Menu0.transform)) return;
            MenuLayout.Apply(mm);
        }
    }

    /// <summary>
    /// The main menu and the pause menu laid out as Half-Life's and Counter-Strike 1.6's: the logo in the top left,
    /// the entries in a column in the lower left (MenuList, from the same left edge) and the versions in the bottom
    /// right corner. The game places the logo and the versions with PositionMe (from the screen's middle and an
    /// offset); this moves them after it, in screen pixels through the UI camera, so it fits any screen shape.
    /// </summary>
    public static class MenuLayout
    {
        private const float Left = 0.065f;      // the logo's left edge (and MenuList's), as a share of the screen's width
        private const float LogoTop = 0.12f;    // the logo's word's top below the screen's top, as a share of its height
        private const float Corner = 0.016f;    // the versions' distance from the right and bottom edges, as a share of the height

        private static bool _busy;

        public static void Apply(MainMenu mm)
        {
            if (_busy || mm == null || mm.Menu0 == null || !mm.Menu0.activeInHierarchy) return;
            var cam = Core.CamUI != null ? Core.CamUI.GetComponent<Camera>() : null;
            if (cam == null) return;
            _busy = true;
            try { Lay(mm, cam); }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Couldn't lay out the menu: {e.Message}"); }
            finally { _busy = false; }
        }

        private static void Lay(MainMenu mm, Camera cam)
        {
            float W = Screen.width, H = Screen.height;
            float left = W * Left;
            // The menu's vignette (Background/Overlay) darkens the screen's edges and is drawn over the logo, which the
            // game keeps in the middle. In the corner it'd be dimmed, so it's brought to the version line's depth, in
            // front of it (the UI camera is orthographic and looks along -Y, so depth doesn't move it on the screen).
            float? front = mm.CurrentVersion != null && cam.orthographic ? mm.CurrentVersion.transform.position.y : (float?)null;
            // The logo: top left, its left edge on the column's.
            var logo = mm.Menu0.transform.Find("Darkwood");
            var logoR = logo != null ? logo.GetComponent<Renderer>() : null;
            if (logoR != null)
            {
                var rect = ScreenRect(cam, logoR.bounds);
                // The picture has a wide clear margin round the word (its glow): the word starts 23% in from its left
                // and 35% down from its top (measured: 810 x 336 pixels, the word 447 x 105, at 1080 lines).
                float wordLeft = rect.xMin + rect.width * 0.232f, wordTop = rect.yMax - rect.height * 0.348f;
                MoveBy(cam, logo, left - wordLeft, (H - H * LogoTop) - wordTop);
                ToFront(logo, front);
            }
            // The versions: right-aligned in the bottom right corner, the player ID line above them.
            float margin = H * Corner, bottom = margin;
            foreach (var t in new[] { mm.CurrentVersion, mm.Menu0.transform.Find("playerID")?.GetComponent<tk2dTextMesh>() })
            {
                if (t == null || !t.gameObject.activeInHierarchy || string.IsNullOrEmpty(t.text)) continue;
                if (t.anchor != TextAnchor.LowerRight) { t.anchor = TextAnchor.LowerRight; t.Commit(); }
                var r = t.GetComponent<Renderer>();
                if (r == null) continue;
                var rect = ScreenRect(cam, r.bounds);
                if (rect.height < 1f) continue;
                MoveBy(cam, t.transform, (W - margin) - rect.xMax, bottom - rect.yMin);
                bottom += rect.height + margin * 0.5f;
            }
        }

        private static void ToFront(Transform t, float? y)
        {
            if (y == null || Mathf.Abs(t.position.y - y.Value) < 0.01f) return;
            var p = t.position;
            t.position = new Vector3(p.x, y.Value, p.z);
        }

        /// <summary>Move a thing by this many screen pixels (whole pixels, so the bitmap font stays sharp).</summary>
        private static void MoveBy(Camera cam, Transform t, float dx, float dy)
        {
            if (Mathf.Abs(dx) < 0.5f && Mathf.Abs(dy) < 0.5f) return;
            var at = cam.WorldToScreenPoint(t.position);
            t.position = cam.ScreenToWorldPoint(new Vector3(at.x + Mathf.Round(dx), at.y + Mathf.Round(dy), at.z));
        }

        /// <summary>A renderer's bounds as a rectangle in screen pixels (from the bottom left).</summary>
        private static Rect ScreenRect(Camera cam, Bounds b)
        {
            Vector3 a = cam.WorldToScreenPoint(b.min), c = cam.WorldToScreenPoint(b.max);
            return Rect.MinMaxRect(Mathf.Min(a.x, c.x), Mathf.Min(a.y, c.y), Mathf.Max(a.x, c.x), Mathf.Max(a.y, c.y));
        }
    }

    [HarmonyPatch(typeof(MainMenu), nameof(MainMenu.close))]
    static class MenuEntryClosePatch
    {
        static void Postfix(MainMenu __instance) => MenuEntry.PauseMenuClosed(__instance);
    }
}
