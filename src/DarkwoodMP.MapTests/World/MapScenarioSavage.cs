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
    /// Map test steps for how the savages (the Villager kinds) and, as the
    /// control, a dog are animated while they attack
    /// (tools/tests/map_scenario_savage.py). Players reported savages that,
    /// attacking a player, looped one or two frames instead of their attack.
    /// The host spawns them beside the client's player; both games then sample,
    /// every frame for some seconds, each of the host's enemies' sprite
    /// animators (clip, frame, the sprite shown, playing or not; on the host
    /// also its AI state and target) into a file each, and check their own
    /// samples for a clip stuck on one or two frames; the client then compares
    /// its samples with the host's.
    /// </summary>
    public static partial class MapScenarioSavage
    {
        private static Player P => Player.Instance;

        public static void Register()
        {
            Add("sv-spawn", Spawn, timeout: 15f);
            Add("sv-sample", Sample, timeout: 60f);
            Add("sv-compare", Compare, timeout: 15f);
            Add("sv-clear", Clear, timeout: 10f);
        }

        /// <summary>
        /// Where both games write their samples: one machine runs both in the
        /// map test.
        /// </summary>
        private static string Dir =>
            Path.Combine(Path.GetTempPath(), "dwmp-savage");
        private static string FileOf(string tag, string role) =>
            Path.Combine(Dir, $"{tag}-{role}.tsv");
        internal static double Now =>
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;

        private static readonly List<Character> _spawned =
            new List<Character>();

        // ---- host -----------------------------------------------------------

        /// <summary>
        /// "sv-spawn KIND[,KIND...] [N]" (host): N of each KIND (a prefab under
        /// Characters/) around the client's player, sent after "the player"
        /// (the one with the most threat), with what its sprite animators are.
        /// </summary>
        private static IEnumerator Spawn(string[] a, Result r)
        {
            if (a.Length < 1)
            {
                r.Fail("usage: sv-spawn KIND[,KIND...] [N]");
                yield break;
            }

            int n = 1;
            if (a.Length > 1)
            {
                n = int.Parse(a[1], CultureInfo.InvariantCulture);
            }

            var proxy = MapScenarioEnemies.ClientProxy();
            if (proxy == null)
            {
                r.Fail("no client player");
                yield break;
            }

            var away = proxy.transform.position - P.transform.position;
            away.y = 0f;
            Vector3 dir;
            if (away.sqrMagnitude > 1f)
            {
                dir = away.normalized;
            }
            else
            {
                dir = Vector3.forward;
            }

            var made = new List<Character>();
            int k = 0;
            foreach (string kind in a[0].Split(','))
            {
                for (int i = 0; i < n; i++, k++)
                {
                    if (Resources.Load("Prefabs/Characters/" + kind) == null)
                    {
                        r.Fail($"no prefab Characters/{kind}");
                        yield break;
                    }

                    var side = Quaternion.Euler(0f, (k - 1) * 35f, 0f) * dir;
                    var spot = MapScenarioEnemies.FreeSpot(
                        proxy.transform.position + side * 400f);
                    Character c;
                    try
                    {
                        c = Singleton<CharacterSpawner>.Instance
                            .spawnCharacterAround(
                                P.gameObject, spot, 1f, kind, nocturnal: false,
                                attackPlayer: true);
                    }
                    catch (Exception e)
                    {
                        r.Fail(
                            $"spawning {kind}: {e.GetType().Name}: "
                                + $"{e.Message}");
                        yield break;
                    }

                    if (c == null)
                    {
                        r.Fail($"couldn't spawn {kind}");
                        yield break;
                    }

                    c.transform.position = spot;
                    made.Add(c);
                    _spawned.Add(c);
                }
            }

            yield return new WaitForSeconds(1f);
            var sb = new StringBuilder();
            foreach (var c in made)
            {
                NetEnemies.Instance.NetIdOf(c, out uint id);
                float range = Core.trueDistance(
                    c.transform.position,
                    proxy.transform.position);
                sb.Append(
                    $"{c.name} #{id} at {Fmt(c.transform.position)} "
                        + $"({range:0} from the client): {Anatomy(c)}; ");
            }

            r.Info = sb.ToString().TrimEnd(' ', ';');
        }

        /// <summary>
        /// A character's sprite animators: path, clip now, its wrap mode, fps
        /// and frames, and the legs it drives.
        /// </summary>
        private static string Anatomy(Character c)
        {
            var l = new List<string>();
            foreach (var an in c.GetComponentsInChildren<tk2dSpriteAnimator>(
                true))
            {
                var clip = an.CurrentClip;
                string attacks = "no library";
                if (an.Library != null)
                {
                    attacks = string.Join("/", an.Library.clips.Where(
                        x => x != null
                            && x.name.StartsWith("Attack")).Select(x =>
                    {
                        tk2dSpriteAnimationFrame[] frames = x.frames;
                        int length = 0;
                        if (frames != null)
                        {
                            length = frames.Length;
                        }

                        return $"{x.name}:{x.wrapMode}:{x.fps:0}fps:{length}";
                    }));
                }

                string path = PathOf(an, c.transform);
                string main = "";
                if (an == c.animator)
                {
                    main = " main";
                }

                string legs = "";
                if (an.legs != null)
                {
                    legs = " legs=" + PathOf(an.legs, c.transform);
                }

                string mainLegs = "";
                if (c.animator != null && c.animator.legs == an)
                {
                    mainLegs = " (the main's legs)";
                }

                string clipName = null;
                if (clip != null)
                {
                    clipName = clip.name;
                }

                if (clipName == null)
                {
                    clipName = "none";
                }

                l.Add($"[{path}{main}{legs}" +
                      $"{mainLegs} clip {clipName} {attacks}]");
            }

            return string.Join(" ", l);
        }

        private static string PathOf(tk2dSpriteAnimator an, Transform root)
        {
            if (an.transform == root)
            {
                return ".";
            }

            string p = an.name;
            for (var t = an.transform.parent; t != null
                && t != root; t = t.parent)
            {
                p = t.name + "/" + p;
            }

            return p;
        }

        /// <summary>
        /// "sv-clear" (host): what sv-spawn made, removed in every game.
        /// </summary>
        private static IEnumerator Clear(string[] a, Result r)
        {
            int n = 0;
            foreach (var c in _spawned)
            {
                if (c != null)
                {
                    Object.Destroy(c.gameObject);
                    n++;
                }
            }

            _spawned.Clear();
            yield return new WaitForSeconds(1.5f);
            r.Info = $"removed {n}";
        }
    }
}
