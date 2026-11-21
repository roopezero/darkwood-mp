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
    /// Tooltips: the dump and check of every item's and place's text.
    public sealed partial class Tooltips
    {
        // ---- Tests ----

        /// <summary>
        /// "--dwmp-test-tooltips DIR" (once the world is ready): writes
        /// DIR/tooltips_dump.json (every ItemsDatabase item's data fields and
        /// recipes, every map element; tools/gen_tooltips.py reads it), then
        /// checks that every item has a description (the game's or the mod's)
        /// or is on the skip list, that every selectable map element has a
        /// line, and that Tubular magazine's inventory popup shows a
        /// description. "[tooltips] PASS/FAIL" lines, then "[tooltips] done".
        /// </summary>
        internal IEnumerator Test(string dir)
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

            Plugin.Log.LogInfo(
                $"[tooltips] table: {Items.Count} items, {Skip.Count} skipped, "
                    + $"{Locations.Count} locations");

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
                File.WriteAllText(
                    Path.Combine(dir, "tooltips_dump.json"),
                    Dump(idb, map).ToString());
                Plugin.Log.LogInfo(
                    "[tooltips] dump written to "
                        + $"{Path.Combine(dir, "tooltips_dump.json")}");
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
                if (global::Language.HasKey(k + DescEnd, ItemsSheet)
                    && !string.IsNullOrEmpty(
                        global::Language.Get(k + DescEnd, ItemsSheet)))
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
                Fail(
                    "items without a description: "
                        + $"{string.Join(", ", missing)}");
            }
            else
            {
                Plugin.Log.LogInfo(
                    $"[tooltips] PASS items: {game} the game's, {mod} the "
                        + $"mod's, {skipped} skipped, none missing");
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
                if (!e.selectable || e.isDeathDrop || e.isWorldChunk
                    || string.IsNullOrEmpty(e.elementName))
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
                Fail(
                    "map locations without a line: "
                        + $"{string.Join(", ", noLine)}");
            }
            else
            {
                Plugin.Log.LogInfo(
                    $"[tooltips] PASS map: {locs} selectable locations, each "
                        + "with a line");
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
                Plugin.Log.LogInfo(
                    "[tooltips] PASS Tubular magazine's popup: "
                        + $"\"{popupText}\"");
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
                    tk2dTextMesh descriptionText = Traverse.Create(popup)
                        .Field("descriptionText").GetValue<tk2dTextMesh>();
                    string txt = null;
                    if (descriptionText != null)
                    {
                        txt = descriptionText.text;
                    }

                    popup.hide();
                    if (txt != null
                        && txt.StartsWith(LocationText(e.elementName))
                        && txt.EndsWith("\nGAME"))
                    {
                        Plugin.Log.LogInfo(
                            $"[tooltips] PASS map popup of {e.elementName}: "
                                + "the mod's line, then the game's");
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

        /// <summary>
        /// The game's own text for an item's description, without the mod (null
        /// if there's no entry).
        /// </summary>
        private string RawDesc(string type)
        {
            var sheet = global::Language.GetAllKeysForSheet(ItemsSheet);
            if (sheet != null && sheet.TryGetValue(type + DescEnd, out var v))
            {
                return v;
            }

            return null;
        }

        /// <summary>Puts one of the item in a free backpack slot, shows its
        /// popup as the inventory does, and takes it out again. Returns the
        /// popup's description.</summary>
        private string PopupDescription(string type)
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

        private IEnumerable<MapElement> MapElements(Map map)
        {
            var seen = new HashSet<MapElement>();
            foreach (var t in map.types)
            {
                if (t != null && t.elements != null)
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

            // Elements of the scene that aren't on a map yet (not initialised
            // while switched off).
            foreach (var e in Resources.FindObjectsOfTypeAll<MapElement>())
            {
                if (e != null && e.gameObject.scene.IsValid() && seen.Add(e))
                {
                    yield return e;
                }
            }
        }

        private JObject Dump(ItemsDatabase idb, Map map)
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

                if (rec == null || rec.recipes == null)
                {
                    continue;
                }

                foreach (var r in rec.recipes)
                {
                    var req = new JArray();
                    foreach (var q in r.requirements)
                    {
                        if (q == null || q.item == null)
                        {
                            continue;
                        }

                        req.Add(new JObject
                        {
                            ["type"] = q.item.type,
                            ["amount"] = q.amount,
                            ["durability"] = q.durabilityAmount
                        });

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

                    m.Add(new JObject
                    {
                        ["amount"] = r.produceAmount,
                        ["needs"] = req,
                        ["craftTime"] = rec.craftTime
                    });
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
                            fx.Add(new JObject
                            {
                                ["type"] = e.type.ToString(),
                                ["duration"] = e.duration,
                                ["modifier"] = e.modifier,
                                ["interval"] = e.interval,
                                ["stopsBleeding"] = e.stopsBleeding,
                                ["stopsPoison"] = e.stopsPoison
                            });
                        }
                    }

                    o["effects"] = fx;
                    var after = new JArray();
                    foreach (var a in it.itemsAfterUsing)
                    {
                        if (a != null && a.item != null)
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
                };

                string ui;
                if (string.IsNullOrEmpty(e.elementName))
                {
                    ui = "";
                }
                else
                {
                    ui = global::Language.Get(e.elementName, "UI");
                }

                o["ui"] = ui;
                o["x"] = e.transform.position.x;
                o["z"] = e.transform.position.z;
                string type = null;
                foreach (var t in map.types)
                {
                    if (t != null && t.elements != null
                        && t.elements.Contains(e))
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

            return new JObject
            {
                ["items"] = items,
                ["mapElements"] = elements
            };
        }

        /// <summary>A component's public fields: numbers, text and enums as
        /// they are, other Unity objects as their name (or null), lists as
        /// their count.</summary>
        private JObject Fields(object c)
        {
            var o = new JObject();
            foreach (var f in c.GetType().GetFields(
                BindingFlags.Public | BindingFlags.Instance))
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
