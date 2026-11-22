using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;
using Object = UnityEngine.Object;

namespace DarkwoodMP.World
{
    public static partial class MapScenarioView
    {
        private static IEnumerator Areas(string[] a, Result r)
        {
            var count = new SortedDictionary<string, int>(
                System.StringComparer.Ordinal);
            int n = 0;
            foreach (var ets in Object.FindObjectsOfType<EventTriggers>(true))
            {
                if (ets == null || !ets.gameObject.scene.IsValid())
                {
                    continue;
                }

                foreach (var et in ets.eventTriggers)
                {
                    if (et == null
                        || et.type != global::EventTrigger.Type.area
                        || !et.multipleFire)
                    {
                        continue;
                    }

                    n++;
                    foreach (var (label, ge) in new[]
                    {
                        ("enter", et.gameEvents),
                        ("exit", et.gameEventsExit)
                    })
                    {
                        if (ge != null)
                        {
                            foreach (var e in ge.events)
                            {
                                if (e == null || e.disabled)
                                {
                                    continue;
                                }

                                string text = "";
                                if (e.type == GameEvent.Type.runFunction
                                    || e.type == GameEvent.Type.fireEvent)
                                {
                                    text = " " + e.Value;
                                }

                                string k = $"{label} {e.type}{text}";
                                if (count.TryGetValue(k, out int c))
                                {
                                    count[k] = c + 1;
                                }
                                else
                                {
                                    count[k] = 1;
                                }
                            }
                        }
                    }
                }
            }

            yield return null;
            r.Info = $"{n} repeatable area triggers: " + string.Join(
                "; ", count.Select(kv => $"{kv.Key} x{kv.Value}"));
            Plugin.Log.LogInfo($"[view] vw-areas: {r.Info}");
        }

        /// <summary>
        /// The colours (alpha last) of the sprites named so near the mark:
        /// "spot_black a0.00 ...".
        /// </summary>
        internal static string ColorsNow(string[] names)
        {
            if (_mark == null)
            {
                return "no mark";
            }

            var parts = new List<string>();
            foreach (var s in Object.FindObjectsOfType<tk2dBaseSprite>(true))
            {
                if (s != null && names.Contains(s.name)
                    && StateDump.Flat(s.transform.position - _mark.Value)
                        < 1500f)
                {
                    string name = s.name;
                    float red = s.color.r;
                    float green = s.color.g;
                    float blue = s.color.b;
                    float alpha = s.color.a;
                    string off = "";
                    if (!s.gameObject.activeInHierarchy)
                    {
                        off = " (off)";
                    }

                    parts.Add(
                        $"{name} rgb {red:0.00},{green:0.00},{blue:0.00} "
                            + $"a{alpha:0.00}{off}");
                }
            }

            if (parts.Count == 0)
            {
                return "none";
            }

            return string.Join(", ", parts);
        }

        /// <summary>
        /// The area triggers by the mark: our player's entries and exits as the
        /// game counted them, and whether it stands on each.
        /// </summary>
        private static string AreasNow()
        {
            var p = Player.Instance;
            if (_mark == null || p == null)
            {
                return "no mark";
            }

            var parts = new List<string>();
            foreach (var et in Object.FindObjectsOfType<EventTriggers>())
            {
                if (et != null && et.reactsToPlayer
                    && StateDump.Flat(et.transform.position - _mark.Value)
                        < 700f
                    && et.eventTriggers.Exists(
                        t => t != null
                            && t.type == global::EventTrigger.Type.area))
                {
                    var standing = Helpers.isComponentAtPos(
                        p.transform.position,
                        1 << et.gameObject.layer,
                        et);
                    parts.Add(
                        $"{et.name} entered {et.entered} left {et.exited}, on "
                            + "it "
                            + $"{standing}");
                }
            }

            if (parts.Count == 0)
            {
                return "none";
            }

            return string.Join("; ", parts);
        }

        /// <summary>
        /// "vw-color [dark|clear] NAME...": with dark or clear, the first NAME
        /// (the screen-covering spot_black) must be faded in (alpha over 0.5)
        /// or out (under 0.1) in this game; the fades take up to 14 s, so this
        /// waits up to 16 s.
        /// </summary>
        private static IEnumerator Colors(string[] a, Result r)
        {
            string want = null;
            if (a.Length > 0 && (a[0] == "dark" || a[0] == "clear"))
            {
                want = a[0];
            }

            string[] names;
            if (want != null)
            {
                names = a.Skip(1).ToArray();
            }
            else
            {
                names = a;
            }

            float Alpha() => Object.FindObjectsOfType<tk2dBaseSprite>(true)
                .Where(
                    s => s != null && names.Length > 0 && s.name == names[0]
                        && _mark != null
                        && StateDump.Flat(s.transform.position - _mark.Value)
                            < 1500f)
                .Select(s =>
                {
                    if (s.gameObject.activeInHierarchy)
                    {
                        return s.color.a;
                    }

                    return 0f;
                }).DefaultIfEmpty(0f).Max();

            bool Ok()
            {
                if (want == null)
                {
                    return true;
                }

                if (want == "dark")
                {
                    return Alpha() > 0.5f;
                }

                return Alpha() < 0.1f;
            }

            float wait = 16f;
            if (want == null)
            {
                wait = 0f;
            }

            float until = Time.realtimeSinceStartup + wait;
            while (!Ok() && Time.realtimeSinceStartup < until)
            {
                yield return new WaitForSeconds(0.25f);
            }

            Vector3? mark = _mark;
            Vector3 origin;
            if (mark.HasValue)
            {
                origin = mark.Value;
            }
            else
            {
                origin = Vector3.zero;
            }

            float away =
                StateDump.Flat(Player.Instance.transform.position - origin);
            r.Info =
                $"{Me()} "
                    + $"{away:0} from the mark: {ColorsNow(names)}";
            if (!Ok())
            {
                r.Fail(
                    $"{names[0]} isn't {want} here: {r.Info}; areas: "
                        + $"{AreasNow()}");
            }
        }

        private static string EventNote(GameEvent e)
        {
            List<GameObject> targetGameObjects =
                e.targetGameObjects;
            if (targetGameObjects == null)
            {
                targetGameObjects =
                    new List<GameObject>();
            }

            string names = string.Join(",",
                targetGameObjects.Select(g =>
            {
                if (g == null)
                {
                    return "null";
                }

                return g.name;
            }));

            List<string> ids =
                e.targetUniqueObjects;
            if (ids == null)
            {
                ids = new List<string>();
            }

            string disabled = "";
            if (e.disabled)
            {
                disabled = " disabled";
            }

            return $"{e.type} '{e.Value}' '{e.Value2}' "
                + $"f={e.floatValue} dur={e.duration} delay={e.delay} "
                + $"mod={e.activeModifier} color={e.destColor} "
                + $"targets=[{names}|{string.Join(",", ids)}] "
                + $"fromParent={e.getTargetGameObjectsFromParent}{disabled}";
        }

        private static IEnumerator Tree(string[] a, Result r)
        {
            var list = Matches(a[0]);
            int i = 0;
            if (a.Length > 1)
            {
                i = int.Parse(a[1], CultureInfo.InvariantCulture);
            }

            if (i >= list.Count)
            {
                r.Fail("not found");
                yield break;
            }

            var root = list[i].transform;
            if (a.Length < 3 || a[2] != "self")
            {
                while (root.parent != null
                    && root.parent.name != "WorldChunks"
                    && root.parent.name != "WorldChunk")
                {
                    root = root.parent;
                }
            }

            var sb = new System.Text.StringBuilder();
            void Walk(Transform t, int depth)
            {
                var comps = string.Join(",", t.GetComponents<Component>()
                        .Where(c => c != null && !(c is Transform))
                        .Select(c => c.GetType().Name));
                var so = t.GetComponent<SaveableObject>();
                string name = t.name;
                bool active = t.gameObject.activeSelf;
                int layer = t.gameObject.layer;
                string spot = Fmt(t.position);
                float height = t.position.y;
                string saved = "";
                if (so != null)
                {
                    saved = $" saved={so.Active}";
                }

                sb.Append(
                    $"\n{new string(' ', depth * 2)}{name} active={active} "
                        + $"layer={layer} at {spot} y {height:0}{saved} "
                        + $"[{comps}]");
                foreach (var c in t.GetComponents<Collider>())
                {
                    string text = "";
                    if (c.isTrigger)
                    {
                        text = "/trigger";
                    }

                    string offNote = "/off";
                    if (c.enabled)
                    {
                        offNote = "";
                    }

                    sb.Append(
                        $" col:{c.GetType().Name}{text}{offNote} "
                            + $"{c.bounds.size.x:0}x{c.bounds.size.z:0}");
                }

                var ets = t.GetComponent<EventTriggers>();
                if (ets != null)
                {
                    foreach (var et in ets.eventTriggers)
                    {
                        string disabledNote = "";
                        if (et.disabled)
                        {
                            disabledNote = " disabled";
                        }

                        sb.Append(
                            $"\n{new string(' ', depth * 2 + 2)}> trigger "
                                + $"{et.type} fired={et.fired} "
                                + $"multi={et.multipleFire}{disabledNote}");
                        foreach (var (label, ge) in new[]
                        {
                            ("do", et.gameEvents),
                            ("exit", et.gameEventsExit),
                            ("else", et.gameEventsElse)
                        })
                        {
                            if (ge != null)
                            {
                                foreach (var e in ge.events)
                                {
                                    if (e != null)
                                    {
                                        string pad =
                                            new string(' ', depth * 2 + 4);
                                        sb.Append($"\n{pad}{label} ");
                                        sb.Append(EventNote(e));
                                    }
                                }
                            }
                        }
                    }
                }

                foreach (Transform c in t)
                {
                    Walk(c, depth + 1);
                }
            }

            Walk(root, 0);
            Plugin.Log.LogInfo($"[view] vw-tree {root.name}:{sb}");
            r.Info =
                $"{root.name}: "
                    + $"{root.GetComponentsInChildren<Transform>(true).Length} "
                    + "objects (in the log)";
        }
    }
}
