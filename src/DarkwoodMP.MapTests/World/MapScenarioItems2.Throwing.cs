using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;
using Object = UnityEngine.Object;

namespace DarkwoodMP.World
{
    /// <summary>Things thrown, and seen landing in the other game.</summary>
    public static partial class MapScenarioItems2
    {
        // ---- throwing -------------------------------------------------------

        private static GameObject _thrown;

        /// <summary>
        /// "i2-throw TYPE AT": throw TYPE at AT (a dog's name, or a spot) the
        /// way the game does: take it out, aim (the held item, as Player's
        /// aiming makes it for a throwable) and attack (Player.attack calls
        /// Player.throwItem), with the mouse pointing at AT. Waits for it to
        /// land.
        /// </summary>
        private static IEnumerator Throw(string[] a, Result r)
        {
            var p = P;
            string type = a[0];
            string at = "fire";
            if (a.Length > 1)
            {
                at = a[1];
            }

            Character dog = null;
            if (at == "meat" || at == "gun")
            {
                yield return WaitDog(at, c => dog = c);
                if (dog == null)
                {
                    r.Fail($"no {at} dog here");
                    yield break;
                }
            }

            Vector3 target;
            if (dog != null)
            {
                target = dog.transform.position;
            }
            else
            {
                target = MapScenarioEnemies.FreeSpot(Spot(at));
            }

            float dist = 250f;
            if (type == "gasBomb")
            {
                dist = 340f; // (out of a gas cloud's reach)
            }

            yield return Walk(Stand(target, dist), 30f);
            if (dog != null)
            {
                target = dog.transform.position;
            }

            try
            {
                yield return TakeOut(type, r);
                if (r.Failure != null)
                {
                    yield break;
                }

                if (!p.currentItem.baseClass.isThrowable)
                {
                    r.Fail($"a {type} isn't throwable");
                    yield break;
                }

                // As Player's aim does for a throwable (the held item, hidden,
                // not hitting us). A bite as we aim puts the held item away
                // (Player.hideHeldItem destroys it) and nothing is thrown: aim
                // again.
                GameObject held = null;
                bool threw = false;
                for (int attempt = 0; attempt < 4 && !threw; attempt++)
                {
                    if (InvItemClass.isNull(p.currentItem)
                        || p.currentItem.type != type)
                    {
                        yield return TakeOut(type, r);
                        if (r.Failure != null)
                        {
                            yield break;
                        }
                    }

                    held = Core.AddPrefab(
                        p.currentItem.baseClass.item, Vector3.zero,
                        Quaternion.Euler(90f, 0f, 0f), p.gameObject);
                    if (held == null)
                    {
                        r.Fail($"couldn't make a held {type}");
                        yield break;
                    }

                    var ren = held.GetComponent<Renderer>();
                    if (ren != null)
                    {
                        ren.enabled = false;
                    }

                    foreach (var hc in held.GetComponentsInChildren<Collider>())
                    {
                        foreach (var pc in p
                            .GetComponentsInChildren<Collider>())
                        {
                            Physics.IgnoreCollision(hc, pc);
                        }
                    }

                    HeldItem(p) = held;
                    AimAt = target;
                    Face(target);
                    yield return new WaitForSeconds(0.3f);
                    if (dog != null)
                    {
                        target = dog.transform.position;
                        AimAt = target;
                    }

                    Face(target);
                    // put away by a bite
                    if (held == null || HeldItem(p) != held)
                    {
                        yield return new WaitForSeconds(0.5f);
                        continue;
                    }

                    int had = p.currentItem.amount;
                    AsPlayer(() => AttackM.Invoke(p, new object[] { 1f }));
                    if (HeldItem(p) != null)
                    {
                        r.Fail($"the {type} wasn't thrown (still held)");
                        yield break;
                    }

                    threw = InvItemClass.isNull(p.currentItem)
                        || p.currentItem.amount < had;
                    if (!threw)
                    {
                        if (held != null)
                        {
                            Object.Destroy(held);
                        }

                        yield return new WaitForSeconds(0.5f);
                    }
                }

                if (!threw)
                {
                    r.Fail(
                        $"the {type} was never thrown (put away by bites each "
                            + "time)");
                    yield break;
                }

                _thrown = held;
                // A gas bomb thrown at a dog right beside us can break on it at
                // once: gone before we look.
                ThrownItem ti = null;
                if (held != null)
                {
                    ti = held.GetComponent<ThrownItem>();
                }

                float until = Time.realtimeSinceStartup + 6f;
                while (held != null && ti != null && !ti.onGround
                    && Time.realtimeSinceStartup < until)
                {
                    yield return null;
                }

                string prefix =
                    $"{type} thrown at {at} {Fmt(target)} from "
                        + $"{Fmt(p.transform.position)}: ";
                string landed;
                if (held == null)
                {
                    landed = "gone on landing";
                }
                else
                {
                    landed =
                        $"at {Fmt(held.transform.position)}, on the ground "
                            + $"{ti != null && ti.onGround}";
                }

                r.Info = prefix + landed;
            }
            finally
            {
                AimAt = null;
                Restore();
            }

            yield return new WaitForSeconds(1f);
        }

        private static readonly HashSet<GameObject> _seenBefore =
            new HashSet<GameObject>();

        /// <summary>
        /// "i2-seen TYPE AT [lands|fire|off]": the other player's TYPE came
        /// here (ThrownHits.Copies) and landed near AT;
        /// "fire": something caught fire there or it left fire behind; "off":
        /// it went off (Explodes) or is gone.
        /// </summary>
        private static IEnumerator Seen(string[] a, Result r)
        {
            string type = a[0];
            string at = "fire";
            if (a.Length > 1)
            {
                at = a[1];
            }

            string want = "lands";
            if (a.Length > 2)
            {
                want = a[2];
            }

            var it = Singleton<ItemsDatabase>.Instance.getItem(
                type, instantiate: false);
            if (it == null || it.item == null)
            {
                r.Fail($"no {type} in the database");
                yield break;
            }

            string prefab = it.item.name;
            Character dog = null;
            if (at == "meat" || at == "gun")
            {
                dog = DogOf(at);
            }

            Vector3 near;
            if (dog != null)
            {
                near = dog.transform.position;
            }
            else
            {
                near = MapScenarioEnemies.FreeSpot(Spot(at));
            }

            GameObject copy = null;
            float until = Time.realtimeSinceStartup + 8f;
            while (copy == null && Time.realtimeSinceStartup < until)
            {
                copy = ThrownHits.Copies.FirstOrDefault(
                    g => g != null && !_seenBefore.Contains(g)
                        && g.name.StartsWith(prefab, StringComparison.Ordinal));
                if (copy == null)
                {
                    yield return new WaitForSeconds(0.2f);
                }
            }

            bool gone = false;
            if (copy == null)
            {
                // It may have been and gone already (a gas bomb or a molotov is
                // destroyed as it breaks): the log says it came; what it left
                // is looked for where it was thrown.
                var came = LogSince(
                    _mark, $@"\[world\] .* threw {Regex.Escape(type)}\s*$");
                if (came.Count == 0)
                {
                    r.Fail(
                        $"no copy of the other player's {type} here (none "
                            + "since i2-note)");
                    yield break;
                }

                gone = true;
            }
            else
            {
                _seenBefore.Add(copy);
            }

            ThrownItem ti = null;
            if (copy != null)
            {
                ti = copy.GetComponent<ThrownItem>();
            }

            Explodes ex = null;
            if (copy != null)
            {
                ex = copy.GetComponent<Explodes>();
            }

            Vector3 last;
            if (copy != null)
            {
                last = copy.transform.position;
            }
            else
            {
                last = near;
            }

            bool landed = gone, off = gone, fire = false;
            until = Time.realtimeSinceStartup + 10f;
            while (Time.realtimeSinceStartup < until)
            {
                if (copy != null)
                {
                    last = copy.transform.position;
                    landed |= ti != null && ti.onGround;
                    off |= ex != null && ExplodesActivated(ex);
                }
                else
                {
                    landed = off = true;
                }

                // (new since i2-note: what earlier throws left there doesn't
                // count)
                fire = ThrownHits.Spawned.Any(
                    g => g != null && !_spawnedAtNote.Contains(g)
                        && StateDump.Flat(g.transform.position - last) < 400f)
                    || Object.FindObjectsOfType<Burn>().Any(
                        b => b != null && !_burnsAtNote.Contains(b)
                            && b.GetComponent<Player>() == null
                            && StateDump.Flat(b.transform.position - last)
                                < 400f);
                bool condition;
                if (want == "lands")
                {
                    condition = landed;
                }
                else if (want == "off")
                {
                    condition = off;
                }
                else
                {
                    condition = fire;
                }

                if (condition)
                {
                    break;
                }

                yield return new WaitForSeconds(0.2f);
            }

            float d = StateDump.Flat(last - near);
            string shownName;
            if (copy != null)
            {
                shownName = copy.name;
            }
            else
            {
                string goneNote = "";
                if (gone)
                {
                    goneNote = " (gone already)";
                }

                shownName = prefab + goneNote;
            }

            r.Info =
                $"{shownName} at {Fmt(last)} ({d:0} from {at}): landed "
                    + $"{landed}, went off {off}, fire {fire}";
            bool ok;
            if (want == "lands")
            {
                ok = landed;
            }
            else if (want == "off")
            {
                ok = off;
            }
            else
            {
                ok = fire;
            }

            if (!ok)
            {
                string text = "leave fire";
                if (want == "lands")
                {
                    text = "land";
                }
                else if (want == "off")
                {
                    text = "go off";
                }

                r.Fail($"it didn't {text}: {r.Info}");
            }
            else if (d > 500f)
            {
                r.Fail($"it landed far from where it was thrown: {r.Info}");
            }

            if (want == "fire" && r.Failure == null)
            {
                // A molotov's gasoline is the host's (Containers.ThrownSpawns):
                // here it's the one the host shared, and every fire the host
                // lit in it is found.
                yield return new WaitForSeconds(1f);
                var fuel = Object.FindObjectsOfType<Liquid>()
                    .Where(
                        l => l != null && l.name.StartsWith("Gasoline")
                            && StateDump.Flat(l.transform.position - last)
                                < 400f).ToList();
                int shared = fuel.Count(
                    l => ObjRef.Of(l.gameObject).K == ObjRef.Kind.Spawned);
                var lost = LogSince(_mark, @"got LiquidLit .*not found here");
                r.Info +=
                    $"; gasoline here {fuel.Count} ({shared} shared, "
                        + $"{fuel.Count(l => l.burning)} burning), "
                        + $"{lost.Count} fire(s) not found";
                if (fuel.Count == 0 || shared < fuel.Count)
                {
                    r.Fail(
                        "the molotov's gasoline isn't the shared one: "
                            + r.Info);
                }
                else if (lost.Count > 0)
                {
                    r.Fail(r.Info + ": " + lost[0].Trim());
                }
            }
        }
    }
}
