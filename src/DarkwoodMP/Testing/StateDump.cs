using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using DarkwoodMP.Combat;
using DarkwoodMP.Net;
using DarkwoodMP.World.Doors;
using UnityEngine;

namespace DarkwoodMP.World
{
    /// <summary>
    /// A JSON dump of the shared world as this game has it, for the two-game
    /// test (tools/run-test.sh map): the host and the client each write one
    /// ("dump PATH" on the test control), and tools/tests/mapcompare.py
    /// compares them. Every entry is keyed by its ObjRef string ("Saved:123",
    /// "Spawned:...", "Placed:..."), the name both games use for it on the
    /// wire, so the same thing lines up in both dumps. Each also carries "name"
    /// and "pos" (x, z rounded), which the comparer uses to pair Placed entries
    /// whose key moved a little.
    ///
    /// The dump is a list of sections. To add one:
    /// <c>StateDump.AddSection("name", ctx => { ... })</c>, where the function
    /// finds its objects (<c>ctx.Near(pos)</c> keeps to the test area) and adds
    /// them with <c>var e = ctx.Add(gameObject); e["field"] = value;</c>
    /// (values: bool, int, long, float, string, a <c>List&lt;object&gt;</c> or
    /// a <c>Dictionary&lt;string, object&gt;</c>), or
    /// <c>ctx.Set(key, entry)</c> for something with no object. Floats are
    /// rounded where they're written: use <c>StateDump.R(x, step)</c>.
    /// Per-player things that needn't match (what the local player holds) go
    /// under "local" (<c>AddLocal</c>), which the comparer prints but doesn't
    /// compare.
    ///
    /// Only what lies within the radius of the test area's centre is dumped
    /// (TestWorld's area, or where the host player stands): the client only
    /// knows the enemies near it, and only loads the world around it.
    /// </summary>
    public static class StateDump
    {
        /// <summary>Every T in the loaded scenes, inactive ones included. The
        /// game switches off things far from its player, and that differs
        /// between games, so FindObjectsOfType (active only) would make the
        /// dumps differ for objects that are the same in both.</summary>
        public static IEnumerable<T> All<T>() where T : Component =>
            Resources.FindObjectsOfTypeAll<T>().Where(
                c => c != null && c.gameObject.scene.IsValid());

        public sealed class Ctx
        {
            public Vector3 Centre;
            public float Radius;
            public readonly SortedDictionary<string, object> Entries =
                new SortedDictionary<string, object>(StringComparer.Ordinal);

            public bool Near(Vector3 p) => Flat(p - Centre) <= Radius;

            /// <summary>
            /// An entry for this object, keyed by its ObjRef; "name" and "pos"
            /// are filled in.
            /// </summary>
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

            /// <summary>
            /// An entry under this key (made unique with "#2", "#3"... if it's
            /// taken).
            /// </summary>
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

        /// <summary>
        /// Add a section of the shared world (compared between the games),
        /// to the game's DumpSections.
        /// </summary>
        public static void AddSection(string name, Action<Ctx> fill)
        {
            Plugin.Instance.Host.Dump.AddSection(name, fill);
        }

        /// <summary>
        /// Add a section of per-player things (reported, not compared), to
        /// the game's DumpSections.
        /// </summary>
        public static void AddLocal(string name, Action<Ctx> fill)
        {
            Plugin.Instance.Host.Dump.AddLocal(name, fill);
        }

        public static float Flat(Vector3 d) =>
            Mathf.Sqrt(d.x * d.x + d.z * d.z);
        /// <summary>
        /// Rounded to a multiple of step (0.25 for hours, 1 for health...).
        /// </summary>
        public static double R(float x, float step) =>
            Math.Round(Math.Round(x / step) * step, 3);
        public static List<object> Pos(Vector3 p) => new List<object>
        {
            (double)Mathf.Round(p.x),
            (double)Mathf.Round(p.z)
        };

        /// <summary>
        /// Where the test area is: TestWorld's centre, else where our player
        /// stands.
        /// </summary>
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

        /// <summary>
        /// Write the dump to path. centre/radius default to the test area's.
        /// </summary>
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
            if (Plugin.Instance.Host.Session == null)
            {
                role = "alone";
            }
            else if (Plugin.Instance.Host.Session.IsHost)
            {
                role = "host";
            }
            else
            {
                role = "client";
            }

            var root = new SortedDictionary<string, object>(
                StringComparer.Ordinal)
            {
                ["role"] = role,
                ["centre"] = Pos(c),
                ["radius"] = (double)radius,
            };

            DumpSections dump = Plugin.Instance.Host.Dump;
            root["sections"] = dump.Shared(c, radius);
            root["local"] = dump.Local(c, radius);
            Directory.CreateDirectory(
                Path.GetDirectoryName(Path.GetFullPath(path)));
            var sb = new StringBuilder();
            Json(sb, root, 0);
            sb.Append('\n');
            File.WriteAllText(path, sb.ToString());
            int n =
                ((SortedDictionary<string, object>)root["sections"]).Values
                    .Sum(s =>
            {
                if (s is SortedDictionary<string, object> d)
                {
                    return d.Count;
                }

                return 0;
            });

            return $"{n} entries";
        }

        /// <summary>The world's item inventories in the area: containers, the
        /// workbench's storage, dropped items, corpses; not the player's own
        /// bags, hotbar or the UI's.</summary>
        internal static IEnumerable<Inventory> WorldInventories(Ctx ctx)
        {
            var p = Player.Instance;
            foreach (var inv in All<Inventory>())
            {
                if (inv.invType != Inventory.InvType.itemInv
                    || !ctx.Near(inv.transform.position))
                {
                    continue;
                }

                if (p != null
                    && (inv == p.Inventory || inv == p.Hotbar
                        || inv.transform.IsChildOf(p.transform)))
                {
                    continue;
                }

                yield return inv;
            }
        }

        internal static readonly HashSet<string> TrapNames =
            new HashSet<string>
            {
                "beartrap",
                "chainTrap",
                "barrelExploding",
                "gasBottle_01",
                "broken_glass"
            };

        /// <summary>A game object's name without Unity's "(Clone)".</summary>
        internal static string Clean(string n) =>
            n.Replace("(Clone)", "").Trim();

        /// <summary>The story flags that are set (Flags.flagsDict).</summary>
        internal static readonly HarmonyLib.AccessTools
            .FieldRef<Flags, Dictionary<string, Flags.Flag>> FlagsDict =
                HarmonyLib.AccessTools
                    .FieldRefAccess<Flags, Dictionary<string, Flags.Flag>>(
                        "flagsDict");

        internal static bool IsDropped(Inventory inv) =>
            inv.name.StartsWith("DroppedItem", StringComparison.Ordinal);

        internal static List<object> Slots(Inventory inv)
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
                // A gun's count is its ammo (InvItemClass.SaveState saves that,
                // not its amount, which means nothing for it).
                int amount;
                if (it.baseClass != null && it.baseClass.hasAmmo)
                {
                    amount = it.ammo;
                }
                else
                {
                    amount = it.amount;
                }

                list.Add(new Dictionary<string, object>
                {
                    ["type"] = it.type,
                    ["amount"] = amount,
                    ["durability"] = R(it.durability, 0.05f)
                });
            }

            // Trailing empty slots don't matter (a workbench's level adds
            // some).
            while (list.Count > 0 && list[list.Count - 1] == null)
            {
                list.RemoveAt(list.Count - 1);
            }

            return list;
        }

        /// <summary>
        /// A trader's stock as one string ("type x amount" per slot), for
        /// ordering traders.
        /// </summary>
        internal static string StockKey(Inventory inv) =>
            string.Join(",", inv.slots.Select(s =>
            {
                if (s == null || InvItemClass.isNull(s.invItem))
                {
                    return "";
                }

                return s.invItem.type + "x" + s.invItem.amount;
            }));

        internal static string Hash(List<string> lines)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                return BitConverter.ToString(
                    sha.ComputeHash(
                        Encoding.UTF8.GetBytes(string.Join("\n", lines))))
                    .Replace("-", "").Substring(0, 16).ToLowerInvariant();
            }
        }

        // ---- a small JSON writer (sorted keys, so the dumps diff cleanly) ---

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
                    sb.Append(
                        ((double)f).ToString(
                            "R", CultureInfo.InvariantCulture));
                    break;
                case double d:
                    sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
                    break;
                case System.Collections.IDictionary dict:
                    {
                        var keys = dict.Keys.Cast<string>()
                            .OrderBy(k => k, StringComparer.Ordinal).ToList();
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
