using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using DarkwoodMP.Net;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;
using Object = UnityEngine.Object;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Map test steps for what each player sees (tools/tests/map_scenario_view.py): players walking up to a spot at
    /// running speed, together and far behind each other, while every game watches its own view light
    /// (Player.FOVLight) every frame:
    ///
    ///   black: its renderer switched off (Light2D.LateUpdate's ray straight down hit something, Sync.ViewWatch);
    ///   not its own: the cone's mesh doesn't match rays cast from where our player stands now (a mesh drawn by
    ///   another light, somewhere else, as a puppet's copied lantern once did);
    ///   ghost blockers: a collider that cuts the view (the light's shadow layers) inside the light's reach whose
    ///   saved object is switched off (SaveableObject.Active false), or one of the mod's own objects;
    ///   one side cut: how far the view reaches on the left and on the right of where our player faces, and what
    ///   stops it there.
    ///
    /// Steps: "vw-find PATTERN [N]", "vw-mark PATTERN INDEX", "vw-stage SPEC", "vw-approach LEN [SPEED]",
    /// "vw-check [DIR TAG]".
    /// </summary>
    public static class MapScenarioView
    {
        private static readonly HarmonyLib.AccessTools.FieldRef<Player, Light2D> FovLight = HarmonyLib.AccessTools.FieldRefAccess<Player, Light2D>("FOVLight");
        private static readonly HarmonyLib.AccessTools.FieldRef<Player, float> CurrentFov = HarmonyLib.AccessTools.FieldRefAccess<Player, float>("currentFOV");

        private static Vector3? _mark;
        private static string _markName = "";

        public static void Register()
        {
            // "vw-find PATTERN [N]": the N (8) objects whose name has PATTERN nearest the centre, with what of them cuts the view.
            Add("vw-find", Find, timeout: 30f);
            // "vw-mark PATTERN INDEX": the INDEXth of those (0: the nearest) is the spot the players walk to.
            Add("vw-mark", MarkStep, timeout: 30f);
            // "vw-stage SPEC": walk (quickly) to our start on the line from the mark towards the centre.
            // SPEC "LEAD;host=GAP/SIDE;c1=GAP/SIDE;...": LEAD + GAP from the mark, SIDE to the right of that line.
            Add("vw-stage", Stage, timeout: 180f);
            // "vw-approach LEN [SPEED]": walk LEN towards the mark at SPEED (our player's run speed), watching the view.
            Add("vw-approach", Approach, timeout: 400f);
            // "vw-check [DIR TAG]": our view now, for 3 s (a screenshot DIR/NAME-TAG.png with DIR).
            Add("vw-check", Check, timeout: 40f);
            // "vw-tree PATTERN [INDEX [self]]": the INDEXth spot's objects (self: from the match itself, not its chunk), colliders and scripted events, into the log.
            Add("vw-tree", Tree, timeout: 30f);
            // "vw-color NAME [NAME...]": the colour of each sprite named NAME within 1500 of the mark (vw-mark first).
            Add("vw-color", Colors, timeout: 20f);
            // "vw-pace DIST TIMES": back and forth DIST towards and away from the mark TIMES times at run speed.
            Add("vw-pace", Pace, timeout: 300f);
            // "vw-areas": every repeatable area trigger in this game: the steps they run, counted by type (into the log).
            Add("vw-areas", Areas, timeout: 60f);
        }

        private static IEnumerator Areas(string[] a, Result r)
        {
            var count = new SortedDictionary<string, int>(System.StringComparer.Ordinal);
            int n = 0;
            foreach (var ets in Object.FindObjectsOfType<EventTriggers>(true))
            {
                if (ets == null || !ets.gameObject.scene.IsValid()) continue;
                foreach (var et in ets.eventTriggers)
                {
                    if (et == null || et.type != global::EventTrigger.Type.area || !et.multipleFire) continue;
                    n++;
                    foreach (var (label, ge) in new[] { ("enter", et.gameEvents), ("exit", et.gameEventsExit) })
                        if (ge != null)
                            foreach (var e in ge.events)
                            {
                                if (e == null || e.disabled) continue;
                                string k = $"{label} {e.type}{(e.type == GameEvent.Type.runFunction || e.type == GameEvent.Type.fireEvent ? " " + e.Value : "")}";
                                count[k] = count.TryGetValue(k, out int c) ? c + 1 : 1;
                            }
                }
            }
            yield return null;
            r.Info = $"{n} repeatable area triggers: " + string.Join("; ", count.Select(kv => $"{kv.Key} x{kv.Value}"));
            Plugin.Log.LogInfo($"[view] vw-areas: {r.Info}");
        }

        private static IEnumerator Pace(string[] a, Result r)
        {
            if (_mark == null) { r.Fail("no mark (vw-mark first)"); yield break; }
            var p = Player.Instance;
            float dist = a.Length > 0 ? F(a[0]) : 200f;
            int times = a.Length > 1 ? int.Parse(a[1], CultureInfo.InvariantCulture) : 3;
            var start = p.transform.position;
            var dir = _mark.Value - start; dir.y = 0f; dir = dir.normalized;
            float speed = RunSpeed;
            var w = new Watch();
            var eof = new WaitForEndOfFrame();
            for (int i = 0; i < times * 2; i++)
            {
                var to = i % 2 == 0 ? start + dir * dist : start;
                while (StateDump.Flat(to - p.transform.position) > 1f)
                {
                    var d = to - p.transform.position; d.y = 0f;
                    Place(p.transform.position + Vector3.ClampMagnitude(d, speed * FrameTime));
                    yield return eof;
                    w.Frame(false);
                }
            }
            r.Info = $"{Me()} paced {dist:0} x{times} at {speed:0}/s, now {StateDump.Flat(p.transform.position - _mark.Value):0} from the mark; {w}";
        }

        /// <summary>The colours (alpha last) of the sprites named so near the mark: "spot_black a0.00 ...".</summary>
        internal static string ColorsNow(string[] names)
        {
            if (_mark == null) return "no mark";
            var parts = new List<string>();
            foreach (var s in Object.FindObjectsOfType<tk2dBaseSprite>(true))
                if (s != null && names.Contains(s.name) && StateDump.Flat(s.transform.position - _mark.Value) < 1500f)
                    parts.Add($"{s.name} rgb {s.color.r:0.00},{s.color.g:0.00},{s.color.b:0.00} a{s.color.a:0.00}{(s.gameObject.activeInHierarchy ? "" : " (off)")}");
            return parts.Count == 0 ? "none" : string.Join(", ", parts);
        }

        /// <summary>The area triggers by the mark: our player's entries and exits as the game counted them, and whether it stands on each.</summary>
        private static string AreasNow()
        {
            var p = Player.Instance;
            if (_mark == null || p == null) return "no mark";
            var parts = new List<string>();
            foreach (var et in Object.FindObjectsOfType<EventTriggers>())
                if (et != null && et.reactsToPlayer && StateDump.Flat(et.transform.position - _mark.Value) < 700f
                    && et.eventTriggers.Exists(t => t != null && t.type == global::EventTrigger.Type.area))
                    parts.Add($"{et.name} entered {et.entered} left {et.exited}, on it {Helpers.isComponentAtPos(p.transform.position, 1 << et.gameObject.layer, et)}");
            return parts.Count == 0 ? "none" : string.Join("; ", parts);
        }

        /// <summary>
        /// "vw-color [dark|clear] NAME...": with dark or clear, the first NAME (the screen-covering spot_black) must be
        /// faded in (alpha over 0.5) or out (under 0.1) in this game; the fades take up to 14 s, so this waits up to 16 s.
        /// </summary>
        private static IEnumerator Colors(string[] a, Result r)
        {
            string want = a.Length > 0 && (a[0] == "dark" || a[0] == "clear") ? a[0] : null;
            var names = want != null ? a.Skip(1).ToArray() : a;
            float Alpha() => Object.FindObjectsOfType<tk2dBaseSprite>(true)
                .Where(s => s != null && names.Length > 0 && s.name == names[0] && _mark != null && StateDump.Flat(s.transform.position - _mark.Value) < 1500f)
                .Select(s => s.gameObject.activeInHierarchy ? s.color.a : 0f).DefaultIfEmpty(0f).Max();
            bool Ok() => want == null || (want == "dark" ? Alpha() > 0.5f : Alpha() < 0.1f);
            float until = Time.realtimeSinceStartup + (want == null ? 0f : 16f);
            while (!Ok() && Time.realtimeSinceStartup < until) yield return new WaitForSeconds(0.25f);
            r.Info = $"{Me()} {StateDump.Flat(Player.Instance.transform.position - (_mark ?? Vector3.zero)):0} from the mark: {ColorsNow(names)}";
            if (!Ok()) r.Fail($"{names[0]} isn't {want} here: {r.Info}; areas: {AreasNow()}");
        }

        private static IEnumerator Tree(string[] a, Result r)
        {
            var list = Matches(a[0]);
            int i = a.Length > 1 ? int.Parse(a[1], CultureInfo.InvariantCulture) : 0;
            if (i >= list.Count) { r.Fail("not found"); yield break; }
            var root = list[i].transform;
            if (a.Length < 3 || a[2] != "self")
                while (root.parent != null && root.parent.name != "WorldChunks" && root.parent.name != "WorldChunk") root = root.parent;
            var sb = new System.Text.StringBuilder();
            void Walk(Transform t, int depth)
            {
                var comps = string.Join(",", t.GetComponents<Component>().Where(c => c != null && !(c is Transform)).Select(c => c.GetType().Name));
                var so = t.GetComponent<SaveableObject>();
                sb.Append($"\n{new string(' ', depth * 2)}{t.name} active={t.gameObject.activeSelf} layer={t.gameObject.layer} at {Fmt(t.position)} y {t.position.y:0}{(so != null ? $" saved={so.Active}" : "")} [{comps}]");
                foreach (var c in t.GetComponents<Collider>())
                    sb.Append($" col:{c.GetType().Name}{(c.isTrigger ? "/trigger" : "")}{(c.enabled ? "" : "/off")} {c.bounds.size.x:0}x{c.bounds.size.z:0}");
                var ets = t.GetComponent<EventTriggers>();
                if (ets != null)
                    foreach (var et in ets.eventTriggers)
                    {
                        sb.Append($"\n{new string(' ', depth * 2 + 2)}> trigger {et.type} fired={et.fired} multi={et.multipleFire}{(et.disabled ? " disabled" : "")}");
                        foreach (var (label, ge) in new[] { ("do", et.gameEvents), ("exit", et.gameEventsExit), ("else", et.gameEventsElse) })
                            if (ge != null)
                                foreach (var e in ge.events)
                                    if (e != null) sb.Append($"\n{new string(' ', depth * 2 + 4)}{label} {e.type} '{e.Value}' '{e.Value2}' f={e.floatValue} dur={e.duration} delay={e.delay} mod={e.activeModifier} color={e.destColor} targets=[{string.Join(",", (e.targetGameObjects ?? new List<GameObject>()).Select(g => g == null ? "null" : g.name))}|{string.Join(",", e.targetUniqueObjects ?? new List<string>())}] fromParent={e.getTargetGameObjectsFromParent}{(e.disabled ? " disabled" : "")}");
                    }
                foreach (Transform c in t) Walk(c, depth + 1);
            }
            Walk(root, 0);
            Plugin.Log.LogInfo($"[view] vw-tree {root.name}:{sb}");
            r.Info = $"{root.name}: {root.GetComponentsInChildren<Transform>(true).Length} objects (in the log)";
        }

        // ---- who we are -------------------------------------------------------------------------------------

        /// <summary>"host", or "c1".."c4" for the client games (named Client1..Client4 by the tests).</summary>
        private static string Me()
        {
            var s = Session.Instance;
            if (s == null || s.IsHost) return "host";
            string n = s.LocalName ?? "";
            return n.StartsWith("Client") ? "c" + n.Substring(6) : n;
        }

        // ---- finding the spot -------------------------------------------------------------------------------

        private static List<GameObject> Matches(string pattern)
        {
            var c = Centre;
            return Object.FindObjectsOfType<Transform>(true)
                .Where(t => t != null && t.gameObject.scene.IsValid() && t.name.IndexOf(pattern, System.StringComparison.OrdinalIgnoreCase) >= 0)
                .Select(t => t.gameObject)
                // One per spot: a prefab's children often carry the same name part.
                .GroupBy(g => new Vector2Int(Mathf.RoundToInt(g.transform.position.x / 100f), Mathf.RoundToInt(g.transform.position.z / 100f)))
                .Select(g => g.OrderBy(x => x.transform.GetSiblingIndex()).First())
                .OrderBy(g => StateDump.Flat(g.transform.position - c)).ToList();
        }

        private static string Describe(GameObject g)
        {
            var so = g.GetComponentInParent<SaveableObject>(true);
            var cols = g.GetComponentsInChildren<Collider>(true).Where(x => (Sync.ViewWatch.HideMask & (1 << x.gameObject.layer)) != 0).ToList();
            return $"{Sync.ViewWatch.Path(g.transform)} at {Fmt(g.transform.position)} ({StateDump.Flat(g.transform.position - Centre):0} from the centre), " +
                   $"active {g.activeInHierarchy}, saved {(so == null ? "-" : so.Active.ToString())}, " +
                   $"view-layer colliders {cols.Count}{(cols.Count == 0 ? "" : $" [{string.Join(", ", cols.Take(4).Select(x => $"{x.name}:{x.gameObject.layer}{(x.enabled ? "" : ":off")}"))}]")}";
        }

        private static IEnumerator Find(string[] a, Result r)
        {
            if (a.Length < 1) { r.Fail("usage: vw-find PATTERN [N]"); yield break; }
            int n = a.Length > 1 ? int.Parse(a[1], CultureInfo.InvariantCulture) : 8;
            var list = Matches(a[0]);
            r.Info = $"{list.Count} spots with '{a[0]}': " + string.Join("; ", list.Take(n).Select(Describe));
            Plugin.Log.LogInfo($"[view] vw-find {a[0]}: {r.Info}");
        }

        private static IEnumerator MarkStep(string[] a, Result r)
        {
            if (a.Length < 2) { r.Fail("usage: vw-mark PATTERN INDEX"); yield break; }
            var list = Matches(a[0]);
            int i = int.Parse(a[1], CultureInfo.InvariantCulture);
            if (i >= list.Count) { r.Fail($"only {list.Count} spots with '{a[0]}'"); yield break; }
            _mark = new Vector3(list[i].transform.position.x, 0f, list[i].transform.position.z);
            _markName = list[i].name;
            r.Info = $"mark: {Describe(list[i])}";
        }

        // ---- walking up to it -------------------------------------------------------------------------------

        /// <summary>From the mark towards the centre (the way the players come from), flat and of length 1.</summary>
        private static Vector3 Back()
        {
            var d = Centre - _mark.Value; d.y = 0f;
            if (d.sqrMagnitude < 1f) d = Vector3.right;
            return d.normalized;
        }

        private static IEnumerator Stage(string[] a, Result r)
        {
            if (_mark == null) { r.Fail("no mark (vw-mark first)"); yield break; }
            if (a.Length < 1) { r.Fail("usage: vw-stage LEAD;host=GAP/SIDE,..."); yield break; }
            var parts = a[0].Split(';');
            float lead = F(parts[0]), gap = 0f, side = 0f;
            string me = Me();
            foreach (var kv in parts.Skip(1).SelectMany(x => x.Split(',')))
            {
                var p = kv.Split('=');
                if (p.Length != 2 || p[0] != me) continue;
                var gs = p[1].Split('/');
                gap = F(gs[0]);
                if (gs.Length > 1) side = F(gs[1]);
            }
            var back = Back();
            var right = new Vector3(back.z, 0f, -back.x);
            var to = _mark.Value + back * (lead + gap) + right * side;
            yield return Walk(to, 20f, detours: 0);
            Face(_mark.Value);
            r.Info = $"{me} at {Fmt(Player.Instance.transform.position)}, {StateDump.Flat(Player.Instance.transform.position - _mark.Value):0} from the mark {_markName}";
        }

        /// <summary>Our player faces the point (its up is its front), as the mouse would turn it.</summary>
        private static void Face(Vector3 at)
        {
            var p = Player.Instance;
            var d = at - p.transform.position; d.y = 0f;
            if (d.sqrMagnitude > 1f) p.transform.rotation = Quaternion.LookRotation(Vector3.down, d.normalized);
        }

        /// <summary>What happened to our view on the way.</summary>
        private sealed class Watch
        {
            public int Frames, Dark, Audits, BadAudits;
            public float WorstMismatch;
            public string WorstAt = "";
            public readonly Dictionary<string, int> DarkBy = new Dictionary<string, int>();
            public readonly HashSet<string> Ghosts = new HashSet<string>();
            public readonly HashSet<string> BadDark = new HashSet<string>();

            public void Frame(bool audit)
            {
                var p = Player.Instance;
                var light = p != null ? FovLight(p) : null;
                if (light == null || Time.timeScale == 0f) return;
                Frames++;
                var mr = light.GetComponent<MeshRenderer>();
                if (light.gameObject.activeInHierarchy && mr != null && !mr.enabled)
                {
                    Dark++;
                    foreach (var h in Physics.RaycastAll(light.transform.position + new Vector3(0f, 100f, 0f), Vector3.down, 200f, Sync.ViewWatch.HideMask))
                    {
                        if (h.collider == null) continue;
                        string k = Sync.ViewWatch.Path(h.collider.transform) + $" [layer {h.collider.gameObject.layer}]";
                        DarkBy[k] = DarkBy.TryGetValue(k, out int n) ? n + 1 : 1;
                        if (IsOurs(h.collider) || IsGhost(h.collider)) BadDark.Add(k);
                    }
                }
                // A light switched off isn't redrawn (Light2D.LateUpdate returns first), so its mesh is the one from
                // before: walking straight through scenery a player can't cross (roots) switches it off, as alone.
                if (!audit || (mr != null && !mr.enabled)) return;
                Audits++;
                float m = Mismatch(light, out _);
                if (m > WorstMismatch) { WorstMismatch = m; WorstAt = Fmt(p.transform.position); }
                if (m > MaxMismatch) BadAudits++;
                foreach (var g in GhostBlockers(light)) Ghosts.Add(g);
            }

            public override string ToString() =>
                $"{Frames} frames: black {Dark}{(DarkBy.Count == 0 ? "" : $" (by {string.Join("; ", DarkBy.OrderByDescending(x => x.Value).Take(6).Select(x => $"{x.Key} x{x.Value}"))})")}, " +
                $"mesh audits {Audits} (worst {WorstMismatch:P0} not matching our rays{(WorstAt == "" ? "" : " at " + WorstAt)}, {BadAudits} over {MaxMismatch:P0}), " +
                $"ghost blockers {(Ghosts.Count == 0 ? "none" : string.Join("; ", Ghosts.Take(6)))}";

            public string Problem()
            {
                if (BadDark.Count > 0) return $"our view went black over something only co-op put there: {string.Join("; ", BadDark)}";
                if (BadAudits > 0) return $"our view cone wasn't worked out where we stand ({BadAudits} of {Audits} audits)";
                if (Ghosts.Count > 0) return $"something switched off cuts our view: {string.Join("; ", Ghosts)}";
                return null;
            }
        }

        private const float MaxMismatch = 0.25f;

        /// <summary>About how fast a player runs (world units a second; Player.runSpeed is in other units).</summary>
        private const float RunSpeed = 450f;

        private static IEnumerator Approach(string[] a, Result r)
        {
            if (_mark == null) { r.Fail("no mark (vw-mark first)"); yield break; }
            var p = Player.Instance;
            float len = a.Length > 0 ? F(a[0]) : 3000f;
            float speed = a.Length > 1 ? F(a[1]) : RunSpeed;
            // Down the line from where we stand towards the mark, LEN long (not past the mark).
            var from = p.transform.position;
            var dir = _mark.Value - from; dir.y = 0f;
            float total = Mathf.Min(len, dir.magnitude);
            dir = dir.normalized;
            var w = new Watch();
            float gone = 0f, nextAudit = 0f, t0 = Time.time;
            var eof = new WaitForEndOfFrame();
            while (gone < total)
            {
                float step = Mathf.Min(speed * FrameTime, total - gone);
                gone += step;
                Place(p.transform.position + dir * step);
                Face(p.transform.position + dir * 100f);
                yield return eof;
                bool audit = Time.time >= nextAudit;
                if (audit) nextAudit = Time.time + 0.5f;
                w.Frame(audit);
            }
            // A moment at the end, standing still: the view settles where we arrived.
            float until = Time.time + 2f;
            while (Time.time < until) { yield return eof; w.Frame(true); }
            r.Info = $"{Me()} walked {gone:0} at {speed:0}/s in {Time.time - t0:0.0} s to {Fmt(p.transform.position)}, " +
                     $"{StateDump.Flat(p.transform.position - _mark.Value):0} from the mark; {w}";
            Plugin.Log.LogInfo($"[view] vw-approach: {r.Info}");
            var bad = w.Problem();
            if (bad != null) r.Fail($"{bad}; {r.Info}");
        }

        private static IEnumerator Check(string[] a, Result r)
        {
            var p = Player.Instance;
            var light = p != null ? FovLight(p) : null;
            if (light == null) { r.Fail("no view light"); yield break; }
            var w = new Watch();
            var eof = new WaitForEndOfFrame();
            float until = Time.time + 3f, next = 0f;
            while (Time.time < until)
            {
                yield return eof;
                bool audit = Time.time >= next;
                if (audit) next = Time.time + 0.5f;
                w.Frame(audit);
            }
            if (a.Length > 1)
            {
                Directory.CreateDirectory(a[0]);
                string path = Path.Combine(a[0], $"{Me()}-{a[1]}.png");
                ScreenCapture.CaptureScreenshot(path);
                yield return new WaitForSecondsRealtime(1f);
                Plugin.Log.LogInfo($"[shots] {path}");
            }
            Mismatch(light, out string audit1);
            r.Info = $"{Me()} at {Fmt(p.transform.position)}{(_mark == null ? "" : $", {StateDump.Flat(p.transform.position - _mark.Value):0} from the mark")}: " +
                     $"cone {light.LightConeAngle:0} (currentFOV {CurrentFov(p):0}), radius {light.LightRadius:0}; {Sides(p, light)}; mesh {audit1}; " +
                     $"{MapScenarioLights.ViewSource()}; {w}";
            Plugin.Log.LogInfo($"[view] vw-check: {r.Info}");
            var bad = w.Problem();
            if (bad == null && w.Dark > 0) bad = $"our view is black here (by {string.Join("; ", w.DarkBy.Keys)})";
            if (bad == null && !MapScenarioLights.ViewSource().StartsWith("view: ours")) bad = "the view lights aren't our own";
            if (bad != null) r.Fail($"{bad}; {r.Info}");
        }

        // ---- the checks -------------------------------------------------------------------------------------

        private static bool IsOurs(Collider c)
        {
            for (var t = c.transform; t != null; t = t.parent) if (t.name.StartsWith("DarkwoodMP_")) return true;
            return c.GetComponentInParent<Combat.NetPlayerProxy>() != null;
        }

        /// <summary>
        /// A collider whose saved object is switched off in the save (it shouldn't be there at all). Not inside a
        /// separate location: the game leaves Active off on many of a location's objects it shows (the village's
        /// border roots and trees, in every game alike), so there it says nothing.
        /// </summary>
        private static bool IsGhost(Collider c)
        {
            if (c == null || !c.enabled || !c.gameObject.activeInHierarchy) return false;
            for (var t = c.transform; t != null; t = t.parent) if (t.name == "OutsideLocations") return false;
            for (var t = c.transform; t != null; t = t.parent)
            {
                var so = t.GetComponent<SaveableObject>();
                if (so != null && !so.Active) return true;
            }
            return false;
        }

        private static List<string> GhostBlockers(Light2D light)
        {
            var list = new List<string>();
            foreach (var c in Physics.OverlapSphere(light.transform.position, light.LightRadius, light.ShadowLayer))
                if (c != null && (IsGhost(c) || IsOurs(c))) list.Add($"{Sync.ViewWatch.Path(c.transform)} [layer {c.gameObject.layer}{(IsOurs(c) ? ", the mod's" : ", switched off in the save")}]");
            return list;
        }

        /// <summary>
        /// How much of the cone's mesh disagrees with rays cast from the light now: each vertex is a ray's end
        /// (UpdateRadialMesh), either where it hit (inside the radius) or the radius itself. A hit vertex wants our
        /// ray to hit there too; a free one wants our ray to reach (nearly) the radius. Rounding and shadow edges
        /// give a few disagreements; a mesh drawn somewhere else disagrees almost everywhere.
        /// </summary>
        private static float Mismatch(Light2D light, out string info)
        {
            info = "none";
            var mesh = light._mesh;
            if (mesh == null || mesh.vertexCount < 3) { info = "no mesh"; return 0f; }
            var verts = mesh.vertices;
            var o = light.transform.position;
            float R = light.LightRadius;
            int n = 0, bad = 0, hits = 0;
            for (int i = 1; i < verts.Length; i++)
            {
                var wpt = light.transform.TransformPoint(verts[i]);
                var d = wpt - o;
                float dist = d.magnitude;
                if (dist < 5f) continue;
                n++;
                bool hit = Physics.Raycast(o, d / dist, out var h, R, light.ShadowLayer);
                if (dist < R - 3f)
                {
                    hits++;
                    if (!hit || Mathf.Abs(h.distance - dist) > Mathf.Max(20f, dist * 0.15f)) bad++;
                }
                else if (hit && h.distance < R * 0.8f) bad++;
            }
            if (n == 0) { info = "empty"; return 0f; }
            float m = (float)bad / n;
            info = $"{n} rays ({hits} hit), {bad} not matching ours ({m:P0})";
            return m;
        }

        /// <summary>How far our view reaches on each side of where our player faces (rays across the cone), and what stops it.</summary>
        private static string Sides(Player p, Light2D light)
        {
            var o = light.transform.position;
            var f = p.transform.up; f.y = 0f;
            if (f.sqrMagnitude < 0.01f) return "no facing";
            f.Normalize();
            float cone = Mathf.Clamp(light.LightConeAngle, 0f, 360f), R = light.LightRadius;
            float left = 0f, right = 0f; int nl = 0, nr = 0;
            var stopL = new Dictionary<string, int>(); var stopR = new Dictionary<string, int>();
            for (int i = 0; i <= 40; i++)
            {
                float ang = -cone / 2f + cone * i / 40f; // negative: to the left (counter-clockwise seen from above)
                var dir = Quaternion.AngleAxis(ang, Vector3.up) * f;
                bool hit = Physics.Raycast(o, dir, out var h, R, light.ShadowLayer);
                float reach = hit ? h.distance / R : 1f;
                var stops = ang < 0f ? stopL : stopR;
                if (hit) { string k = h.collider.name; stops[k] = stops.TryGetValue(k, out int c) ? c + 1 : 1; }
                if (ang < 0f) { left += reach; nl++; } else if (ang > 0f) { right += reach; nr++; }
            }
            string top(Dictionary<string, int> s) => s.Count == 0 ? "nothing" : string.Join(", ", s.OrderByDescending(x => x.Value).Take(3).Select(x => $"{x.Key} x{x.Value}"));
            return $"reach left {(nl > 0 ? left / nl : 0f):P0} (stopped by {top(stopL)}), right {(nr > 0 ? right / nr : 0f):P0} (stopped by {top(stopR)})";
        }
    }
}
