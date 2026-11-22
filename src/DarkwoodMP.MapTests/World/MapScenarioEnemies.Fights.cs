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
    /// <summary>The client's fights with the host's enemies.</summary>
    public static partial class MapScenarioEnemies
    {
        // ---- client ---------------------------------------------------------

        // client: the net ID of the kind we fought
        private static uint _fought;

        /// <summary>
        /// A copy of one of the host's enemies here (and its net ID).
        /// </summary>
        private static IEnumerable<(Character c, uint id)> HostEnemies()
        {
            NetEnemies ne = null;
            if (Coop.Current != null)
            {
                ne = Coop.Current.Enemies;
            }

            if (ne == null)
            {
                yield break;
            }

            foreach (var c in Object.FindObjectsOfType<Character>())
            {
                if (c != null && ne.NetIdOf(c, out uint id))
                {
                    yield return (c, id);
                }
            }
        }

        /// <summary>
        /// "ee-fight KIND [--hurts] [--moves]" (client): the host's KIND shows
        /// here; with --moves it moves, with --hurts it hurts our player
        /// (waited for up to 12 s, standing still; with --twice, two hits: one
        /// pooled sensor must hurt again), with --throws one of its projectiles
        /// shows here (EnemyProjectile or EnemySensorThrown). Then our player
        /// hits it, and anything of the host's it spawned near us, with a
        /// pitchfork until none are left alive (healed when low).
        /// </summary>
        private static IEnumerator Fight(string[] a, Result r)
        {
            var p = P;
            string kind = "";
            if (a.Length > 0)
            {
                kind = a[0];
            }

            bool hurts = a.Contains("--hurts"),
                moves = a.Contains("--moves"),
                throws = a.Contains("--throws");
            int needHits = 1;
            if (a.Contains("--twice"))
            {
                needHits = 2;
            }

            int shown0 = Coop.Current.Landings.Shown;
            Character c = null;
            uint id = 0;
            // the world's own, here before it: not ours to fight
            var earlier = new HashSet<uint>(HostEnemies()
                    .Where(e => !IsKind(e.c, kind)).Select(e => e.id));
            float until = Time.realtimeSinceStartup + 8f;
            while (c == null && Time.realtimeSinceStartup < until)
            {
                foreach (var (o, i) in HostEnemies())
                {
                    if (IsKind(o, kind) && NetEnemies.InPlay(o)
                        && (c == null || Core.trueDistance(
                            o.transform.position,
                            p.transform.position) < Core.trueDistance(
                                c.transform.position, p.transform.position)))
                    {
                        c = o;
                        id = i;
                    }
                }

                if (c == null)
                {
                    yield return new WaitForSeconds(0.3f);
                }
            }

            if (c == null)
            {
                r.Fail($"no copy of the host's {kind} here");
                yield break;
            }

            _fought = id;
            Vector3 from = c.transform.position;
            float moved = 0f,
                lost = 0f,
                hp = p.health,
                closest = float.MaxValue;
            int hits = 0;
            // Let it come and attack.
            float wait = 1f;
            if (hurts)
            {
                if (needHits > 1)
                {
                    wait = 18f;
                }
                else
                {
                    wait = 12f;
                }
            }
            else if (moves)
            {
                wait = 6f;
            }

            float waitUntil = Time.realtimeSinceStartup + wait;
            while (Time.realtimeSinceStartup < waitUntil && c != null
                && NetEnemies.InPlay(c))
            {
                moved = Mathf.Max(
                    moved, Core.trueDistance(c.transform.position, from));
                closest = Mathf.Min(
                    closest,
                    Core.trueDistance(
                        c.transform.position, p.transform.position));
                if (p.health < hp)
                {
                    lost += hp - p.health;
                    hits++;
                }

                hp = p.health;
                if (p.health < p.maxHealth * 0.5f)
                {
                    HealNow();
                    hp = p.health;
                }

                if (hurts && hits >= needHits && (!moves || moved > 30f)
                    && (!throws || Coop.Current.Landings.Shown > shown0))
                {
                    break;
                }

                yield return new WaitForSeconds(0.1f);
            }

            int thrown = Coop.Current.Landings.Shown - shown0;
            string came =
                $"{c.name} #{id}: moved {moved:0}, came within {closest:0}, "
                    + $"hurt us {hits} time(s) for {lost:0}, {thrown} "
                    + $"projectile(s) shown (children here: {Children(c)})";
            string weapon = "pitchfork";
            if (a.Contains("--cut"))
            {
                weapon = CutWeapon();
            }

            if (weapon == null)
            {
                r.Fail("no melee weapon in the game cuts in half");
                yield break;
            }

            yield return TakeOut(p, weapon, r);
            if (r.Failure != null)
            {
                yield break;
            }

            // Hit it and whatever of the host's is alive near us until
            // nothing is left: a swing of the weapon (the game's own melee hit,
            // when it lands), then hits through the same path (Character.getHit
            // by our player, which goes to the host as EnemyHit) for the most
            // the weapon does (the Strong skill doubles it), so a fight ends in
            // seconds even with something that dodges, flies or runs.
            var killed = new HashSet<string>();
            var seen = new HashSet<uint> { id };
            var swung = new HashSet<uint>();
            int swings = 0, direct = 0;
            float dmg = HitDamage(p);
            bool cut = a.Contains("--cut");
            float giveUp = Time.realtimeSinceStartup + FightTime,
                lastSeen = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup < giveUp)
            {
                (Character c, uint id) best = default;
                float bestD = float.MaxValue;
                foreach (var e in HostEnemies())
                {
                    if (!NetEnemies.InPlay(e.c) || earlier.Contains(e.id))
                    {
                        continue;
                    }

                    float d = Core.trueDistance(
                        e.c.transform.position, p.transform.position);
                    if (d > 2500f)
                    {
                        continue;
                    }

                    seen.Add(e.id);
                    if (d < bestD)
                    {
                        bestD = d;
                        best = e;
                    }
                }

                if (best.c == null)
                {
                    // Nothing left: unless something it left (a half cut off, a
                    // minion) shows up within a moment.
                    float sinceSeen = Time.realtimeSinceStartup - lastSeen;
                    float grace = 3f;
                    if (a.Contains("--minions"))
                    {
                        grace = 8f;
                    }

                    if (sinceSeen > grace)
                    {
                        break;
                    }

                    yield return new WaitForSeconds(0.3f);
                    continue;
                }

                lastSeen = Time.realtimeSinceStartup;
                if (p.health < p.maxHealth * 0.5f)
                {
                    HealNow();
                }

                if (p.health < hp)
                {
                    lost += hp - p.health;
                    hits++;
                }

                bool first = swung.Add(best.id);
                float nearEnough = 300f;
                if (first)
                {
                    nearEnough = 90f;
                }

                if (bestD > nearEnough)
                {
                    float within = 250f;
                    if (first)
                    {
                        within = 70f;
                    }

                    yield return Walk(
                        best.c.transform.position, within, detours: 0);
                }

                if (p.health < hp)
                {
                    lost += hp - p.health;
                    hits++;
                }

                if (best.c == null || !NetEnemies.InPlay(best.c))
                {
                    continue;
                }

                if (first || (cut && best.id == id))
                {
                    Swing(p, best.c.transform.position);
                    swings++;
                    yield return new WaitForSeconds(0.45f);
                }
                else
                {
                    var bc = best.c;
                    AsPlayer(
                        () => bc.getHit(
                            dmg, p.transform, false, true, true, true, false,
                            false, false));
                    direct++;
                    yield return new WaitForSeconds(0.25f);
                }

                hp = p.health;
            }

            var left = HostEnemies()
                .Where(
                    e => NetEnemies.InPlay(e.c) && !earlier.Contains(e.id)
                        && Core.trueDistance(
                            e.c.transform.position,
                            p.transform.position) < 2500f)
                .Select(e => $"{e.c.name} #{e.id}").ToList();
            var others = seen.Where(x => x != id).ToList();
            HealNow();
            int count = seen.Count;
            string spawned;
            if (others.Count == 0)
            {
                spawned = "nothing it spawned";
            }
            else
            {
                spawned = "spawned " + string.Join(", ", HostEnemies()
                        .Where(e => others.Contains(e.id))
                        .Select(e => $"{e.c.name} #{e.id}"));
            }

            r.Info =
                $"{came}; then {swings} swing(s) and {direct} hit(s) of "
                    + $"{dmg:0}; the host's here: {count} ({spawned}), our "
                    + $"player lost {lost:0} in all";
            if (moves && moved < 30f)
            {
                r.Fail($"it didn't move here: {r.Info}");
            }
            else if (hurts && lost <= 0f)
            {
                r.Fail($"it never hurt our player: {r.Info}");
            }
            else if (hurts && hits < needHits)
            {
                r.Fail(
                    $"it hurt our player only {hits} time(s), not {needHits}: "
                        + $"{r.Info}");
            }
            else if (throws && thrown == 0)
            {
                r.Fail($"none of its projectiles showed here: {r.Info}");
            }
            else if (left.Count > 0)
            {
                r.Fail($"still alive: {string.Join(", ", left)}; {r.Info}");
            }
        }
    }
}
