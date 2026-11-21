using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using DarkwoodMP.Combat;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;
using Object = UnityEngine.Object;

namespace DarkwoodMP.World
{
    public static partial class MapScenarioSavage
    {
        // ---- both -----------------------------------------------------------

        private struct S
        {
            public double T;
            public uint Id;
            public string Kind, Path, Clip, State;
            public int Frame, Sprite, Frames;
            public bool Playing;
        }

        /// <summary>
        /// "sv-sample AT SECS TAG [--walk] [--provoke]" (host and client at
        /// once): from epoch second AT, every frame for SECS seconds, each of
        /// the host's enemies within 3000 of our player: each sprite animator's
        /// clip, frame, the sprite it shows and whether it plays (the host
        /// also: attacking / recovering / hit, and whom it goes for), written
        /// to a file per game. With --walk the client walks 600 away mid-way
        /// and comes back; with --provoke the host's player walks up to the
        /// nearest one and hits it (so its threat goes to the host), then walks
        /// back. Fails if, here, an enemy's clip that has more frames kept to
        /// one or two of them while playing for a second or more (the reported
        /// "looping one or two frames").
        /// </summary>
        private static IEnumerator Sample(string[] a, Result r)
        {
            if (a.Length < 3)
            {
                r.Fail("usage: sv-sample AT SECS TAG [--walk] [--provoke]");
                yield break;
            }

            double at = double.Parse(a[0], CultureInfo.InvariantCulture);
            float secs = F(a[1]);
            string tag = a[2];
            var s = Net.Session.Instance;
            bool host = s != null && s.IsHost;
            while (Now < at)
            {
                yield return null;
            }

            var samples = new List<S>();
            var home = P.transform.position;
            IEnumerator act = null;
            if (!host && a.Contains("--walk"))
            {
                act = WalkAway(home, secs);
            }

            if (host && a.Contains("--provoke"))
            {
                act = Provoke(home, secs);
            }

            if (act != null)
            {
                Plugin.Instance.StartCoroutine(act);
            }

            double end = at + secs;
            var ne = NetEnemies.Instance;
            NetPlayerProxy proxy = null;
            if (host)
            {
                proxy = MapScenarioEnemies.ClientProxy();
            }

            // The client's copies are disabled characters (NetEnemies.Freeze),
            // which AllCharacters skips.
            var chars = new List<Character>();
            double listed = 0;
            while (Now < end)
            {
                double t = Now;
                if (t - listed > 0.5)
                {
                    chars = Object.FindObjectsOfType<Character>().ToList();
                    listed = t;
                }

                foreach (var c in chars)
                {
                    if (c == null || ne == null || !ne.NetIdOf(c, out uint id)
                        || !NetEnemies.InPlay(c))
                    {
                        continue;
                    }

                    if (Core.trueDistance(
                        c.transform.position, P.transform.position) > 3000f
                        && (proxy == null || Core.trueDistance(
                            c.transform.position,
                            proxy.transform.position) > 3000f))
                    {
                        continue;
                    }

                    string state = "";
                    if (host)
                    {
                        string attack = "";
                        if (c.attacking)
                        {
                            attack = "A";
                        }

                        string recover = "";
                        if (c.recoveringFromAttack)
                        {
                            recover = "R";
                        }

                        string hit = "";
                        if (c.recoveringFromHit || c.gettingHit)
                        {
                            hit = "H";
                        }

                        state = attack + recover + hit;
                        CharBase cb = null;
                        if (c.target != null)
                        {
                            cb = c.target.GetComponent<CharBase>();
                        }

                        if (cb is NetPlayerProxy)
                        {
                            state += ">c";
                        }
                        else if (cb is Player)
                        {
                            state += ">h";
                        }
                        else if (c.target != null)
                        {
                            state += ">o";
                        }
                        else
                        {
                            state += ">-";
                        }

                        if (c.target != null)
                        {
                            float away = Core.trueDistance(
                                c.transform.position,
                                c.target.position);
                            state += FormattableString.Invariant($"{away:0}");
                        }
                    }

                    foreach (var an in c
                        .GetComponentsInChildren<tk2dSpriteAnimator>(false))
                    {
                        var clip = an.CurrentClip;
                        if (clip == null || clip.frames == null)
                        {
                            continue;
                        }

                        S sample = new S
                        {
                            T = t,
                            Id = id,
                            Kind = StateDump.Clean(c.name),
                            Path = PathOf(an, c.transform),
                            Clip = clip.name,
                            Frame = an.CurrentFrame,
                        };

                        if (an.Sprite != null)
                        {
                            sample.Sprite = an.Sprite.spriteId;
                        }
                        else
                        {
                            sample.Sprite = -1;
                        }

                        sample.Frames = clip.frames.Length;
                        sample.Playing = an.Playing;
                        sample.State = state;
                        samples.Add(sample);
                    }
                }

                yield return null;
            }

            try
            {
                Directory.CreateDirectory(Dir);
                string role = "client";
                if (host)
                {
                    role = "host";
                }

                File.WriteAllLines(FileOf(tag, role), samples.Select(x =>
                {
                    int playing = 0;
                    if (x.Playing)
                    {
                        playing = 1;
                    }

                    return FormattableString.Invariant(
                            $"{x.T:0.000}\t{x.Id}\t{x.Kind}\t{x.Path}\t")
                        + FormattableString.Invariant(
                            $"{x.Clip}\t{x.Frame}\t{x.Sprite}\t{x.Frames}\t")
                        + FormattableString.Invariant($"{playing}\t{x.State}");
                }));
            }
            catch (Exception e)
            {
                r.Fail($"couldn't write the samples: {e.Message}");
                yield break;
            }

            var (summary, stuck) = Analyse(samples);
            r.Info = $"{samples.Count} samples; {summary}";
            if (stuck.Count > 0)
            {
                r.Fail($"stuck: {string.Join("; ", stuck)}; {r.Info}");
            }
        }

        private static IEnumerator WalkAway(Vector3 home, float secs)
        {
            yield return new WaitForSeconds(secs * 0.3f);
            yield return Walk(
                home + new Vector3(600f, 0f, 0f), 40f, detours: 0);
            yield return new WaitForSeconds(secs * 0.15f);
            yield return Walk(home, 40f, detours: 0);
        }

        private static IEnumerator Provoke(Vector3 home, float secs)
        {
            yield return new WaitForSeconds(secs * 0.2f);
            var c = _spawned.Where(x => x != null && NetEnemies.InPlay(x))
                .OrderBy(
                    x => Core.trueDistance(
                        x.transform.position,
                        P.transform.position)).FirstOrDefault();
            if (c == null)
            {
                yield break;
            }

            yield return Walk(c.transform.position, 90f, detours: 0);
            if (c != null && NetEnemies.InPlay(c))
            {
                AsPlayer(() => c.getHit(3f, P.transform, false, true, false));
            }

            string targetName = "none";
            if (c != null && c.target != null)
            {
                targetName = c.target.name;
            }

            Character struck = c;
            string name = null;
            if (struck != null)
            {
                name = struck.name;
            }

            Plugin.Log.LogInfo(
                $"[sv] hit {name} to draw it to the host; its target now "
                    + $"{targetName}");
            yield return new WaitForSeconds(secs * 0.4f);
            yield return Walk(home, 60f, detours: 0);
        }
    }
}
