using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DarkwoodMP.World.Texts
{
    /// <summary>
    /// Descriptions the game leaves out. About two thirds of the items in
    /// ItemsDatabase have an empty or missing "Items/&lt;type&gt;_desc" entry
    /// in every language, so their popup shows only a name; and no map location
    /// has a description at all. The mod carries its own English lines for
    /// those (the embedded Tooltips.tsv, made by tools/gen_tooltips.py from a
    /// dump of the game's item data) and fills them in only where the game has
    /// nothing: an item description the game has is never replaced.
    ///
    /// Tooltips.tsv lines (tab separated, "\n" written as a backslash and n):
    ///   item KEY DRAFT TEXT   a description for Items/KEY_desc
    ///   loc  KEY DRAFT TEXT   a line for the map element KEY, shown above the
    ///                         game's "you are here" line
    ///   skip KEY REASON       an item that needs no text (journal items, which
    ///                         have their own page, and dev items)
    /// DRAFT is 1 when the text isn't taken from the game's data and waits for
    /// review; it isn't used in the game.
    /// </summary>
    public sealed partial class Tooltips
    {
        /// The one ModHost keeps, for the patches.
        internal static Tooltips Current => Plugin.Instance.Host.Tooltips;

        private const string ItemsSheet = "Items";
        private const string DescEnd = "_desc";

        /// <summary>Items/KEY_desc -> text.</summary>
        internal readonly Dictionary<string, string> Items =
            new Dictionary<string, string>(StringComparer.Ordinal);
        /// <summary>Map element name -> text.</summary>
        internal readonly Dictionary<string, string> Locations =
            new Dictionary<string, string>(StringComparer.Ordinal);
        /// <summary>Item types that need no text, with why.</summary>
        internal readonly Dictionary<string, string> Skip =
            new Dictionary<string, string>(StringComparer.Ordinal);

        public Tooltips()
        {
            Load();
        }

        private void Load()
        {
            try
            {
                using (var s =
                    typeof(Tooltips).Assembly.GetManifestResourceStream(
                        "DarkwoodMP.Tooltips.tsv"))
                {
                    if (s == null)
                    {
                        Plugin.Log.LogWarning(
                            "[tooltips] no Tooltips.tsv in the mod");
                        return;
                    }

                    using (var r = new StreamReader(s))
                    {
                        string line;
                        while ((line = r.ReadLine()) != null)
                        {
                            if (line.Length == 0 || line[0] == '#')
                            {
                                continue;
                            }

                            var f = line.Split('\t');
                            if (f[0] == "skip" && f.Length >= 2)
                            {
                                if (f.Length > 2)
                                {
                                    Skip[f[1]] = f[2];
                                }
                                else
                                {
                                    Skip[f[1]] = "";
                                }
                            }
                            else if (f.Length >= 4 && f[0] == "item")
                            {
                                Items[f[1] + DescEnd] =
                                    f[3].Replace("\\n", "\n");
                            }
                            else if (f.Length >= 4 && f[0] == "loc")
                            {
                                Locations[f[1]] = f[3].Replace("\\n", "\n");
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning(
                    $"[tooltips] couldn't read Tooltips.tsv: {e.Message}");
            }
        }

        /// <summary>
        /// The mod's line for an item description key ("type_desc"), or null.
        /// </summary>
        internal string ItemText(string descKey)
        {
            if (descKey != null && Items.TryGetValue(descKey, out var t))
            {
                return Resolve(t);
            }

            return null;
        }

        // Names in the lines are the game's own, in the current language:
        // "{item:TYPE}" is that item's name and "{ui:KEY}" the UI sheet's KEY
        // (a location's name). Each line is resolved once per language.
        private readonly Dictionary<string, string> _resolved =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private LanguageCode _resolvedFor;

        private string Resolve(string text)
        {
            if (text.IndexOf('{') < 0)
            {
                return text;
            }

            var lang = global::Language.CurrentLanguage();
            if (lang != _resolvedFor)
            {
                _resolved.Clear();
                _resolvedFor = lang;
            }

            if (_resolved.TryGetValue(text, out var done))
            {
                return done;
            }

            var sb = new System.Text.StringBuilder();
            int i = 0;
            while (i < text.Length)
            {
                int a = text.IndexOf('{', i);
                int b;
                if (a < 0)
                {
                    b = -1;
                }
                else
                {
                    b = text.IndexOf('}', a);
                }

                if (a < 0 || b < 0)
                {
                    sb.Append(text, i, text.Length - i);
                    break;
                }

                sb.Append(text, i, a - i);
                string tag = text.Substring(a + 1, b - a - 1);
                if (tag.StartsWith("item:", StringComparison.Ordinal))
                {
                    sb.Append(
                        global::Language.Get(
                            tag.Substring(5) + "_name", ItemsSheet).Trim());
                }
                else if (tag.StartsWith("ui:", StringComparison.Ordinal))
                {
                    sb.Append(
                        global::Language.Get(tag.Substring(3), "UI").Trim());
                }
                else
                {
                    sb.Append(text, a, b - a + 1);
                }

                i = b + 1;
            }

            return _resolved[text] = sb.ToString();
        }

        /// <summary>
        /// The mod's line for a map element (its "_done" form too, as
        /// Map.getElement matches both), or null.
        /// </summary>
        internal string LocationText(string element)
        {
            if (string.IsNullOrEmpty(element))
            {
                return null;
            }

            if (Locations.TryGetValue(element, out var t))
            {
                return Resolve(t);
            }

            if (element.EndsWith("_done", StringComparison.Ordinal)
                && Locations.TryGetValue(
                    element.Substring(0, element.Length - 5), out t))
            {
                return Resolve(t);
            }

            return null;
        }

        // Language.Get and HasKey run for every text the game shows, many times
        // a frame: the sheet is checked first (a reference compare for the
        // game's own "Items" literal, then the string), then the key's end, and
        // only then the table.
        private bool IsItemDesc(string key, string sheet) =>
            key != null && sheet != null
                && (ReferenceEquals(sheet, ItemsSheet) || sheet == ItemsSheet)
                && key.EndsWith(DescEnd, StringComparison.Ordinal);

        // ---- what the patches hand over (TooltipPatches) ----

        /// Language.Get gave "" for an empty entry or the key itself for a
        /// missing one: an item's description is the mod's then.
        internal void Got(string key, string sheet, ref string text)
        {
            if (!IsItemDesc(key, sheet))
            {
                return;
            }

            if (!string.IsNullOrEmpty(text)
                && !ReferenceEquals(text, key) && text != key)
            {
                return;
            }

            if (ItemText(key) is string t)
            {
                text = t;
            }
        }

        /// Language.HasKey: an item's description the mod has is there.
        internal void HadKey(string key, string sheet, ref bool has)
        {
            if (has || !IsItemDesc(key, sheet))
            {
                return;
            }

            if (Items.ContainsKey(key))
            {
                has = true;
            }
        }

        /// The map's popup for a location: the mod's line first, then the
        /// game's own text ("You are here") if it gave one.
        internal void PopupShowing(string name, ref string desc)
        {
            if (LocationText(name) is string t)
            {
                if (string.IsNullOrEmpty(desc))
                {
                    desc = t;
                }
                else
                {
                    desc = t + "\n" + desc;
                }
            }
        }
    }
}
