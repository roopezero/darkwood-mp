using System;
using System.Collections.Generic;
using UnityEngine;

namespace DarkwoodMP.Menus
{
    /// <summary>
    /// The menu's own words (the options pages' tabs; the first page's entries,
    /// which MenuList hides and draws itself) are pictures of words in one
    /// typeface. For one look with the rest of the menu, each picture is hidden
    /// and its word, from the game's own translations (the UI sheet), is drawn
    /// in that font instead. The picture stays (hidden), so the game's code
    /// that swaps it on hover or fades it still works; the word follows its
    /// fading and the button's hover. A language the font has no letters for
    /// keeps the pictures.
    /// </summary>
    public sealed class MenuWords
    {
        private const string LabelName = "DarkwoodMP_Label";
        private readonly Vector3 Scale = new Vector3(2.5f, 2.5f, 2.5f);
        // as the pictures' words
        private static readonly Color Grey = new Color(0.6f, 0.6f, 0.6f, 1f);

        /// <summary>
        /// The picture's name (without its "_0_EN" ending) and the UI sheet's
        /// key for its word.
        /// </summary>
        private readonly Dictionary<string, string> Keys =
            new Dictionary<string, string>
            {
                ["play"] = "Play",
                ["options"] = "Options",
                ["credits"] = "Credits",
                ["exit"] = "Quit",
                ["continue"] = "Resume",
                ["help"] = "Help",
                ["mainMenu"] = "Main_menu",
                ["controls"] = "Controls",
                ["graphics"] = "Video",
                ["sound"] = "Sound",
            };

        private sealed class Word
        {
            public global::Button Button;
            public tk2dBaseSprite Picture;
            public MeshRenderer PictureRenderer;
            public tk2dTextMesh Text;
        }

        private readonly List<Word> _words = new List<Word>();
        private readonly HashSet<int> _seen = new HashSet<int>();
        private float _nextScan;

        public void Update(MainMenu mm)
        {
            if (Time.unscaledTime >= _nextScan)
            {
                _nextScan = Time.unscaledTime + 1f;
                Scan(mm);
            }

            for (int i = _words.Count - 1; i >= 0; i--)
            {
                var w = _words[i];
                if (w.Button == null || w.Text == null)
                {
                    _words.RemoveAt(i);
                    continue;
                }

                if (w.PictureRenderer != null && w.PictureRenderer.enabled)
                {
                    w.PictureRenderer.enabled = false;
                }

                Color c;
                if (w.Button.rolledOver)
                {
                    c = Color.white;
                }
                else
                {
                    c = Grey;
                }

                if (w.Picture != null)
                {
                    c.a = w.Picture.color.a;
                }
                else
                {
                    c.a = 1f;
                }

                if (w.Text.color != c)
                {
                    w.Text.color = c;
                    w.Text.Commit();
                }
            }
        }

        private void Scan(MainMenu mm)
        {
            tk2dFontData font = null;
            if (mm.CurrentVersion != null)
            {
                font = mm.CurrentVersion.font;
            }

            if (font == null)
            {
                return;
            }

            foreach (var b in mm.GetComponentsInChildren<global::Button>(true))
            {
                if (b == null || !_seen.Add(b.GetInstanceID()))
                {
                    continue;
                }

                try
                {
                    Redraw(b, font);
                }
                catch (System.Exception e)
                {
                    Plugin.Log.LogWarning(
                        $"Couldn't redraw the menu's {b.name}: {e.Message}");
                }
            }
        }

        private void Redraw(global::Button b, tk2dFontData font)
        {
            var pic = b.GetComponent<tk2dBaseSprite>();
            if (pic == null || pic.Collection == null
                || !pic.Collection.spriteCollectionName.StartsWith(
                    "MainMenuButtons"))
            {
                return;
            }

            tk2dSpriteDefinition currentSprite = pic.CurrentSprite;
            string sprite = null;
            if (currentSprite != null)
            {
                sprite = currentSprite.name;
            }

            if (sprite == null)
            {
                sprite = "";
            }

            int cut = sprite.IndexOf('_');
            if (cut <= 0
                || !Keys.TryGetValue(sprite.Substring(0, cut), out var key))
            {
                return;
            }

            string word = global::Language.Get(key, "UI");
            if (string.IsNullOrEmpty(word))
            {
                return;
            }

            word = word.ToUpper();
            if (!HasLetters(font, word))
            {
                return;
            }

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
            if (pr != null && tr != null)
            {
                tr.sortingLayerID = pr.sortingLayerID;
                tr.sortingOrder = pr.sortingOrder;
                pr.enabled = false;
            }

            _words.Add(new Word
            {
                Button = b,
                Picture = pic,
                PictureRenderer = pr,
                Text = text
            });
        }

        private bool HasLetters(tk2dFontData font, string word)
        {
            foreach (char ch in word)
            {
                if (ch == ' ')
                {
                    continue;
                }

                bool condition;
                if (font.useDictionary)
                {
                    condition =
                        font.charDict == null || !font.charDict.ContainsKey(ch);
                }
                else
                {
                    condition = font.chars == null || ch >= font.chars.Length
                        || font.chars[ch] == null
                        || font.chars[ch].advance <= 0f;
                }

                if (condition)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
