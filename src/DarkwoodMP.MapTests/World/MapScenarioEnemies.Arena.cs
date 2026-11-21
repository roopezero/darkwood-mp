using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DarkwoodMP.Combat;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;
using Object = UnityEngine.Object;

namespace DarkwoodMP.World
{
    /// <summary>Enemies spawned and watched in the host's arena.</summary>
    public static partial class MapScenarioEnemies
    {
        // ---- host -----------------------------------------------------------

        private sealed class Watch
        {
            public string Kind;
            public Character C;
            public uint Id;
            public float SpawnedAt;
            public Vector3 From;
            public readonly HashSet<int> Before = new HashSet<int>();
            public readonly List<Character> Minions = new List<Character>();
            public readonly HashSet<string> Targets = new HashSet<string>();
            public readonly List<string> Timeline = new List<string>();
            public string LastTarget;
            public bool TargetedClient;
            public float Moved;
            public bool Done;
        }

        private static Watch _w;

        /// <summary>
        /// "ee-arena" (client): walk to the fighting spot, unhurt.
        /// </summary>
        private static IEnumerator Arena(string[] a, Result r)
        {
            TraceSpawns = true;
            yield return Walk(ArenaSpot, 60f);
            HealNow();
            r.Info = $"at {Fmt(P.transform.position)}";
        }

        /// <summary>
        /// "ee-spawn KIND" (host): KIND (a prefab under Characters/) beside the
        /// client's player, on the far side from ours, sent after "the player"
        /// (Character.attackPlayer: the one it has most threat for). A kind
        /// that is an NPC here (it runs in every game, not on the host) or has
        /// no prefab is reported "skip".
        /// </summary>
        private static IEnumerator Spawn(string[] a, Result r)
        {
            if (a.Length < 1)
            {
                r.Fail("usage: ee-spawn KIND");
                yield break;
            }

            string kind = a[0];
            TraceSpawns = true;
            var proxy = ClientProxy();
            if (proxy == null)
            {
                r.Fail("no client player");
                yield break;
            }

            if (Resources.Load("Prefabs/Characters/" + kind) == null)
            {
                r.Info = $"skip: no prefab Characters/{kind}";
                yield break;
            }

            var away = proxy.transform.position - P.transform.position;
            away.y = 0f;
            Vector3 heading;
            if (away.sqrMagnitude > 1f)
            {
                heading = away.normalized;
            }
            else
            {
                heading = Vector3.forward;
            }

            var spot = FreeSpot(proxy.transform.position + heading * 450f);
            var w = new Watch
            {
                Kind = kind,
                SpawnedAt = Time.realtimeSinceStartup
            };

            foreach (var c0 in Object.FindObjectsOfType<Character>())
            {
                w.Before.Add(c0.GetInstanceID());
            }

            Character c;
            try
            {
                c = SpawnAt(kind, spot, attack: true);
            }
            catch (Exception e)
            {
                r.Fail($"spawning {kind}: {e.GetType().Name}: {e.Message}");
                yield break;
            }

            if (c == null)
            {
                r.Fail($"couldn't spawn {kind}");
                yield break;
            }

            w.C = c;
            w.From = spot;
            w.Before.Add(c.GetInstanceID());
            _w = w;
            if (c.npc != null)
            {
                r.Info = $"skip: {c.name} is an NPC here (runs in every game)";
                Object.Destroy(c.gameObject);
                _w = null;
                yield break;
            }

            Plugin.Instance.StartCoroutine(Monitor(w));
            yield return new WaitForSeconds(1f);
            Coop.Current.Enemies.NetIdOf(c, out w.Id);
            r.Info =
                $"{c.name} #{w.Id} at {Fmt(spot)} "
                    + $"({Core.trueDistance(spot, proxy.transform.position):0} "
                    + "from the client, "
                    + $"{Core.trueDistance(spot, P.transform.position):0} from "
                    + $"us), health {c.health:0}, {Traits(c)}; children: "
                    + $"{Children(c)}";
        }

        /// <summary>
        /// "ee-solo KIND" (host): KIND beside our own player, the client far
        /// off: whether it hurts our player in the game's own single-player way
        /// within 15 s (to tell a kind that never attacks from one that can't
        /// reach a joiner). It's killed after.
        /// </summary>
        private static IEnumerator Solo(string[] a, Result r)
        {
            string kind = "Spider01";
            if (a.Length > 0)
            {
                kind = a[0];
            }

            var spot =
                FreeSpot(P.transform.position + new Vector3(0f, 0f, -400f));
            var c = SpawnAt(kind, spot, attack: true);
            if (c == null)
            {
                r.Fail($"couldn't spawn {kind}");
                yield break;
            }

            float lost = 0f,
                hp = P.health,
                until = Time.realtimeSinceStartup + 15f;
            while (Time.realtimeSinceStartup < until && lost <= 0f
                && c != null && c.alive)
            {
                if (P.health < hp)
                {
                    lost += hp - P.health;
                }

                hp = P.health;
                yield return new WaitForSeconds(0.1f);
            }

            string text = "gone";
            if (c != null)
            {
                float away = Core.trueDistance(
                    c.transform.position,
                    P.transform.position);
                text = $"{away:0} from us, alive {c.alive}";
            }

            r.Info =
                $"{kind} beside our player: it hurt us for {lost:0}; {text}";
            HealNow();
            if (c != null)
            {
                Kill(c);
            }

            yield return new WaitForSeconds(1f);
            if (c != null)
            {
                Object.Destroy(c.gameObject);
            }
        }

        /// <summary>
        /// A character's child objects that are items (with their components):
        /// what the dumps' items list shows.
        /// </summary>
        internal static string Children(Character c)
        {
            var l = new List<string>();
            foreach (var it in c.GetComponentsInChildren<Item>(true))
            {
                if (it != null && it.gameObject != c.gameObject)
                {
                    string text = " (off)";
                    if (it.gameObject.activeInHierarchy)
                    {
                        text = "";
                    }

                    string name = it.name;
                    string components = string.Join("/",
                        it.GetComponents<MonoBehaviour>().Where(m => m != null)
                            .Select(m =>
                    {
                        string type = m.GetType().Name;
                        string off = "-off";
                        if (m.enabled)
                        {
                            off = "";
                        }

                        return type + off;
                    }));

                    l.Add($"{name}{text} [{components}]");
                }
            }

            if (l.Count == 0)
            {
                return "no items";
            }

            return string.Join(", ", l);
        }

        /// <summary>
        /// What's special about a character (the components and flags behind
        /// its special behaviours).
        /// </summary>
        private static string Traits(Character c)
        {
            var t = new List<string>();
            if (c.flier != null)
            {
                t.Add("flier");
            }

            if (c.banshee)
            {
                t.Add("banshee");
            }

            if (c.summonsAfterEscaping)
            {
                t.Add("summons");
            }

            if (c.GetComponent<Shooter>() != null)
            {
                t.Add("shooter");
            }

            if (c.GetComponent<ShadowArmor>() != null)
            {
                t.Add("shadowArmor");
            }

            if (c.hasPreDeath)
            {
                t.Add("preDeath");
            }

            if (c.attacks != null)
            {
                t.Add(
                    "attacks " + string.Join(
                        "/",
                        c.attacks.Where(x => x != null).Select(x => x.name)));
            }

            t.Add("aggr " + c.aggressiveness);
            if (c.immobile)
            {
                t.Add("immobile");
            }

            if (c.dontMove)
            {
                t.Add("dontMove");
            }

            if (c.onlyAttackPlayer)
            {
                t.Add("onlyAttackPlayer");
            }

            if (c.constantlyAttackPlayer)
            {
                t.Add("constantlyAttackPlayer");
            }

            if (c.damagesAroundMe)
            {
                t.Add("damagesAroundMe");
            }

            if (c.dieOnContactWithTarget)
            {
                t.Add("dieOnContact");
            }

            if (c.sleeping)
            {
                t.Add("sleeping");
            }

            t.Add("behaviour " + c.behaviour);
            t.Add(
                $"view {c.farViewDistance}/{c.nearViewDistance} "
                    + $"x{c.aniSightRangeModifier:0.##}");
            if (c.currentActivity != null && c.currentActivity.isActive)
            {
                t.Add("activity " + c.currentActivity.type);
            }

            if (c.activities != null && c.activities.Count > 0)
            {
                t.Add(
                    "activities " + string.Join(
                        "/", c.activities.Select(x => x.type)));
            }

            t.Add(
                "components " + string.Join(
                    "/",
                    c.GetComponents<MonoBehaviour>().Where(m => m != null)
                        .Select(m => m.GetType().Name).Distinct()));
            return string.Join(", ", t);
        }

        /// <summary>
        /// Host, while the fight lasts: whom the enemy goes for, how far it
        /// moved, and what it spawned.
        /// </summary>
        private static IEnumerator Monitor(Watch w)
        {
            while (!w.Done && Time.realtimeSinceStartup - w.SpawnedAt < 200f)
            {
                var c = w.C;
                if (c != null)
                {
                    if (c.target != null)
                    {
                        var cb = c.target.GetComponent<CharBase>();
                        string who;
                        if (cb is NetPlayerProxy np)
                        {
                            who = "client " + np.OwnerName;
                        }
                        else if (cb is Player)
                        {
                            who = "host player";
                        }
                        else
                        {
                            who = c.target.name;
                        }

                        w.Targets.Add(who);
                        if (who != w.LastTarget)
                        {
                            float at = Time.realtimeSinceStartup - w.SpawnedAt;
                            w.Timeline.Add($"{at:0.0}s {who} ({c.behaviour})");
                        }

                        w.LastTarget = who;
                        if (cb is NetPlayerProxy)
                        {
                            w.TargetedClient = true;
                        }
                    }

                    w.Moved = Mathf.Max(
                        w.Moved,
                        Core.trueDistance(c.transform.position, w.From));
                }

                foreach (var o in Object.FindObjectsOfType<Character>())
                {
                    if (o == null || w.Before.Contains(o.GetInstanceID())
                        || o.npc != null)
                    {
                        continue;
                    }

                    w.Before.Add(o.GetInstanceID());
                    if (Core.trueDistance(o.transform.position, w.From) < 3000f)
                    {
                        w.Minions.Add(o);
                    }
                }

                yield return new WaitForSeconds(0.2f);
            }
        }

        /// <summary>
        /// "ee-check [--targets]" (host): the enemy and all it spawned are dead
        /// here; with --targets, it went for the client's player. Anything
        /// still alive is killed (so it doesn't spoil the next fight) and fails
        /// the step.
        /// </summary>
        private static IEnumerator Check(string[] a, Result r)
        {
            var w = _w;
            if (w == null)
            {
                r.Info = "nothing spawned (skipped)";
                yield break;
            }

            yield return new WaitForSeconds(0.5f);
            w.Done = true;
            var all = new List<Character> { w.C }.Concat(w.Minions).ToList();
            var alive =
                all.Where(c => c != null && NetEnemies.InPlay(c)).ToList();
            string minions;
            if (w.Minions.Count == 0)
            {
                minions = "none";
            }
            else
            {
                minions = string.Join(", ", w.Minions.Select(m =>
                {
                    if (m == null)
                    {
                        return "(gone)";
                    }

                    string name = m.name;
                    string number = "";
                    if (Coop.Current.Enemies.NetIdOf(m, out uint i))
                    {
                        number = " #" + i;
                    }

                    string status = "dead";
                    if (NetEnemies.InPlay(m))
                    {
                        status = "alive";
                    }

                    return $"{name}{number} {status}";
                }));
            }

            string summary =
                $"{w.Kind}: went for [{string.Join(", ", w.Targets)}], moved "
                    + $"{w.Moved:0}, spawned: {minions}, ";
            string state;
            if (w.C == null)
            {
                state = "gone";
            }
            else
            {
                state =
                    $"alive {w.C.alive}, health {w.C.health:0}, children "
                        + $"{Children(w.C)}";
            }

            r.Info = summary + state;
            if (w.Timeline.Count > 1)
            {
                r.Info += $"; targets: {string.Join(", ", w.Timeline.Take(8))}";
            }

            if (a.Contains("--targets") && !w.TargetedClient)
            {
                r.Fail($"it never went for the client's player: {r.Info}");
            }

            if (alive.Count > 0)
            {
                r.Fail(
                    "still alive here: "
                        + $"{string.Join(", ", alive.Select(c => c.name))}; "
                        + $"{r.Info}");
                foreach (var c in alive)
                {
                    Kill(c);
                }
            }
        }

        /// <summary>
        /// "ee-clear" (host): what the last fight left (corpses, spawned
        /// things) removed in every game.
        /// </summary>
        private static IEnumerator Clear(string[] a, Result r)
        {
            var w = _w;
            _w = null;
            int n = 0;
            var gone = new HashSet<Character>();
            if (w != null)
            {
                foreach (var c in new List<Character> { w.C }.Concat(w.Minions))
                {
                    if (c != null && gone.Add(c))
                    {
                        Object.Destroy(c.gameObject);
                        n++;
                    }
                }
            }

            // And whatever else came while the fights went on (a banshee's
            // babies, the world's own animals), near the client: anything not
            // here when the clock was first held.
            var proxy = ClientProxy();
            var others = new List<string>();
            if (proxy != null && _baseline.Count > 0)
            {
                foreach (var c in Object.FindObjectsOfType<Character>())
                {
                    if (c != null && c.npc == null && !gone.Contains(c)
                        && !_baseline.Contains(c.GetInstanceID())
                        && Core.trueDistance(
                            c.transform.position,
                            proxy.transform.position) < 3000f && gone.Add(c))
                    {
                        others.Add(c.name);
                        Object.Destroy(c.gameObject);
                        n++;
                    }
                }
            }

            yield return new WaitForSeconds(1.5f);
            string othersNote = "";
            if (others.Count > 0)
            {
                othersNote = $" (and of others: {string.Join(", ", others)})";
            }

            r.Info = $"removed {n}" + othersNote;
        }
    }
}
