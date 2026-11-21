using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using DarkwoodMP.Combat;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;

namespace DarkwoodMP.World
{
    public static partial class MapScenarioSavage
    {
        /// <summary>
        /// Per enemy and animator: the clips it played in order (with the
        /// frames each went through), and where a clip of more than two frames
        /// showed only one or two different sprites while playing for a second
        /// or more.
        /// </summary>
        private static (string summary, List<string> stuck) Analyse(
            List<S> samples)
        {
            var stuck = new List<string>();
            var parts = new List<string>();
            foreach (var g in samples.GroupBy(x => (x.Id, x.Path)).OrderBy(
                g => g.Key.Id))
            {
                var runs = Runs(g.ToList());
                var seq = new List<string>();
                foreach (var run in runs)
                {
                    double dur = run[run.Count - 1].T - run[0].T;
                    var playing = run.Where(x => x.Playing).ToList();
                    int sprites =
                        playing.Select(x => x.Sprite).Distinct().Count();
                    int frames = run.Select(x => x.Frame).Distinct().Count();
                    string text = "";
                    if (playing.Count == 0)
                    {
                        text = " held";
                    }

                    string stateNote = "";
                    if (run[0].State != "")
                    {
                        stateNote = " " + run[0].State;
                    }

                    seq.Add(
                        FormattableString.Invariant(
                                $"{run[0].Clip}({dur:0.0}s {frames}/")
                            + FormattableString.Invariant(
                                $"{run[0].Frames}f{text}{stateNote})"));
                    double playDur = 0;
                    if (playing.Count > 1)
                    {
                        playDur = playing[playing.Count - 1].T - playing[0].T;
                    }

                    if (run[0].Frames > 2 && playDur >= 1.0 && sprites <= 2)
                    {
                        string shown = string.Join(
                            ",",
                            playing.Select(x => x.Frame).Distinct().Take(6));
                        string line =
                            FormattableString.Invariant(
                                $"{run[0].Kind} #{g.Key.Id} {g.Key.Path} ")
                            + FormattableString.Invariant(
                                $"{run[0].Clip} played {playDur:0.0}s ")
                            + FormattableString.Invariant(
                                $"showing {sprites} sprite(s) ")
                            + FormattableString.Invariant(
                                $"of {run[0].Frames} frames (frames {shown})");
                        if (run[0].State != "")
                        {
                            line += ", AI " + run[0].State;
                        }

                        stuck.Add(line);
                    }
                }

                if (seq.Count > 14)
                {
                    seq = seq.Take(6)
                        .Concat(new[] { $"... {seq.Count - 12} more ..." })
                        .Concat(seq.Skip(seq.Count - 6)).ToList();
                }

                parts.Add(
                    $"{g.First().Kind} #{g.Key.Id} {g.Key.Path}: "
                        + $"{string.Join(" > ", seq)}");
            }

            return (string.Join(" | ", parts), stuck);
        }

        /// <summary>Consecutive samples of the same clip.</summary>
        private static List<List<S>> Runs(List<S> l)
        {
            var runs = new List<List<S>>();
            foreach (var x in l.OrderBy(x => x.T))
            {
                if (runs.Count == 0 || runs[runs.Count - 1][0].Clip != x.Clip)
                {
                    runs.Add(new List<S>());
                }

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
                if (f.Length < 10)
                {
                    continue;
                }

                l.Add(new S
                {
                    T = double.Parse(f[0], CultureInfo.InvariantCulture),
                    Id = uint.Parse(f[1]),
                    Kind = f[2],
                    Path = f[3],
                    Clip = f[4],
                    Frame = int.Parse(f[5]),
                    Sprite = int.Parse(f[6]),
                    Frames = int.Parse(f[7]),
                    Playing = f[8] == "1",
                    State = f[9],
                });
            }

            return l;
        }

        /// <summary>
        /// "sv-compare TAG" (client): our samples against the host's. For each
        /// of our samples of an animator, the host's same animator within the
        /// 0.5 s before (what we show comes from the host's last state) must
        /// have shown the same clip and a sprite we show, or a frame within two
        /// of ours. Fails if fewer than 85% of an animator's samples match, or
        /// if a clip the host played for 0.3 s or more while we sampled never
        /// showed here.
        /// </summary>
        private static IEnumerator Compare(string[] a, Result r)
        {
            if (a.Length < 1)
            {
                r.Fail("usage: sv-compare TAG");
                yield break;
            }

            string tag = a[0];
            List<S> host, ours;
            try
            {
                host = Load(FileOf(tag, "host"));
                ours = Load(FileOf(tag, "client"));
            }
            catch (Exception e)
            {
                r.Fail($"couldn't read the samples: {e.Message}");
                yield break;
            }

            var parts = new List<string>();
            foreach (var g in ours.GroupBy(x => (x.Id, x.Path)).OrderBy(
                g => g.Key.Id))
            {
                var h = host.Where(
                    x => x.Id == g.Key.Id
                        && x.Path == g.Key.Path).OrderBy(x => x.T).ToList();
                if (h.Count == 0)
                {
                    r.Fail(
                        $"#{g.Key.Id} {g.Key.Path}: the host has no samples of "
                            + "it");
                    continue;
                }

                var mine = g.Where(
                    x => x.T >= h[0].T + 0.5
                        && x.T <= h[h.Count - 1].T).ToList();
                if (mine.Count < 20)
                {
                    continue;
                }

                int ok = 0;
                var bad = new Dictionary<string, int>();
                foreach (var x in mine)
                {
                    bool match = h.Any(
                        y => y.T >= x.T - 0.5 && y.T <= x.T + 0.05
                            && y.Clip == x.Clip
                            && (y.Sprite == x.Sprite
                                || Math.Abs(y.Frame - x.Frame) <= 2
                                || Math.Abs(y.Frame - x.Frame)
                                    >= x.Frames - 2));
                    if (match)
                    {
                        ok++;
                    }
                    else
                    {
                        var y0 = h.LastOrDefault(y => y.T <= x.T);
                        string key =
                            $"ours {x.Clip}:{x.Frame} host "
                                + $"{y0.Clip}:{y0.Frame}";
                        if (bad.TryGetValue(key, out int c))
                        {
                            bad[key] = c + 1;
                        }
                        else
                        {
                            bad[key] = 1;
                        }
                    }
                }

                double rate = ok / (double)mine.Count;
                // Only what the host played while we sampled it too: our
                // samples can stop before the host's (each game ends its own
                // sampling), and a clip that started there after our last
                // sample can't have shown here.
                double from = mine[0].T - 0.5, to = mine[mine.Count - 1].T;
                var hostClips = Runs(h.Where(y => y.T >= from && y.T <= to)
                        .ToList())
                    .Where(run => run[run.Count - 1].T - run[0].T >= 0.3)
                    .Select(run => run[0].Clip).Distinct().ToList();
                var shown = new HashSet<string>(g.Select(x => x.Clip));
                var missing = hostClips.Where(c => !shown.Contains(c)).ToList();
                string worst;
                if (bad.Count == 0)
                {
                    worst = "";
                }
                else
                {
                    worst = " (mismatches: " + string.Join(
                        ", ",
                        bad.OrderByDescending(kv => kv.Value).Take(3)
                            .Select(kv => $"{kv.Key} x{kv.Value}")) + ")";
                }

                string line =
                    FormattableString.Invariant(
                        $"{g.First().Kind} #{g.Key.Id} {g.Key.Path}: ")
                    + FormattableString.Invariant(
                        $"{rate:P0} of {mine.Count} match{worst}");
                if (missing.Count > 0)
                {
                    line += "; never shown here: " + string.Join("/", missing);
                }

                parts.Add(line);
                if (rate < 0.85 || missing.Count > 0)
                {
                    r.Fail(line);
                }
            }

            if (parts.Count == 0)
            {
                r.Fail("no animator with enough samples in both games");
            }

            r.Info = string.Join(" | ", parts);
            if (r.Failure != null)
            {
                r.Failure += " | all: " + r.Info;
            }
        }
    }
}
