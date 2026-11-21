using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;

namespace DarkwoodMP.World
{
    public static partial class MapScenarioView
    {
        private static IEnumerator Pace(string[] a, Result r)
        {
            if (_mark == null)
            {
                r.Fail("no mark (vw-mark first)");
                yield break;
            }

            var p = Player.Instance;
            float dist = 200f;
            if (a.Length > 0)
            {
                dist = F(a[0]);
            }

            int times = 3;
            if (a.Length > 1)
            {
                times = int.Parse(a[1], CultureInfo.InvariantCulture);
            }

            var start = p.transform.position;
            var dir = _mark.Value - start;
            dir.y = 0f;
            dir = dir.normalized;
            float speed = RunSpeed;
            var w = new Watch();
            var eof = new WaitForEndOfFrame();
            for (int i = 0; i < times * 2; i++)
            {
                Vector3 to;
                if (i % 2 == 0)
                {
                    to = start + dir * dist;
                }
                else
                {
                    to = start;
                }

                while (StateDump.Flat(to - p.transform.position) > 1f)
                {
                    var d = to - p.transform.position;
                    d.y = 0f;
                    Place(
                        p.transform.position + Vector3.ClampMagnitude(
                            d, speed * FrameTime));
                    yield return eof;
                    w.Frame(false);
                }
            }

            r.Info =
                $"{Me()} paced {dist:0} x{times} at {speed:0}/s, now "
                    + $"{StateDump.Flat(p.transform.position - _mark.Value):0} "
                    + $"from the mark; {w}";
        }

        // ---- walking up to it -----------------------------------------------

        /// <summary>
        /// From the mark towards the centre (the way the players come from),
        /// flat and of length 1.
        /// </summary>
        private static Vector3 Back()
        {
            var d = Centre - _mark.Value;
            d.y = 0f;
            if (d.sqrMagnitude < 1f)
            {
                d = Vector3.right;
            }

            return d.normalized;
        }

        private static IEnumerator Stage(string[] a, Result r)
        {
            if (_mark == null)
            {
                r.Fail("no mark (vw-mark first)");
                yield break;
            }

            if (a.Length < 1)
            {
                r.Fail("usage: vw-stage LEAD;host=GAP/SIDE,...");
                yield break;
            }

            var parts = a[0].Split(';');
            float lead = F(parts[0]), gap = 0f, side = 0f;
            string me = Me();
            foreach (var kv in parts.Skip(1).SelectMany(x => x.Split(',')))
            {
                var p = kv.Split('=');
                if (p.Length != 2 || p[0] != me)
                {
                    continue;
                }

                var gs = p[1].Split('/');
                gap = F(gs[0]);
                if (gs.Length > 1)
                {
                    side = F(gs[1]);
                }
            }

            var back = Back();
            var right = new Vector3(back.z, 0f, -back.x);
            var to = _mark.Value + back * (lead + gap) + right * side;
            yield return Walk(to, 20f, detours: 0);
            Face(_mark.Value);
            float away = StateDump.Flat(
                Player.Instance.transform.position - _mark.Value);
            r.Info =
                $"{me} at {Fmt(Player.Instance.transform.position)}, "
                    + $"{away:0} from the mark {_markName}";
        }

        /// <summary>
        /// Our player faces the point (its up is its front), as the mouse would
        /// turn it.
        /// </summary>
        private static void Face(Vector3 at)
        {
            var p = Player.Instance;
            var d = at - p.transform.position;
            d.y = 0f;
            if (d.sqrMagnitude > 1f)
            {
                p.transform.rotation =
                    Quaternion.LookRotation(Vector3.down, d.normalized);
            }
        }

        /// <summary>What happened to our view on the way.</summary>
        private sealed class Watch
        {
            public int Frames, Dark, Audits, BadAudits;
            public float WorstMismatch;
            public string WorstAt = "";
            public readonly Dictionary<string, int> DarkBy =
                new Dictionary<string, int>();
            public readonly HashSet<string> Ghosts = new HashSet<string>();
            public readonly HashSet<string> BadDark = new HashSet<string>();

            public void Frame(bool audit)
            {
                var p = Player.Instance;
                Light2D light = null;
                if (p != null)
                {
                    light = FovLight(p);
                }

                if (light == null || Time.timeScale == 0f)
                {
                    return;
                }

                Frames++;
                var mr = light.GetComponent<MeshRenderer>();
                if (light.gameObject.activeInHierarchy && mr != null
                    && !mr.enabled)
                {
                    Dark++;
                    foreach (var h in Physics.RaycastAll(
                        light.transform.position + new Vector3(0f, 100f, 0f),
                        Vector3.down, 200f, Sync.ViewWatch.HideMask))
                    {
                        if (h.collider == null)
                        {
                            continue;
                        }

                        string k = Sync.ViewWatch.Path(h.collider.transform)
                            + $" [layer {h.collider.gameObject.layer}]";
                        if (DarkBy.TryGetValue(k, out int n))
                        {
                            DarkBy[k] = n + 1;
                        }
                        else
                        {
                            DarkBy[k] = 1;
                        }

                        if (IsOurs(h.collider) || IsGhost(h.collider))
                        {
                            BadDark.Add(k);
                        }
                    }
                }

                // A light switched off isn't redrawn (Light2D.LateUpdate
                // returns first), so its mesh is the one from before: walking
                // straight through scenery a player can't cross (roots)
                // switches it off, as alone.
                if (!audit || (mr != null && !mr.enabled))
                {
                    return;
                }

                Audits++;
                float m = Mismatch(light, out _);
                if (m > WorstMismatch)
                {
                    WorstMismatch = m;
                    WorstAt = Fmt(p.transform.position);
                }

                if (m > MaxMismatch)
                {
                    BadAudits++;
                }

                foreach (var g in GhostBlockers(light))
                {
                    Ghosts.Add(g);
                }
            }

            public override string ToString()
            {
                string causes = "";
                if (DarkBy.Count != 0)
                {
                    string top = string.Join(
                        "; ",
                        DarkBy.OrderByDescending(x => x.Value)
                            .Take(6)
                            .Select(x => $"{x.Key} x{x.Value}"));
                    causes = " (by " + $"{top})";
                }

                string place = "";
                if (WorstAt != "")
                {
                    place = " at " + WorstAt;
                }

                string ghosts;
                if (Ghosts.Count == 0)
                {
                    ghosts = "none";
                }
                else
                {
                    ghosts = string.Join("; ", Ghosts.Take(6));
                }

                return $"{Frames} frames: black {Dark}{causes}, " +
                    $"mesh audits {Audits} (worst {WorstMismatch:P0} not "
                    + $"matching our rays{place}, {BadAudits} over "
                    + $"{MaxMismatch:P0}), " +
                    $"ghost blockers {ghosts}";
            }

            public string Problem()
            {
                if (BadDark.Count > 0)
                {
                    return "our view went black over something only co-op put "
                        + $"there: {string.Join("; ", BadDark)}";
                }

                if (BadAudits > 0)
                {
                    return "our view cone wasn't worked out where we stand "
                        + $"({BadAudits} of {Audits} audits)";
                }

                if (Ghosts.Count > 0)
                {
                    return "something switched off cuts our view: "
                        + $"{string.Join("; ", Ghosts)}";
                }

                return null;
            }
        }

        private const float MaxMismatch = 0.25f;

        /// <summary>
        /// About how fast a player runs (world units a second; Player.runSpeed
        /// is in other units).
        /// </summary>
        private const float RunSpeed = 450f;

        private static IEnumerator Approach(string[] a, Result r)
        {
            if (_mark == null)
            {
                r.Fail("no mark (vw-mark first)");
                yield break;
            }

            var p = Player.Instance;
            float len = 3000f;
            if (a.Length > 0)
            {
                len = F(a[0]);
            }

            float speed;
            if (a.Length > 1)
            {
                speed = F(a[1]);
            }
            else
            {
                speed = RunSpeed;
            }

            // Down the line from where we stand towards the mark, LEN long (not
            // past the mark).
            var from = p.transform.position;
            var dir = _mark.Value - from;
            dir.y = 0f;
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
                if (audit)
                {
                    nextAudit = Time.time + 0.5f;
                }

                w.Frame(audit);
            }

            // A moment at the end, standing still: the view settles where we
            // arrived.
            float until = Time.time + 2f;
            while (Time.time < until)
            {
                yield return eof;
                w.Frame(true);
            }

            float away = StateDump.Flat(p.transform.position - _mark.Value);
            r.Info =
                $"{Me()} walked {gone:0} at {speed:0}/s in "
                    + $"{Time.time - t0:0.0} s to {Fmt(p.transform.position)}, "
                    + $"{away:0} "
                    + $"from the mark; {w}";
            Plugin.Log.LogInfo($"[view] vw-approach: {r.Info}");
            var bad = w.Problem();
            if (bad != null)
            {
                r.Fail($"{bad}; {r.Info}");
            }
        }

        private static IEnumerator Check(string[] a, Result r)
        {
            var p = Player.Instance;
            Light2D light = null;
            if (p != null)
            {
                light = FovLight(p);
            }

            if (light == null)
            {
                r.Fail("no view light");
                yield break;
            }

            var w = new Watch();
            var eof = new WaitForEndOfFrame();
            float until = Time.time + 3f, next = 0f;
            while (Time.time < until)
            {
                yield return eof;
                bool audit = Time.time >= next;
                if (audit)
                {
                    next = Time.time + 0.5f;
                }

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
            string who = Me();
            string spot = Fmt(p.transform.position);
            string distance = "";
            if (_mark != null)
            {
                distance =
                    $", {StateDump.Flat(p.transform.position - _mark.Value):0} "
                        + "from the mark";
            }

            r.Info = $"{who} at {spot}{distance}: " +
                     $"cone {light.LightConeAngle:0} (currentFOV "
                + $"{CurrentFov(p):0}), radius {light.LightRadius:0}; "
                + $"{Sides(p, light)}; mesh {audit1}; " +
                     $"{MapScenarioLights.ViewSource()}; {w}";
            Plugin.Log.LogInfo($"[view] vw-check: {r.Info}");
            var bad = w.Problem();
            if (bad == null && w.Dark > 0)
            {
                bad = "our view is black here (by "
                    + $"{string.Join("; ", w.DarkBy.Keys)})";
            }

            if (bad == null
                && !MapScenarioLights.ViewSource().StartsWith("view: ours"))
            {
                bad = "the view lights aren't our own";
            }

            if (bad != null)
            {
                r.Fail($"{bad}; {r.Info}");
            }
        }
    }
}
