using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using DarkwoodMP.Combat;
using DarkwoodMP.Net;
using UnityEngine;

namespace DarkwoodMP.World
{
    /// <summary>
    /// A JSON dump of the shared world as this game has it, for the two-game test (tools/run-test.sh map): the
    /// host and the client each write one ("dump PATH" on the test control), and tools/tests/mapcompare.py
    /// compares them. Every entry is keyed by its ObjRef string ("Saved:123", "Spawned:...", "Placed:..."), the
    /// name both games use for it on the wire, so the same thing lines up in both dumps. Each also carries
    /// "name" and "pos" (x, z rounded), which the comparer uses to pair Placed entries whose key moved a little.
    ///
    /// The dump is a list of sections. To add one: <c>StateDump.AddSection("name", ctx => { ... })</c>, where the
    /// function finds its objects (<c>ctx.Near(pos)</c> keeps to the test area) and adds them with
    /// <c>var e = ctx.Add(gameObject); e["field"] = value;</c> (values: bool, int, long, float, string, a
    /// <c>List&lt;object&gt;</c> or a <c>Dictionary&lt;string, object&gt;</c>), or <c>ctx.Set(key, entry)</c> for something
    /// with no object. Floats are rounded where they're written: use <c>StateDump.R(x, step)</c>. Per-player things
    /// that needn't match (what the local player holds) go under "local" (<c>AddLocal</c>), which the comparer
    /// prints but doesn't compare.
    ///
    /// Only what lies within the radius of the test area's centre is dumped (TestWorld's area, or where the host
    /// player stands): the client only knows the enemies near it, and only loads the world around it.
    /// </summary>
    public static class StateDump
    {
        /// <summary>Every T in the loaded scenes, inactive ones included. The game switches off things far from
        /// its player, and that differs between games, so FindObjectsOfType (active only) would make the
        /// dumps differ for objects that are the same in both.</summary>
        public static IEnumerable<T> All<T>() where T : Component =>
            Resources.FindObjectsOfTypeAll<T>().Where(c => c != null && c.gameObject.scene.IsValid());

        public sealed class Ctx
        {
            public Vector3 Centre;
            public float Radius;
            public readonly SortedDictionary<string, object> Entries = new SortedDictionary<string, object>(StringComparer.Ordinal);

            public bool Near(Vector3 p) => Flat(p - Centre) <= Radius;

            /// <summary>An entry for this object, keyed by its ObjRef; "name" and "pos" are filled in.</summary>
            public Dictionary<string, object> Add(GameObject go)
            {
                var e = new Dictionary<string, object>
                {
                    ["name"] = Clean(go.name),
                    ["pos"] = Pos(go.transform.position),
                };
                Set(ObjRef.Of(go).ToString(), e);
                return e;
            }

            /// <summary>An entry under this key (made unique with "#2", "#3"... if it's taken).</summary>
            public void Set(string key, object entry)
            {
                string k = key;
                int n = 1;
                while (Entries.ContainsKey(k))
                {
                    k = $"{key}#{++n}";
                }
                Entries[k] = entry;
            }
        }

        private sealed class Section
        {
            public string Name;
            public Action<Ctx> Fill;
        }
        private static readonly List<Section> _sections = new List<Section>();
        private static readonly List<Section> _local = new List<Section>();

        /// <summary>Add a section of the shared world (compared between the games).</summary>
        public static void AddSection(string name, Action<Ctx> fill) => _sections.Add(new Section { Name = name, Fill = fill });
        /// <summary>Add a section of per-player things (reported, not compared).</summary>
        public static void AddLocal(string name, Action<Ctx> fill) => _local.Add(new Section { Name = name, Fill = fill });

        public static float Flat(Vector3 d) => Mathf.Sqrt(d.x * d.x + d.z * d.z);
        /// <summary>Rounded to a multiple of step (0.25 for hours, 1 for health...).</summary>
        public static double R(float x, float step) => Math.Round(Math.Round(x / step) * step, 3);
        public static List<object> Pos(Vector3 p) => new List<object> { (double)Mathf.Round(p.x), (double)Mathf.Round(p.z) };

        /// <summary>Where the test area is: TestWorld's centre, else where our player stands.</summary>
        public static Vector3 DefaultCentre()
        {
            Vector3? centre = TestWorld.Center();
            if (centre.HasValue)
            {
                return centre.Value;
            }
            if (Player.Instance != null)
            {
                return Player.Instance.transform.position;
            }
            return Vector3.zero;
        }
        public const float DefaultRadius = 2000f;

        /// <summary>Write the dump to path. centre/radius default to the test area's.</summary>
        public static string Write(string path, Vector3? centre, float radius)
        {
            Vector3 c;
            if (centre.HasValue)
            {
                c = centre.Value;
            }
            else
            {
                c = DefaultCentre();
            }
            string role;
            if (Session.Instance == null)
            {
                role = "alone";
            }
            else if (Session.Instance.IsHost)
            {
                role = "host";
            }
            else
            {
                role = "client";
            }
            var root = new SortedDictionary<string, object>(StringComparer.Ordinal)
            {
                ["role"] = role,
                ["centre"] = Pos(c),
                ["radius"] = (double)radius,
            };
            root["sections"] = Run(_sections, c, radius);
            root["local"] = Run(_local, c, radius);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            var sb = new StringBuilder();
            Json(sb, root, 0);
            sb.Append('\n');
            File.WriteAllText(path, sb.ToString());
            int n = ((SortedDictionary<string, object>)root["sections"]).Values.Sum(s =>
            {
                if (s is SortedDictionary<string, object> d)
                {
                    return d.Count;
                }
                return 0;
            });
            return $"{n} entries";
        }

        private static SortedDictionary<string, object> Run(List<Section> list, Vector3 c, float radius)
        {
            var all = new SortedDictionary<string, object>(StringComparer.Ordinal);
            foreach (var s in list)
            {
                var ctx = new Ctx { Centre = c, Radius = radius };
                try
                {
                    s.Fill(ctx);
                }
                catch (Exception e)
                {
                    ctx.Entries["error"] = e.GetType().Name + ": " + e.Message;
                    Plugin.Log.LogWarning($"[dump] {s.Name}: {e}");
                }
                all[s.Name] = ctx.Entries;
            }
            return all;
        }

        // ---- the sections --------------------------------------------------------------------------------------

        static StateDump()
        {
            AddSection("doors", ctx =>
            {
                foreach (var d in All<Door>())
                {
                    if (!ctx.Near(d.transform.position))
                    {
                        continue;
                    }
                    var l = d.GetComponent<Locked>();
                    var e = ctx.Add(d.gameObject);
                    e["open"] = d.opened;
                    e["locked"] = l != null && l.locked;
                    e["health"] = d.health;
                    e["broken"] = d.destroyed;
                    e["blocked"] = d.blocked;
                    e["leafTurned"] = !d.destroyed && DoorLeaf.Turned(d); // drawn open (Door.open pushes the leaf; lost on a switched-off door)
                    e["barricaded"] = d.barricaded;
                    e["barricadeHealth"] = d.barricadeHealth;
                    e["burning"] = d.GetComponent<Burn>() != null;
                }
            });
            AddSection("windows", ctx =>
            {
                foreach (var w in All<Window>())
                {
                    if (!ctx.Near(w.transform.position))
                    {
                        continue;
                    }
                    var e = ctx.Add(w.gameObject);
                    e["barricaded"] = w.barricaded;
                    e["barricadeHealth"] = w.barricadeHealth;
                    e["burning"] = w.GetComponent<Burn>() != null;
                }
            });
            AddSection("containers", ctx =>
            {
                foreach (var inv in WorldInventories(ctx))
                {
                    if (IsDropped(inv))
                    {
                        continue;
                    }
                    var e = ctx.Add(inv.gameObject);
                    e["workbench"] = inv.isWorkbench;
                    var l = inv.GetComponentInParent<Locked>(true); // switched off here too (far from our player)
                    if (l != null)
                    {
                        e["locked"] = l.locked;
                    }
                    e["slots"] = Slots(inv);
                }
            });
            AddSection("dropped", ctx =>
            {
                foreach (var inv in WorldInventories(ctx))
                {
                    if (!IsDropped(inv))
                    {
                        continue;
                    }
                    var e = ctx.Add(inv.gameObject);
                    e["slots"] = Slots(inv);
                }
            });
            AddSection("items", ctx =>
            {
                // Things with a state of their own: health, broken, switched on, searched, dragged to a spot.
                foreach (var it in All<Item>())
                {
                    if (!ctx.Near(it.transform.position) || it.GetComponent<Door>() != null || it.GetComponent<Window>() != null)
                    {
                        continue;
                    }
                    if (it.GetComponentInParent<Player>() != null || it.GetComponent<Inventory>() is Inventory inv && IsDropped(inv))
                    {
                        continue;
                    }
                    // A client's own copy of a character the host runs (a searchable corpse prop in the shared dream) is
                    // frozen with its renderers and colliders off: nobody sees or searches it; the host's copy is listed.
                    if (it.GetComponent<Character>() is Character ch && NetEnemies.Frozen.Contains(ch) && NetEnemies.Instance != null
                        && !NetEnemies.Instance.NetIdOf(ch, out _))
                    {
                        continue;
                    }
                    var e = ctx.Add(it.gameObject);
                    e["health"] = it.health;
                    e["broken"] = it.destroyed;
                    if (it.switchable)
                    {
                        e["on"] = it.isOn;
                    }
                    if (it.draggable)
                    {
                        e["draggable"] = true;
                    }
                    e["searched"] = it.searched;
                    var l = it.GetComponent<Locked>();
                    if (l != null)
                    {
                        e["locked"] = l.locked;
                    }
                    if (it.GetComponent<Burn>() != null)
                    {
                        e["burning"] = true;
                    }
                }
            });
            AddSection("traps", ctx =>
            {
                // Traps (a trigger with an Item; one that went off loses its Item, so those are kept by name too).
                foreach (var t in All<Trigger>())
                {
                    if (!ctx.Near(t.transform.position) || (t.GetComponent<Item>() == null && !t.setByPlayer && !TrapNames.Contains(Clean(t.name))))
                    {
                        continue;
                    }
                    var e = ctx.Add(t.gameObject);
                    e["armed"] = t.active;
                    e["triggered"] = t.triggered;
                }
            });
            AddSection("power", ctx =>
            {
                foreach (var g in All<Generator>())
                {
                    if (!ctx.Near(g.transform.position))
                    {
                        continue;
                    }
                    var e = ctx.Add(g.gameObject);
                    e["on"] = g.isOn;
                    e["fuel"] = R(g.fuel, 1f);
                    e["saw"] = g.isSaw;
                }
            });
            AddSection("saw", ctx =>
            {
                foreach (var w in All<Saw>())
                {
                    if (!ctx.Near(w.transform.position))
                    {
                        continue;
                    }
                    var e = ctx.Add(w.gameObject);
                    e["fuel"] = R(w.fuel, 1f);
                }
            });
            AddSection("built", ctx =>
            {
                // Things built or repaired (a Constructible removes itself once built, unless it's kept), and the
                // switches shown in their own window.
                foreach (var c in All<Constructible>())
                {
                    if (!ctx.Near(c.transform.position))
                    {
                        continue;
                    }
                    var e = ctx.Add(c.gameObject);
                    e["constructed"] = c.constructed;
                    e["option"] = c.chosenOption;
                }
                foreach (var ii in All<InteractiveItem>())
                {
                    if (!ctx.Near(ii.transform.position))
                    {
                        continue;
                    }
                    var e = ctx.Add(ii.gameObject);
                    e["switchOn"] = ii.isOn;
                }
            });
            AddSection("traders", ctx =>
            {
                // NPCs live in each game separately, so traders go by name (TraderSync), and those from the shared
                // save by name and ref; the reputation is the story's. Other copies can share a name (the morning
                // trader in each hideout); they're numbered in the order of their stock, the same in both games.
                var traders = All<NPC>().Where(n => n != null && n.trader && TraderSync.Inv(n) != null)
                    .Select(n => (n, key: StockKey(TraderSync.Inv(n)), rep: n.reputation))
                    .OrderBy(t => t.n.name, StringComparer.Ordinal).ThenBy(t => t.key, StringComparer.Ordinal).ThenBy(t => t.rep);
                foreach (var (n, _, _) in traders)
                {
                    var ch = n.GetComponent<Character>();
                    // One from the shared save is that one (TraderSync names him by his ref): keyed by it.
                    var who = ObjRef.Of(n.gameObject);
                    string label = "trader:" + n.name;
                    if (who.K == ObjRef.Kind.Saved)
                    {
                        label += "@" + who;
                    }
                    ctx.Set(label, new Dictionary<string, object>
                    {
                        ["stock"] = Slots(TraderSync.Inv(n)),
                        ["reputation"] = n.reputation,
                        ["alive"] = ch == null || ch.alive,
                    });
                }
            });
            AddSection("npcpos", ctx =>
            {
                // Where the NPCs that every game has (a shared save id) stand: the host's place them all (NpcSync).
                foreach (var n in All<NPC>())
                {
                    Character ch = null;
                    if (n != null)
                    {
                        ch = n.GetComponent<Character>();
                    }
                    SaveableObject save = null;
                    if (n != null)
                    {
                        save = n.GetComponent<SaveableObject>();
                    }
                    if (ch == null || !ch.alive || save == null || save.uniqueId <= 0 || save.uniqueId >= SharedIds.ResolveLimit)
                    {
                        continue;
                    }
                    if (!ctx.Near(ch.transform.position) || (NetEnemies.Instance != null && NetEnemies.Instance.NetIdOf(ch, out _)))
                    {
                        continue;
                    }
                    ctx.Add(n.gameObject);
                }
            });
            AddSection("journal", ctx =>
            {
                Journal j = null;
                if (Singleton<UI>.Instance != null)
                {
                    j = Singleton<UI>.Instance.journal;
                }
                if (j == null)
                {
                    return;
                }
                void Keys(string what, IEnumerable<string> keys)
                {
                    var list = keys.Where(k => k != null).OrderBy(k => k, StringComparer.Ordinal).ToList();
                    ctx.Set(what, new Dictionary<string, object> { ["count"] = list.Count, ["hash"] = Hash(list) });
                }
                Keys("entries", j.journalEntriesDict.Keys);
                Keys("notes", j.notesDict.Keys);
                Keys("keys", j.keysDict.Keys);
                Keys("items", j.itemsDict.Keys);
                Keys("places", j.locationsDict.Keys);
            });
            AddSection("enemies", ctx =>
            {
                var ne = NetEnemies.Instance;
                foreach (var c in All<Character>())
                {
                    if (!NetEnemies.Eligible(c) || !ctx.Near(c.transform.position))
                    {
                        continue;
                    }
                    if (ne == null || !ne.NetIdOf(c, out uint id))
                    {
                        continue; // not one of the host's shared enemies
                    }
                    // A living one switched off by the host's culling isn't sent (NetEnemies sends the active ones), so
                    // no client has it; a client's copy of one is hidden and forgotten.
                    if (NetEnemies.InPlay(c) && !c.gameObject.activeInHierarchy && !NetEnemies.ClientMode)
                    {
                        continue;
                    }
                    // A client's copy switched off by its own culling (far from its player, near another player) is still the host's, kept up to date.
                    var e = ctx.Add(c.gameObject);
                    e["alive"] = c.alive;
                    // A hostile NPC's health is the host's (Combat.HostileNpcs): every player's hits add up.
                    if (c.npc != null && c.alive)
                    {
                        e["health"] = Mathf.Round(c.health);
                    }
                    var snd = c.GetComponent<CharacterSounds>();
                    AudioObject loop = null;
                    if (snd != null)
                    {
                        loop = snd.idleAudioObject;
                    }
                    if (loop != null && loop.IsPlaying())
                    {
                        e["loopSound"] = loop.audioID;
                    }
                    else
                    {
                        e["loopSound"] = "";
                    }
                    if (!c.alive)
                    {
                        // A corpse can be searched in every game (the host's loot came with it).
                        e["searchable"] = c.GetComponent<Item>() != null;
                        // A settled corpse is walked through (setDeathCollider makes its body a trigger).
                        e["walkThrough"] = c.collider == null || c.collider.isTrigger || !c.collider.enabled;
                        // What of it cuts the player's view (Player.FOVLight's shadow layers): a client's copy had
                        // colliders switched on that the host's corpse has off or destroyed (a black screen beside it).
                        e["viewBlockers"] = string.Join(", ", NetEnemies.FovBlockers(c, NetEnemies.FovShadowMask()));
                        var inv = c.GetComponent<Inventory>();
                        if (inv != null)
                        {
                            e["loot"] = Slots(inv);
                        }
                    }
                }
            });
            AddSection("story", ctx =>
            {
                var flags = Singleton<Flags>.Instance;
                if (flags == null)
                {
                    return;
                }
                var dict = FlagsDict(flags);
                // Not the flags about this game's own player (FlagsSync.PlayersOwn: where it is, the help popups it
                // has seen): those are under "local", since each game keeps its own.
                var shared = dict.Values.Where(f => f != null && !FlagsSync.PlayersOwn(f.name)).ToList();
                var lines = shared.Select(f => f.name + "=" + f.isTrue).OrderBy(s => s, StringComparer.Ordinal).ToList();
                var section = new Dictionary<string, object> { ["count"] = lines.Count, ["true"] = shared.Count(f => f.isTrue), ["hash"] = Hash(lines) };
                // A short list is given whole, so a difference names the flag.
                if (lines.Count <= 40)
                {
                    section["list"] = string.Join(" ", lines);
                }
                ctx.Set("flags", section);
                var npcs = flags.npcStates.Where(n => n != null).Select(n => $"{n.name}:{n.reputation}:{n.dead}").OrderBy(s => s, StringComparer.Ordinal).ToList();
                ctx.Set("npcs", new Dictionary<string, object> { ["count"] = npcs.Count, ["hash"] = Hash(npcs) });
            });
            AddSection("time", ctx =>
            {
                var ctl = Singleton<Controller>.Instance;
                if (ctl == null)
                {
                    return;
                }
                ctx.Set("clock", new Dictionary<string, object> { ["day"] = ctl.day, ["hour"] = R(ctl.CurrentTime / 60f, 0.25f) });
            });

            AddLocal("where", ctx =>
            {
                var flags = Singleton<Flags>.Instance;
                if (flags == null)
                {
                    return;
                }
                var dict = FlagsDict(flags);
                foreach (var f in dict.Values)
                {
                    if (f != null && FlagsSync.PlayersPlace(f.name))
                    {
                        ctx.Set(f.name, f.isTrue);
                    }
                }
                ctx.Set("place", Locations.Current());
            });
            AddLocal("popups", ctx =>
            {
                // The one-time help popups this game's player has seen (FlagsSync.PlayersPopup): each game's own.
                var flags = Singleton<Flags>.Instance;
                if (flags == null)
                {
                    return;
                }
                foreach (var f in FlagsDict(flags).Values)
                {
                    if (f != null && FlagsSync.PlayersPopup(f.name))
                    {
                        ctx.Set(f.name, f.isTrue);
                    }
                }
            });
            AddLocal("player", ctx =>
            {
                var p = Player.Instance;
                if (p == null)
                {
                    return;
                }
                string holding;
                if (InvItemClass.isNull(p.currentItem))
                {
                    holding = "";
                }
                else
                {
                    holding = p.currentItem.type;
                }
                ctx.Set("player", new Dictionary<string, object>
                {
                    ["pos"] = Pos(p.transform.position),
                    ["health"] = R(p.health, 1f),
                    ["holding"] = holding,
                    ["carrying"] = p.Inventory.getAllItems().Count,
                });
            });
        }

        /// <summary>The world's item inventories in the area: containers, the workbench's storage, dropped items,
        /// corpses; not the player's own bags, hotbar or the UI's.</summary>
        private static IEnumerable<Inventory> WorldInventories(Ctx ctx)
        {
            var p = Player.Instance;
            foreach (var inv in All<Inventory>())
            {
                if (inv.invType != Inventory.InvType.itemInv || !ctx.Near(inv.transform.position))
                {
                    continue;
                }
                if (p != null && (inv == p.Inventory || inv == p.Hotbar || inv.transform.IsChildOf(p.transform)))
                {
                    continue;
                }
                yield return inv;
            }
        }

        private static readonly HashSet<string> TrapNames = new HashSet<string> { "beartrap", "chainTrap", "barrelExploding", "gasBottle_01", "broken_glass" };
        /// <summary>A game object's name without Unity's "(Clone)".</summary>
        internal static string Clean(string n) => n.Replace("(Clone)", "").Trim();

        /// <summary>The story flags that are set (Flags.flagsDict).</summary>
        internal static readonly HarmonyLib.AccessTools.FieldRef<Flags, Dictionary<string, Flags.Flag>> FlagsDict =
            HarmonyLib.AccessTools.FieldRefAccess<Flags, Dictionary<string, Flags.Flag>>("flagsDict");

        private static bool IsDropped(Inventory inv) => inv.name.StartsWith("DroppedItem", StringComparison.Ordinal);

        private static List<object> Slots(Inventory inv)
        {
            var list = new List<object>();
            foreach (var s in inv.slots)
            {
                if (s == null || InvItemClass.isNull(s.invItem))
                {
                    list.Add(null);
                    continue;
                }
                var it = s.invItem;
                // A gun's count is its ammo (InvItemClass.SaveState saves that, not its amount, which means nothing for it).
                int amount;
                if (it.baseClass != null && it.baseClass.hasAmmo)
                {
                    amount = it.ammo;
                }
                else
                {
                    amount = it.amount;
                }
                list.Add(new Dictionary<string, object> { ["type"] = it.type, ["amount"] = amount, ["durability"] = R(it.durability, 0.05f) });
            }
            // Trailing empty slots don't matter (a workbench's level adds some).
            while (list.Count > 0 && list[list.Count - 1] == null)
            {
                list.RemoveAt(list.Count - 1);
            }
            return list;
        }

        /// <summary>A trader's stock as one string ("type x amount" per slot), for ordering traders.</summary>
        private static string StockKey(Inventory inv) =>
            string.Join(",", inv.slots.Select(s =>
            {
                if (s == null || InvItemClass.isNull(s.invItem))
                {
                    return "";
                }
                return s.invItem.type + "x" + s.invItem.amount;
            }));

        private static string Hash(List<string> lines)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(string.Join("\n", lines)))).Replace("-", "").Substring(0, 16).ToLowerInvariant();
            }
        }

        // ---- a small JSON writer (sorted keys, so the dumps diff cleanly) ------------------------------------------

        private static void Json(StringBuilder sb, object v, int indent)
        {
            switch (v)
            {
                case null:
                    sb.Append("null");
                    break;
                case bool b:
                    string literal = "false";
                    if (b)
                    {
                        literal = "true";
                    }
                    sb.Append(literal);
                    break;
                case string s:
                    Str(sb, s);
                    break;
                case int i:
                    sb.Append(i);
                    break;
                case uint u:
                    sb.Append(u);
                    break;
                case long l:
                    sb.Append(l);
                    break;
                case float f:
                    sb.Append(((double)f).ToString("R", CultureInfo.InvariantCulture));
                    break;
                case double d:
                    sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
                    break;
                case System.Collections.IDictionary dict:
                    {
                        var keys = dict.Keys.Cast<string>().OrderBy(k => k, StringComparer.Ordinal).ToList();
                        if (keys.Count == 0)
                        {
                            sb.Append("{}");
                            break;
                        }
                        bool flat = indent >= 3; // entries' fields on one line
                        sb.Append('{');
                        for (int k = 0; k < keys.Count; k++)
                        {
                            if (k > 0)
                            {
                                sb.Append(',');
                            }
                            if (flat)
                            {
                                string space = "";
                                if (k > 0)
                                {
                                    space = " ";
                                }
                                sb.Append(space);
                            }
                            else
                            {
                                sb.Append('\n').Append(' ', 2 * (indent + 1));
                            }
                            Str(sb, keys[k]);
                            sb.Append(": ");
                            Json(sb, dict[keys[k]], indent + 1);
                        }
                        if (!flat)
                        {
                            sb.Append('\n').Append(' ', 2 * indent);
                        }
                        sb.Append('}');
                        break;
                    }
                case System.Collections.IEnumerable list:
                    {
                        sb.Append('[');
                        bool first = true;
                        foreach (var x in list)
                        {
                            if (!first)
                            {
                                sb.Append(", ");
                            }
                            first = false;
                            Json(sb, x, Math.Max(indent + 1, 3));
                        }
                        sb.Append(']');
                        break;
                    }
                default:
                    Str(sb, v.ToString());
                    break;
            }
        }

        private static void Str(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char ch in s)
            {
                if (ch == '"' || ch == '\\')
                {
                    sb.Append('\\').Append(ch);
                }
                else if (ch < 0x20)
                {
                    sb.Append($"\\u{(int)ch:x4}");
                }
                else
                {
                    sb.Append(ch);
                }
            }
            sb.Append('"');
        }
    }
}
