using UnityEngine;

namespace DarkwoodMP.Menus
{
    /// <summary>
    /// The game's pixel fonts as PixelFont draws them, found once the menu
    /// has loaded them, and Unity's font for when they aren't there. One
    /// lives as long as the game (ModHost.Fonts).
    /// </summary>
    internal sealed class FontFaces
    {
        public sealed class Face
        {
            public tk2dFontData Data;
            public Texture Texture;
            public float LineHeight;
            // where a capital H's top is (y up) and how tall it is, in font
            // pixels
            public float CapTop, CapHeight;
        }

        private Face _body, _title;
        private float _nextLook;
        private GUIStyle _fallback;

        public Face Get(PixelFont.Kind kind)
        {
            if ((_body == null || _title == null)
                && Time.unscaledTime >= _nextLook)
            {
                _nextLook = Time.unscaledTime + 1f;
                foreach (var f in Resources
                    .FindObjectsOfTypeAll<tk2dFontData>())
                {
                    if (f == null)
                    {
                        continue;
                    }

                    var d = f.inst;
                    Texture tex = null;
                    if (d != null && d.material != null)
                    {
                        tex = d.material.mainTexture;
                    }

                    if (tex == null)
                    {
                        continue;
                    }

                    var face = new Face
                    {
                        Data = d,
                        Texture = tex,
                        LineHeight = d.lineHeight,
                        CapTop = 0f,
                        CapHeight = 8f
                    };

                    var h = PixelFont.Glyph(face, 'H');
                    if (h != null && h.p0.y > h.p1.y)
                    {
                        face.CapTop = h.p0.y;
                        face.CapHeight = h.p0.y - h.p1.y;
                    }

                    if (_body == null
                        && f.name.StartsWith("tahoma_11px_light_AA")
                        && !f.name.Contains("occluded"))
                    {
                        _body = face;
                    }

                    if (_title == null
                        && f.name.StartsWith("tahoma_11px_bevel_white"))
                    {
                        _title = face;
                    }
                }

                if (_body != null && _title != null)
                {
                    Plugin.Log.LogInfo(
                        "Co-op windows use the game's fonts: line "
                            + $"{_body.LineHeight}, H "
                            + $"{_body.CapTop}/{_body.CapHeight}");
                }
            }

            if (kind == PixelFont.Kind.Title)
            {
                Face title = _title;
                if (title == null)
                {
                    title = _body;
                }

                return title;
            }

            Face body = _body;
            if (body == null)
            {
                body = _title;
            }

            return body;
        }

        public GUIStyle Fallback(int scale)
        {
            if (_fallback == null)
            {
                _fallback = new GUIStyle(GUI.skin.label)
                {
                    padding = new RectOffset(0, 0, 0, 0),
                    margin = new RectOffset(0, 0, 0, 0),
                    wordWrap = false,
                    clipping = TextClipping.Clip
                };
            }

            _fallback.font = MenuSkin.FieldFont;
            _fallback.fontSize = 11 * scale;
            return _fallback;
        }
    }
}
