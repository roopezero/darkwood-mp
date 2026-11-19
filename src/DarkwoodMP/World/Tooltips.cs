using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Descriptions the game leaves out. About two thirds of the items in ItemsDatabase have an empty or missing
    /// "Items/&lt;type&gt;_desc" entry in every language, so their popup shows only a name; and no map location has a
    /// description at all. The mod carries its own English lines for those (the embedded Tooltips.tsv, made by
    /// tools/gen_tooltips.py from a dump of the game's item data) and fills them in only where the game has nothing:
    /// an item description the game has is never replaced.
    ///
    /// Tooltips.tsv lines (tab separated, "\n" written as a backslash and n):
    ///   item KEY DRAFT TEXT   a description for Items/KEY_desc
    ///   loc  KEY DRAFT TEXT   a line for the map element KEY, shown above the game's "you are here" line
    ///   skip KEY REASON       an item that needs no text (journal items, which have their own page, and dev items)
    /// DRAFT is 1 when the text isn't taken from the game's data and waits for review; it isn't used in the game.
    /// </summary>
    public static class Tooltips
    {
        private const string ItemsSheet = "Items";
        private const string DescEnd = "_desc";

        /// <summary>Items/KEY_desc -> text.</summary>
        internal static readonly Dictionary<string, string> Items = new Dictionary<string, string>(StringComparer.Ordinal);
        /// <summary>Map element name -> text.</summary>
        internal static readonly Dictionary<string, string> Locations = new Dictionary<string, string>(StringComparer.Ordinal);
        /// <summary>Item types that need no text, with why.</summary>
        internal static readonly Dictionary<string, string> Skip = new Dictionary<string, string>(StringComparer.Ordinal);

        static Tooltips()
        {
            Load();
        }

        private static void Load()
        {
            try
            {
                using (var s = typeof(Tooltips).Assembly.GetManifestResourceStream("DarkwoodMP.Tooltips.tsv"))
                {
                    if (s == null)
                    {
                        Plugin.Log.LogWarning("[tooltips] no Tooltips.tsv in the mod");
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
                                Items[f[1] + DescEnd] = f[3].Replace("\\n", "\n");
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
                Plugin.Log.LogWarning($"[tooltips] couldn't read Tooltips.tsv: {e.Message}");
            }
        }

        /// <summary>The mod's line for an item description key ("type_desc"), or null.</summary>
        internal static string ItemText(string descKey)
        {
            if (descKey != null && Items.TryGetValue(descKey, out var t))
            {
                return Resolve(t);
            }
            return null;
        }

        // Names in the lines are the game's own, in the current language: "{item:TYPE}" is that item's name and
        // "{ui:KEY}" the UI sheet's KEY (a location's name). Each line is resolved once per language.
        private static readonly Dictionary<string, string> _resolved = new Dictionary<string, string>(StringComparer.Ordinal);
        private static LanguageCode _resolvedFor;

        private static string Resolve(string text)
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
                int a = text.IndexOf('{', i), b = a < 0 ? -1 : text.IndexOf('}', a);
                if (a < 0 || b < 0)
                {
                    sb.Append(text, i, text.Length - i);
                    break;
                }
                sb.Append(text, i, a - i);
                string tag = text.Substring(a + 1, b - a - 1);
                if (tag.StartsWith("item:", StringComparison.Ordinal))
                {
                    sb.Append(global::Language.Get(tag.Substring(5) + "_name", ItemsSheet).Trim());
                }
                else if (tag.StartsWith("ui:", StringComparison.Ordinal))
                {
                    sb.Append(global::Language.Get(tag.Substring(3), "UI").Trim());
                }
                else
                {
                    sb.Append(text, a, b - a + 1);
                }
                i = b + 1;
            }
            return _resolved[text] = sb.ToString();
        }

        /// <summary>The mod's line for a map element (its "_done" form too, as Map.getElement matches both), or null.</summary>
        internal static string LocationText(string element)
        {
            if (string.IsNullOrEmpty(element))
            {
                return null;
            }
            if (Locations.TryGetValue(element, out var t))
            {
                return Resolve(t);
            }
            if (element.EndsWith("_done", StringComparison.Ordinal) && Locations.TryGetValue(element.Substring(0, element.Length - 5), out t))
            {
                return Resolve(t);
            }
            return null;
        }

        // Language.Get and HasKey run for every text the game shows, many times a frame: the sheet is checked first
        // (a reference compare for the game's own "Items" literal, then the string), then the key's end, and only
        // then the table.
        private static bool IsItemDesc(string key, string sheet) =>
            key != null && sheet != null && (ReferenceEquals(sheet, ItemsSheet) || sheet == ItemsSheet)
            && key.EndsWith(DescEnd, StringComparison.Ordinal);

        [HarmonyPatch(typeof(global::Language), nameof(global::Language.Get), new[] { typeof(string), typeof(string) })]
        private static class GetPatch
        {
            // Language.Get gives "" for an empty entry and the key itself for a missing one.
            private static void Postfix(string key, string sheetTitle, ref string __result)
            {
                if (!IsItemDesc(key, sheetTitle))
                {
                    return;
                }
                if (!string.IsNullOrEmpty(__result) && !ReferenceEquals(__result, key) && __result != key)
                {
                    return;
                }
                if (ItemText(key) is string t)
                {
                    __result = t;
                }
            }
        }

        [HarmonyPatch(typeof(global::Language), nameof(global::Language.HasKey), new[] { typeof(string), typeof(string) })]
        private static class HasKeyPatch
        {
            private static void Postfix(string key, string sheetTitle, ref bool __result)
            {
                if (__result || !IsItemDesc(key, sheetTitle))
                {
                    return;
                }
                if (Items.ContainsKey(key))
                {
                    __result = true;
                }
            }
        }

        /// <summary>The map's popup for a location (Map.Update, every frame the mouse is on one): the mod's line
        /// first, then the game's own text ("You are here") if it gave one.</summary>
        [HarmonyPatch(typeof(ItemPopup), nameof(ItemPopup.show), new[] { typeof(string), typeof(Vector2), typeof(string) })]
        private static class MapPopupPatch
        {
            private static void Prefix(string _name, ref string descText)
            {
                if (LocationText(_name) is string t)
                {
                    if (string.IsNullOrEmpty(descText))
                    {
                        descText = t;
                    }
                    else
                    {
                        descText = t + "\n" + descText;
                    }
                }
            }
        }

        // ---- Tests ----

        /// <summary>
        /// "--dwmp-test-tooltips DIR" (once the world is ready): writes DIR/tooltips_dump.json (every ItemsDatabase item's
        /// data fields and recipes, every map element; tools/gen_tooltips.py reads it), then checks that every item
        /// has a description (the game's or the mod's) or is on the skip list, that every selectable map element has
        /// a line, and that Tubular magazine's inventory popup shows a description. "[tooltips] PASS/FAIL" lines,
        /// then "[tooltips] done".
        /// </summary>
        internal static IEnumerator Test(string dir)
        {
            while (!WorldShare.WorldReady)
            {
                yield return null;
            }
            yield return new WaitForSeconds(2f);
            int fails = 0;
            void Fail(string what)
            {
                fails++;
                Plugin.Log.LogInfo($"[tooltips] FAIL {what}");
            }
            Plugin.Log.LogInfo($"[tooltips] table: {Items.Count} items, {Skip.Count} skipped, {Locations.Count} locations");

            var idb = Singleton<ItemsDatabase>.Instance;
            var map = Singleton<Map>.Instance;
            if (idb == null || map == null)
            {
                Fail("no ItemsDatabase or Map");
                Plugin.Log.LogInfo("[tooltips] done: failed");
                yield break;
            }
            try
            {
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "tooltips_dump.json"), Dump(idb, map).ToString());
                Plugin.Log.LogInfo($"[tooltips] dump written to {Path.Combine(dir, "tooltips_dump.json")}");
            }
            catch (Exception e)
            {
                Fail($"dump: {e}");
            }

            int game = 0, mod = 0, skipped = 0;
            var missing = new List<string>();
            foreach (var kv in idb.itemsDict)
            {
                string k = kv.Key, raw = RawDesc(k);
                if (!string.IsNullOrEmpty(raw))
                {
                    game++;
                    continue;
                }
                if (Skip.ContainsKey(k))
                {
                    skipped++;
                    continue;
                }
                // Through the game's own calls, as the popup does.
                if (global::Language.HasKey(k + DescEnd, ItemsSheet) && !string.IsNullOrEmpty(global::Language.Get(k + DescEnd, ItemsSheet)))
                {
                    mod++;
                }
                else
                {
                    missing.Add(k);
                }
            }
            if (missing.Count > 0)
            {
                Fail($"items without a description: {string.Join(", ", missing)}");
            }
            else
            {
                Plugin.Log.LogInfo($"[tooltips] PASS items: {game} the game's, {mod} the mod's, {skipped} skipped, none missing");
            }

            // A description the game has must come through untouched.
            foreach (var kv in idb.itemsDict)
            {
                string raw = RawDesc(kv.Key);
                if (string.IsNullOrEmpty(raw))
                {
                    continue;
                }
                if (global::Language.Get(kv.Key + DescEnd, ItemsSheet) != raw)
                {
                    Fail($"the game's description of {kv.Key} was changed");
                }
            }

            var noLine = new SortedSet<string>();
            int locs = 0;
            foreach (var e in MapElements(map))
            {
                if (!e.selectable || e.isDeathDrop || e.isWorldChunk || string.IsNullOrEmpty(e.elementName))
                {
                    continue;
                }
                locs++;
                if (LocationText(e.elementName) == null)
                {
                    noLine.Add(e.elementName);
                }
            }
            if (noLine.Count > 0)
            {
                Fail($"map locations without a line: {string.Join(", ", noLine)}");
            }
            else
            {
                Plugin.Log.LogInfo($"[tooltips] PASS map: {locs} selectable locations, each with a line");
            }

            // Tubular magazine's popup, as the inventory shows it.
            string popupText = null;
            try
            {
                popupText = PopupDescription("firecraft_tubularMagazine");
            }
            catch (Exception e)
            {
                Fail($"popup: {e.Message}");
            }
            if (!string.IsNullOrEmpty(popupText))
            {
                Plugin.Log.LogInfo($"[tooltips] PASS Tubular magazine's popup: \"{popupText}\"");
            }
            else
            {
                Fail("Tubular magazine's popup has no description");
            }

            // And a location's popup keeps the game's own line under the mod's.
            try
            {
                var popup = Singleton<InventoryController>.Instance.itemPopup;
                foreach (var e in MapElements(map))
                {
                    if (!e.selectable || LocationText(e.elementName) == null)
                    {
                        continue;
                    }
                    popup.show(e.elementName, Vector2.zero, "GAME");
                    var txt = Traverse.Create(popup).Field("descriptionText").GetValue<tk2dTextMesh>()?.text;
                    popup.hide();
                    if (txt != null && txt.StartsWith(LocationText(e.elementName)) && txt.EndsWith("\nGAME"))
                    {
                        Plugin.Log.LogInfo($"[tooltips] PASS map popup of {e.elementName}: the mod's line, then the game's");
                    }
                    else
                    {
                        Fail($"map popup of {e.elementName}: \"{txt}\"");
                    }
                    break;
                }
            }
            catch (Exception e)
            {
                Fail($"map popup: {e.Message}");
            }

            string data;
            if (fails == 0)
            {
                data = "[tooltips] done: all passed";
            }
            else
            {
                data = $"[tooltips] done: {fails} failed";
            }
            Plugin.Log.LogInfo(data);
        }

        /// <summary>The game's own text for an item's description, without the mod (null if there's no entry).</summary>
        private static string RawDesc(string type)
        {
            var sheet = global::Language.GetAllKeysForSheet(ItemsSheet);
            if (sheet != null && sheet.TryGetValue(type + DescEnd, out var v))
            {
                return v;
            }
            return null;
        }

        /// <summary>Puts one of the item in a free backpack slot, shows its popup as the inventory does, and takes it
        /// out again. Returns the popup's description.</summary>
        private static string PopupDescription(string type)
        {
            var inv = Player.Instance.Inventory;
            var slot = inv.slots.Find(s => InvItemClass.isNull(s.invItem));
            if (slot == null)
            {
                throw new InvalidOperationException("no free backpack slot");
            }
            slot.inventory = inv;
            slot.createItem(type, 1);
            var popup = Singleton<InventoryController>.Instance.itemPopup;
            try
            {
                popup.show(slot, Inventory.InvType.playerInv);
                return popup.itemDescription;
            }
            finally
            {
                popup.hide();
                slot.removeItem();
            }
        }

        private static IEnumerable<MapElement> MapElements(Map map)
        {
            var seen = new HashSet<MapElement>();
            foreach (var t in map.types)
            {
                if (t?.elements != null)
                {
                    foreach (var e in t.elements)
                    {
                        if (e != null && seen.Add(e))
                        {
                            yield return e;
                        }
                    }
                }
            }
            // Elements of the scene that aren't on a map yet (not initialised while switched off).
            foreach (var e in Resources.FindObjectsOfTypeAll<MapElement>())
            {
                if (e != null && e.gameObject.scene.IsValid() && seen.Add(e))
                {
                    yield return e;
                }
            }
        }

        private static JObject Dump(ItemsDatabase idb, Map map)
        {
            var made = new Dictionary<string, JArray>();
            var usedIn = new Dictionary<string, JArray>();
            foreach (var kv in idb.recipesDict)
            {
                CraftingRecipes rec = null;
                try
                {
                    rec = idb.getRecipes(kv.Key, instantiate: false);
                }
                catch
                {
                }
                if (rec?.recipes == null)
                {
                    continue;
                }
                foreach (var r in rec.recipes)
                {
                    var req = new JArray();
                    foreach (var q in r.requirements)
                    {
                        if (q?.item == null)
                        {
                            continue;
                        }
                        req.Add(new JObject { ["type"] = q.item.type, ["amount"] = q.amount, ["durability"] = q.durabilityAmount });
                        if (!usedIn.TryGetValue(q.item.type, out var u))
                        {
                            usedIn[q.item.type] = u = new JArray();
                        }
                        if (!u.Contains(kv.Key))
                        {
                            u.Add(kv.Key);
                        }
                    }
                    if (!made.TryGetValue(kv.Key, out var m))
                    {
                        made[kv.Key] = m = new JArray();
                    }
                    m.Add(new JObject { ["amount"] = r.produceAmount, ["needs"] = req, ["craftTime"] = rec.craftTime });
                }
            }

            var items = new JArray();
            foreach (var kv in idb.itemsDict)
            {
                var o = new JObject { ["key"] = kv.Key, ["path"] = kv.Value };
                o["name"] = global::Language.Get(kv.Key + "_name", ItemsSheet);
                o["gameDesc"] = !string.IsNullOrEmpty(RawDesc(kv.Key));
                InvItem it = null;
                try
                {
                    it = idb.getItem(kv.Key, instantiate: false);
                }
                catch
                {
                }
                if (it != null)
                {
                    o["fields"] = Fields(it);
                    var cats = new JArray();
                    foreach (var c in it.categories)
                    {
                        cats.Add(c.ToString());
                    }
                    o["categories"] = cats;
                    var fx = new JArray();
                    foreach (var e in it.effects)
                    {
                        if (e != null)
                        {
                            fx.Add(new JObject { ["type"] = e.type.ToString(), ["duration"] = e.duration, ["modifier"] = e.modifier, ["interval"] = e.interval, ["stopsBleeding"] = e.stopsBleeding, ["stopsPoison"] = e.stopsPoison });
                        }
                    }
                    o["effects"] = fx;
                    var after = new JArray();
                    foreach (var a in it.itemsAfterUsing)
                    {
                        if (a?.item != null)
                        {
                            after.Add(a.item.type);
                        }
                    }
                    o["itemsAfterUsing"] = after;
                    var ups = new JArray();
                    foreach (var u in it.upgrades)
                    {
                        if (u != null)
                        {
                            ups.Add(u.name);
                        }
                    }
                    o["upgrades"] = ups;
                    if (it.rottenItem != null)
                    {
                        o["rottenItem"] = it.rottenItem.type;
                    }
                    else
                    {
                        o["rottenItem"] = (string)null;
                    }
                    o["locations"] = new JArray(it.locations.ToArray());
                    var locUi = new JArray();
                    foreach (var l in it.locations)
                    {
                        locUi.Add(global::Language.Get(l, "UI"));
                    }
                    o["locationsUi"] = locUi;
                }
                if (made.TryGetValue(kv.Key, out var mr))
                {
                    o["madeBy"] = mr;
                }
                if (usedIn.TryGetValue(kv.Key, out var ur))
                {
                    o["usedIn"] = ur;
                }
                items.Add(o);
            }

            var elements = new JArray();
            foreach (var e in MapElements(map))
            {
                var o = new JObject
                {
                    ["elementName"] = e.elementName,
                    ["object"] = e.gameObject.name,
                    ["selectable"] = e.selectable,
                    ["isWorldChunk"] = e.isWorldChunk,
                    ["isDeathDrop"] = e.isDeathDrop,
                    ["isOnMap"] = e.isOnMap,
                    ["ui"] = string.IsNullOrEmpty(e.elementName) ? "" : global::Language.Get(e.elementName, "UI"),
                    ["x"] = e.transform.position.x,
                    ["z"] = e.transform.position.z,
                };
                string type = null;
                foreach (var t in map.types)
                {
                    if (t?.elements != null && t.elements.Contains(e))
                    {
                        type = t.name;
                        break;
                    }
                }
                o["mapType"] = type;
                var loc = e.GetComponent<Location>();
                if (loc != null)
                {
                    o["location"] = Fields(loc);
                }
                try
                {
                    var b = Biome.getBiomeAtPos(e.transform.position);
                    if (b != null)
                    {
                        o["biome"] = b.type.ToString();
                    }
                    else
                    {
                        o["biome"] = (string)null;
                    }
                }
                catch
                {
                }
                elements.Add(o);
            }
            return new JObject { ["items"] = items, ["mapElements"] = elements };
        }

        /// <summary>A component's public fields: numbers, text and enums as they are, other Unity objects as their
        /// name (or null), lists as their count.</summary>
        private static JObject Fields(object c)
        {
            var o = new JObject();
            foreach (var f in c.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                object v;
                try
                {
                    v = f.GetValue(c);
                }
                catch
                {
                    continue;
                }
                var ft = f.FieldType;
                if (v == null)
                {
                    o[f.Name] = null;
                    continue;
                }
                if (ft.IsEnum)
                {
                    o[f.Name] = v.ToString();
                }
                else if (ft.IsPrimitive || ft == typeof(string))
                {
                    o[f.Name] = JToken.FromObject(v);
                }
                else if (v is UnityEngine.Object uo)
                {
                    if (uo != null)
                    {
                        o[f.Name] = uo.name;
                    }
                    else
                    {
                        o[f.Name] = (string)null;
                    }
                }
                else if (v is ICollection col)
                {
                    o[f.Name + "#"] = col.Count;
                }
            }
            return o;
        }
    }
}
