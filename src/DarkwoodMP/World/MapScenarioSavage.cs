using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using DarkwoodMP.Combat;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;
using Object = UnityEngine.Object;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Map test steps for how the savages (the Villager kinds) and, as the control, a dog are animated while they
    /// attack (tools/tests/map_scenario_savage.py). Players reported savages that, attacking a player, looped one or
    /// two frames instead of their attack. The host spawns them beside the client's player; both games then sample,
    /// every frame for some seconds, each of the host's enemies' sprite animators (clip, frame, the sprite shown,
    /// playing or not; on the host also its AI state and target) into a file each, and check their own samples for a
    /// clip stuck on one or two frames; the client then compares its samples with the host's.
    /// </summary>
    public static class MapScenarioSavage
    {
        private static Player P => Player.Instance;

        public static void Register()
        {
            Add("sv-spawn", Spawn, timeout: 15f);
            Add("sv-sample", Sample, timeout: 60f);
            Add("sv-compare", Compare, timeout: 15f);
            Add("sv-clear", Clear, timeout: 10f);
        }

        /// <summary>Where both games write their samples: one machine runs both in the map test.</summary>
        private static string Dir => Path.Combine(Path.GetTempPath(), "dwmp-savage");
        private static string FileOf(string tag, string role) => Path.Combine(Dir, $"{tag}-{role}.tsv");
        private static double Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
        private static string Fmt(Vector3 p) => FormattableString.Invariant($"{p.x:0} {p.z:0}");

        private static readonly List<Character> _spawned = new List<Character>();

        // ---- host ------------------------------------------------------------------------------------------------

        /// <summary>
        /// "sv-spawn KIND[,KIND...] [N]" (host): N of each KIND (a prefab under Characters/) around the client's
        /// player, sent after "the player" (the one with the most threat), with what its sprite animators are.
        /// </summary>
        private static IEnumerator Spawn(string[] a, Result r)
        {
            if (a.Length < 1) { r.Fail("usage: sv-spawn KIND[,KIND...] [N]"); yield break; }
            int n = a.Length > 1 ? int.Parse(a[1], CultureInfo.InvariantCulture) : 1;
            var proxy = MapScenarioEnemies.ClientProxy();
            if (proxy == null) { r.Fail("no client player"); yield break; }
            var away = proxy.transform.position - P.transform.position; away.y = 0f;
            var dir = away.sqrMagnitude > 1f ? away.normalized : Vector3.forward;
            var made = new List<Character>();
            int k = 0;
            foreach (string kind in a[0].Split(','))
                for (int i = 0; i < n; i++, k++)
                {
                    if (Resources.Load("Prefabs/Characters/" + kind) == null) { r.Fail($"no prefab Characters/{kind}"); yield break; }
                    var side = Quaternion.Euler(0f, (k - 1) * 35f, 0f) * dir;
                    var spot = MapScenarioEnemies.FreeSpot(proxy.transform.position + side * 400f);
                    Character c;
                    try { c = Singleton<CharacterSpawner>.Instance.spawnCharacterAround(P.gameObject, spot, 1f, kind, nocturnal: false, attackPlayer: true); }
                    catch (Exception e) { r.Fail($"spawning {kind}: {e.GetType().Name}: {e.Message}"); yield break; }
                    if (c == null) { r.Fail($"couldn't spawn {kind}"); yield break; }
                    c.transform.position = spot;
                    made.Add(c);
                    _spawned.Add(c);
                }
            yield return new WaitForSeconds(1f);
            var sb = new StringBuilder();
            foreach (var c in made)
            {
                NetEnemies.Instance.NetIdOf(c, out uint id);
                sb.Append($"{c.name} #{id} at {Fmt(c.transform.position)} ({Core.trueDistance(c.transform.position, proxy.transform.position):0} from the client): {Anatomy(c)}; ");
            }
            r.Info = sb.ToString().TrimEnd(' ', ';');
        }

        /// <summary>A character's sprite animators: path, clip now, its wrap mode, fps and frames, and the legs it drives.</summary>
        internal static string Anatomy(Character c)
        {
            var l = new List<string>();
            foreach (var an in c.GetComponentsInChildren<tk2dSpriteAnimator>(true))
            {
                var clip = an.CurrentClip;
                string attacks = an.Library != null ? string.Join("/", an.Library.clips.Where(x => x != null && x.name.StartsWith("Attack")).Select(x => $"{x.name}:{x.wrapMode}:{x.fps:0}fps:{x.frames?.Length ?? 0}")) : "no library";
                l.Add($"[{PathOf(an, c.transform)}{(an == c.animator ? " main" : "")}{(an.legs != null ? " legs=" + PathOf(an.legs, c.transform) : "")}" +
                      $"{(c.animator != null && c.animator.legs == an ? " (the main's legs)" : "")} clip {clip?.name ?? "none"} {attacks}]");
            }
            return string.Join(" ", l);
        }

        private static string PathOf(tk2dSpriteAnimator an, Transform root)
        {
            if (an.transform == root) return ".";
            string p = an.name;
            for (var t = an.transform.parent; t != null && t != root; t = t.parent) p = t.name + "/" + p;
            return p;
        }

        /// <summary>"sv-clear" (host): what sv-spawn made, removed in every game.</summary>
        private static IEnumerator Clear(string[] a, Result r)
        {
            int n = 0;
            foreach (var c in _spawned) if (c != null) { Object.Destroy(c.gameObject); n++; }
            _spawned.Clear();
            yield return new WaitForSeconds(1.5f);
            r.Info = $"removed {n}";
        }

        // ---- both ------------------------------------------------------------------------------------------------

        private struct S
        {
            public double T;
            public uint Id;
            public string Kind, Path, Clip, State;
            public int Frame, Sprite, Frames;
            public bool Playing;
        }

        /// <summary>
        /// "sv-sample AT SECS TAG [--walk] [--provoke]" (host and client at once): from epoch second AT, every frame for
        /// SECS seconds, each of the host's enemies within 3000 of our player: each sprite animator's clip, frame, the
        /// sprite it shows and whether it plays (the host also: attacking / recovering / hit, and whom it goes for),
        /// written to a file per game. With --walk the client walks 600 away mid-way and comes back; with --provoke
        /// the host's player walks up to the nearest one and hits it (so its threat goes to the host), then walks
        /// back. Fails if, here, an enemy's clip that has more frames kept to one or two of them while playing for a
        /// second or more (the reported "looping one or two frames").
        /// </summary>
        private static IEnumerator Sample(string[] a, Result r)
        {
            if (a.Length < 3) { r.Fail("usage: sv-sample AT SECS TAG [--walk] [--provoke]"); yield break; }
            double at = double.Parse(a[0], CultureInfo.InvariantCulture);
            float secs = float.Parse(a[1], CultureInfo.InvariantCulture);
            string tag = a[2];
            var s = Net.Session.Instance;
            bool host = s != null && s.IsHost;
            while (Now < at) yield return null;
            var samples = new List<S>();
            var home = P.transform.position;
            IEnumerator act = null;
            if (!host && a.Contains("--walk")) act = WalkAway(home, secs);
            if (host && a.Contains("--provoke")) act = Provoke(home, secs);
            if (act != null) Plugin.Instance.StartCoroutine(act);
            double end = at + secs;
            var ne = NetEnemies.Instance;
            var proxy = host ? MapScenarioEnemies.ClientProxy() : null;
            // The client's copies are disabled characters (NetEnemies.Freeze), which AllCharacters skips.
            var chars = new List<Character>();
            double listed = 0;
            while (Now < end)
            {
                double t = Now;
                if (t - listed > 0.5) { chars = Object.FindObjectsOfType<Character>().ToList(); listed = t; }
                foreach (var c in chars)
                {
                    if (c == null || ne == null || !ne.NetIdOf(c, out uint id) || !NetEnemies.InPlay(c)) continue;
                    if (Core.trueDistance(c.transform.position, P.transform.position) > 3000f && (proxy == null || Core.trueDistance(c.transform.position, proxy.transform.position) > 3000f)) continue;
                    string state = "";
                    if (host)
                    {
                        state = (c.attacking ? "A" : "") + (c.recoveringFromAttack ? "R" : "") + (c.recoveringFromHit || c.gettingHit ? "H" : "");
                        var cb = c.target != null ? c.target.GetComponent<CharBase>() : null;
                        state += cb is NetPlayerProxy ? ">c" : cb is Player ? ">h" : c.target != null ? ">o" : ">-";
                        if (c.target != null) state += FormattableString.Invariant($"{Core.trueDistance(c.transform.position, c.target.position):0}");
                    }
                    foreach (var an in c.GetComponentsInChildren<tk2dSpriteAnimator>(false))
                    {
                        var clip = an.CurrentClip;
                        if (clip == null || clip.frames == null) continue;
                        samples.Add(new S
                        {
                            T = t, Id = id, Kind = c.name.Replace("(Clone)", "").Trim(), Path = PathOf(an, c.transform), Clip = clip.name,
                            Frame = an.CurrentFrame, Sprite = an.Sprite != null ? an.Sprite.spriteId : -1, Frames = clip.frames.Length,
                            Playing = an.Playing, State = state,
                        });
                    }
                }
                yield return null;
            }
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllLines(FileOf(tag, host ? "host" : "client"), samples.Select(x => FormattableString.Invariant(
                    $"{x.T:0.000}\t{x.Id}\t{x.Kind}\t{x.Path}\t{x.Clip}\t{x.Frame}\t{x.Sprite}\t{x.Frames}\t{(x.Playing ? 1 : 0)}\t{x.State}")));
            }
            catch (Exception e) { r.Fail($"couldn't write the samples: {e.Message}"); yield break; }
            var (summary, stuck) = Analyse(samples);
            r.Info = $"{samples.Count} samples; {summary}";
            if (stuck.Count > 0) r.Fail($"stuck: {string.Join("; ", stuck)}; {r.Info}");
        }

        private static IEnumerator WalkAway(Vector3 home, float secs)
        {
            yield return new WaitForSeconds(secs * 0.3f);
            yield return Walk(home + new Vector3(600f, 0f, 0f), 40f, detours: 0);
            yield return new WaitForSeconds(secs * 0.15f);
            yield return Walk(home, 40f, detours: 0);
        }

        private static IEnumerator Provoke(Vector3 home, float secs)
        {
            yield return new WaitForSeconds(secs * 0.2f);
            var c = _spawned.Where(x => x != null && NetEnemies.InPlay(x)).OrderBy(x => Core.trueDistance(x.transform.position, P.transform.position)).FirstOrDefault();
            if (c == null) yield break;
            yield return Walk(c.transform.position, 90f, detours: 0);
            if (c != null && NetEnemies.InPlay(c)) AsPlayer(() => c.getHit(3f, P.transform, false, true, false));
            Plugin.Log.LogInfo($"[sv] hit {c?.name} to draw it to the host; its target now {(c != null && c.target != null ? c.target.name : "none")}");
            yield return new WaitForSeconds(secs * 0.4f);
            yield return Walk(home, 60f, detours: 0);
        }

        /// <summary>
        /// Per enemy and animator: the clips it played in order (with the frames each went through), and where a clip
        /// of more than two frames showed only one or two different sprites while playing for a second or more.
        /// </summary>
        private static (string summary, List<string> stuck) Analyse(List<S> samples)
        {
            var stuck = new List<string>();
            var parts = new List<string>();
            foreach (var g in samples.GroupBy(x => (x.Id, x.Path)).OrderBy(g => g.Key.Id))
            {
                var runs = Runs(g.ToList());
                var seq = new List<string>();
                foreach (var run in runs)
                {
                    double dur = run[run.Count - 1].T - run[0].T;
                    var playing = run.Where(x => x.Playing).ToList();
                    int sprites = playing.Select(x => x.Sprite).Distinct().Count();
                    int frames = run.Select(x => x.Frame).Distinct().Count();
                    seq.Add(FormattableString.Invariant($"{run[0].Clip}({dur:0.0}s {frames}/{run[0].Frames}f{(playing.Count == 0 ? " held" : "")}{(run[0].State != "" ? " " + run[0].State : "")})"));
                    double playDur = playing.Count > 1 ? playing[playing.Count - 1].T - playing[0].T : 0;
                    if (run[0].Frames > 2 && playDur >= 1.0 && sprites <= 2)
                        stuck.Add(FormattableString.Invariant($"{run[0].Kind} #{g.Key.Id} {g.Key.Path} {run[0].Clip} played {playDur:0.0}s showing {sprites} sprite(s) of {run[0].Frames} frames (frames {string.Join(",", playing.Select(x => x.Frame).Distinct().Take(6))}){(run[0].State != "" ? ", AI " + run[0].State : "")}"));
                }
                if (seq.Count > 14) seq = seq.Take(6).Concat(new[] { $"... {seq.Count - 12} more ..." }).Concat(seq.Skip(seq.Count - 6)).ToList();
                parts.Add($"{g.First().Kind} #{g.Key.Id} {g.Key.Path}: {string.Join(" > ", seq)}");
            }
            return (string.Join(" | ", parts), stuck);
        }

        /// <summary>Consecutive samples of the same clip.</summary>
        private static List<List<S>> Runs(List<S> l)
        {
            var runs = new List<List<S>>();
            foreach (var x in l.OrderBy(x => x.T))
            {
                if (runs.Count == 0 || runs[runs.Count - 1][0].Clip != x.Clip) runs.Add(new List<S>());
                runs[runs.Count - 1].Add(x);
            }
            return runs;
        }

        private static List<S> Load(string path)
        {
            var l = new List<S>();
            foreach (var line in File.ReadAllLines(path))
            {
                var f = line.Split('\t');
                if (f.Length < 10) continue;
                l.Add(new S
                {
                    T = double.Parse(f[0], CultureInfo.InvariantCulture), Id = uint.Parse(f[1]), Kind = f[2], Path = f[3], Clip = f[4],
                    Frame = int.Parse(f[5]), Sprite = int.Parse(f[6]), Frames = int.Parse(f[7]), Playing = f[8] == "1", State = f[9],
                });
            }
            return l;
        }

        /// <summary>
        /// "sv-compare TAG" (client): our samples against the host's. For each of our samples of an animator, the host's
        /// same animator within the 0.5 s before (what we show comes from the host's last state) must have shown the
        /// same clip and a sprite we show, or a frame within two of ours. Fails if fewer than 85% of an animator's
        /// samples match, or if a clip the host played for 0.3 s or more while we sampled never showed here.
        /// </summary>
        private static IEnumerator Compare(string[] a, Result r)
        {
            if (a.Length < 1) { r.Fail("usage: sv-compare TAG"); yield break; }
            string tag = a[0];
            List<S> host, ours;
            try { host = Load(FileOf(tag, "host")); ours = Load(FileOf(tag, "client")); }
            catch (Exception e) { r.Fail($"couldn't read the samples: {e.Message}"); yield break; }
            var parts = new List<string>();
            foreach (var g in ours.GroupBy(x => (x.Id, x.Path)).OrderBy(g => g.Key.Id))
            {
                var h = host.Where(x => x.Id == g.Key.Id && x.Path == g.Key.Path).OrderBy(x => x.T).ToList();
                if (h.Count == 0) { r.Fail($"#{g.Key.Id} {g.Key.Path}: the host has no samples of it"); continue; }
                var mine = g.Where(x => x.T >= h[0].T + 0.5 && x.T <= h[h.Count - 1].T).ToList();
                if (mine.Count < 20) continue;
                int ok = 0;
                var bad = new Dictionary<string, int>();
                foreach (var x in mine)
                {
                    bool match = h.Any(y => y.T >= x.T - 0.5 && y.T <= x.T + 0.05 && y.Clip == x.Clip
                                            && (y.Sprite == x.Sprite || Math.Abs(y.Frame - x.Frame) <= 2 || Math.Abs(y.Frame - x.Frame) >= x.Frames - 2));
                    if (match) ok++;
                    else
                    {
                        var y0 = h.LastOrDefault(y => y.T <= x.T);
                        string key = $"ours {x.Clip}:{x.Frame} host {y0.Clip}:{y0.Frame}";
                        bad[key] = bad.TryGetValue(key, out int c) ? c + 1 : 1;
                    }
                }
                double rate = ok / (double)mine.Count;
                // Only what the host played while we sampled it too: our samples can stop before the host's (each game
                // ends its own sampling), and a clip that started there after our last sample can't have shown here.
                double from = mine[0].T - 0.5, to = mine[mine.Count - 1].T;
                var hostClips = Runs(h.Where(y => y.T >= from && y.T <= to).ToList()).Where(run => run[run.Count - 1].T - run[0].T >= 0.3).Select(run => run[0].Clip).Distinct().ToList();
                var shown = new HashSet<string>(g.Select(x => x.Clip));
                var missing = hostClips.Where(c => !shown.Contains(c)).ToList();
                string worst = bad.Count == 0 ? "" : " (mismatches: " + string.Join(", ", bad.OrderByDescending(kv => kv.Value).Take(3).Select(kv => $"{kv.Key} x{kv.Value}")) + ")";
                string line = FormattableString.Invariant($"{g.First().Kind} #{g.Key.Id} {g.Key.Path}: {rate:P0} of {mine.Count} match{worst}{(missing.Count > 0 ? "; never shown here: " + string.Join("/", missing) : "")}");
                parts.Add(line);
                if (rate < 0.85 || missing.Count > 0) r.Fail(line);
            }
            if (parts.Count == 0) r.Fail("no animator with enough samples in both games");
            r.Info = string.Join(" | ", parts);
            if (r.Failure != null) r.Failure += " | all: " + r.Info;
        }
    }
}
