using UnityEngine;

namespace DarkwoodMP
{
    /// <summary>
    /// The co-op windows' look: Darkwood's menu colours (near-black, grey words that turn white under the mouse,
    /// no colour but a dull red for a refusal) in the shape of Half-Life's and Counter-Strike 1.6's menus: a title
    /// bar, raised buttons with a light top-left edge and a dark bottom-right one, sunken lists and text fields.
    /// Words are drawn in the game's pixel font (PixelFont); text fields use a system font, since you type in them.
    /// </summary>
    public static class MenuSkin
    {
        public static readonly Color Text = new Color(0.70f, 0.70f, 0.70f);
        public static readonly Color Bright = new Color(0.93f, 0.93f, 0.93f);
        public static readonly Color Dim = new Color(0.44f, 0.44f, 0.44f);
        public static readonly Color Warn = new Color(0.72f, 0.30f, 0.26f);

        static readonly Color WindowFill = new Color(0.045f, 0.045f, 0.045f, 0.96f);
        static readonly Color TitleFill = new Color(0.11f, 0.11f, 0.11f, 1f);
        static readonly Color Light = new Color(0.36f, 0.36f, 0.36f, 1f);
        static readonly Color Dark = new Color(0.01f, 0.01f, 0.01f, 1f);
        static readonly Color ButtonFill = new Color(0.12f, 0.12f, 0.12f, 1f);
        static readonly Color ButtonHoverFill = new Color(0.19f, 0.19f, 0.19f, 1f);
        static readonly Color SunkenFill = new Color(0.025f, 0.025f, 0.025f, 1f);

        public static GUISkin Skin { get; private set; }
        public static GUIStyle Window, TitleBar, Button, Pressed, Sunken, Field, Plain, Selected, Line;
        public static Font FieldFont { get; private set; }
        public static Texture2D White { get; private set; }
        private static int _fieldSize;

        public static void Ensure()
        {
            if (FieldFont == null)
            {
                // Tahoma where there is one (Windows), as the game's own pixel font is Tahoma; else a plain sans.
                try { FieldFont = Font.CreateDynamicFontFromOSFont(new[] { "Tahoma", "Verdana", "DejaVu Sans", "Liberation Sans", "Noto Sans", "Arial" }, 12); }
                catch { FieldFont = null; }
            }
            if (Skin == null)
            {
                var s = Object.Instantiate(GUI.skin);
                if (FieldFont != null) s.font = FieldFont;
                Window = new GUIStyle { border = new RectOffset(2, 2, 2, 2), normal = { background = Bevel(WindowFill, Light, Dark) } };
                Window.onNormal.background = Window.normal.background;
                TitleBar = Flat(TitleFill);
                Button = new GUIStyle { border = new RectOffset(2, 2, 2, 2) };
                Button.normal.background = Bevel(ButtonFill, Light, Dark);
                Button.hover.background = Bevel(ButtonHoverFill, new Color(0.55f, 0.55f, 0.55f), Dark);
                Button.active.background = Bevel(SunkenFill, Dark, Light);
                Pressed = new GUIStyle { border = new RectOffset(2, 2, 2, 2), normal = { background = Bevel(new Color(0.07f, 0.07f, 0.07f), Dark, Light) } };
                Sunken = new GUIStyle { border = new RectOffset(2, 2, 2, 2), normal = { background = Bevel(SunkenFill, Dark, Light) } };
                Plain = new GUIStyle();
                Selected = Flat(new Color(0.24f, 0.24f, 0.24f, 1f));
                Line = Flat(new Color(0.22f, 0.22f, 0.22f, 1f));

                White = Solid(Color.white);
                Field = new GUIStyle(s.textField) { font = FieldFont, border = new RectOffset(2, 2, 2, 2), padding = new RectOffset(5, 5, 3, 3), alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip };
                foreach (var st in new[] { Field.normal, Field.hover, Field.active, Field.focused, Field.onNormal, Field.onHover, Field.onActive, Field.onFocused })
                {
                    st.background = Sunken.normal.background;
                    st.textColor = Bright;
                }
                s.settings.cursorColor = Bright;
                s.settings.selectionColor = new Color(0.4f, 0.4f, 0.4f, 0.8f);
                s.verticalScrollbar = new GUIStyle(s.verticalScrollbar) { fixedWidth = 0, border = new RectOffset(2, 2, 2, 2), normal = { background = Bevel(SunkenFill, Dark, Light) } };
                s.verticalScrollbarThumb = new GUIStyle(s.verticalScrollbarThumb) { fixedWidth = 0, border = new RectOffset(2, 2, 2, 2), normal = { background = Bevel(ButtonFill, Light, Dark) } };
                s.verticalScrollbarUpButton = new GUIStyle { fixedHeight = 0 };
                s.verticalScrollbarDownButton = new GUIStyle { fixedHeight = 0 };
                s.horizontalScrollbar = new GUIStyle { fixedHeight = 0 };
                Skin = s;
            }
            // Sizes follow the screen (PixelFont.Scale).
            int scale = PixelFont.Scale;
            if (_fieldSize != scale)
            {
                _fieldSize = scale;
                Field.fontSize = 11 * scale + 1;
                Field.padding = new RectOffset(4 * scale, 4 * scale, 2, 2);
                Skin.verticalScrollbar.fixedWidth = 10 * scale;
                Skin.verticalScrollbarThumb.fixedWidth = 10 * scale;
            }
        }

        private static GUIStyle Flat(Color c) => new GUIStyle { normal = { background = Solid(c) } };

        private static Texture2D Solid(Color c)
        {
            var t = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }

        /// <summary>
        /// A 5x5 box with a one-pixel edge, lit from the top left: light along the top and left, dark along the
        /// bottom and right (swap them for a sunken box). IMGUI stretches the middle (border 2).
        /// </summary>
        private static Texture2D Bevel(Color fill, Color topLeft, Color bottomRight)
        {
            var t = new Texture2D(5, 5) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            for (int x = 0; x < 5; x++)
                for (int y = 0; y < 5; y++) // y = 0 is the bottom row
                {
                    Color c = fill;
                    if (y == 4 || x == 0) c = topLeft;
                    if (y == 0 || x == 4) c = bottomRight;
                    t.SetPixel(x, y, c);
                }
            t.Apply();
            return t;
        }
    }
}
