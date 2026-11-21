using UnityEngine;

namespace DarkwoodMP
{
    /// <summary>
    /// The co-op windows' look: Darkwood's menu colours (near-black, grey words
    /// that turn white under the mouse, no colour but a dull red for a refusal)
    /// in the shape of Half-Life's and Counter-Strike 1.6's menus: a title bar,
    /// raised buttons with a light top-left edge and a dark bottom-right one,
    /// sunken lists and text fields. Words are drawn in the game's pixel font
    /// (PixelFont); text fields use a system font, since you type in them.
    /// </summary>
    public static class MenuSkin
    {
        public static readonly Color Text = new Color(0.70f, 0.70f, 0.70f);
        public static readonly Color Bright = new Color(0.93f, 0.93f, 0.93f);
        public static readonly Color Dim = new Color(0.44f, 0.44f, 0.44f);
        public static readonly Color Warn = new Color(0.72f, 0.30f, 0.26f);

        private static MenuStyles Styles => Plugin.Instance.Host.Styles;

        public static GUISkin Skin => Styles.Skin;

        public static GUIStyle Window => Styles.Window;

        public static GUIStyle TitleBar => Styles.TitleBar;

        public static GUIStyle Button => Styles.Button;

        public static GUIStyle Pressed => Styles.Pressed;

        public static GUIStyle Sunken => Styles.Sunken;

        public static GUIStyle Field => Styles.Field;

        public static GUIStyle Plain => Styles.Plain;

        public static GUIStyle Selected => Styles.Selected;

        public static GUIStyle Line => Styles.Line;

        public static Font FieldFont => Styles.FieldFont;

        public static Texture2D White => Styles.White;

        /// Makes the skin the first time, and sizes it to the screen.
        public static void Ensure()
        {
            Styles.Ensure();
        }
    }
}
