using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DarkwoodMP.World.Traps;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;
using static DarkwoodMP.World.MapScenario;

namespace DarkwoodMP.World
{
    /// <summary>Steps for traps, explosions and fire.</summary>
    public static partial class MapScenarioWorld
    {
        /// <summary>
        /// "trap-fire NAME": our player steps into a trap (its trigger with our
        /// player's collider).
        /// </summary>
        private static IEnumerator TrapFire(string[] a, Result r)
        {
            var t = Find<Trigger>(a[0], x => x.active && !x.triggered);
            if (t == null)
            {
                r.Fail($"no armed trap {a[0]}");
                yield break;
            }

            yield return Walk(t.transform.position);
            Collider col = P.GetComponent<Collider>();
            if (col == null)
            {
                col = P.GetComponentInChildren<Collider>();
            }

            AsPlayer(
                () => AccessTools.Method(typeof(Trigger), "OnAfterTrigger")
                    .Invoke(t, new object[] { col, false }));
            yield return new WaitForSeconds(1f);
            if (t == null)
            {
                r.Info = $"{a[0]} gone";
            }
            else
            {
                r.Info =
                    $"{t.name} {Ref(t)} triggered={t.triggered} "
                        + $"active={t.active}";
            }

            if (t != null && !t.triggered)
            {
                r.Fail($"{r.Info}: didn't go off");
            }

            // Out of it again (a bear trap holds the player that stepped in).
            yield return Walk(Centre, 60f);
        }

        /// <summary>
        /// "trap-step NAME [refused]": our player walks into a trap the game's
        /// way (Trigger.checkCollision with our player's collider: the bite,
        /// the hold, a vine trap's vine on us). With "refused" (the host was
        /// set to refuse it, "trap-refuse"), after the verdict our player must
        /// be out of any bear trap's hold with the health it had.
        /// </summary>
        private static IEnumerator TrapStep(string[] a, Result r)
        {
            var t = Find<Trigger>(a[0], x => x.active && !x.triggered);
            if (t == null)
            {
                r.Fail($"no armed trap {a[0]}");
                yield break;
            }

            yield return Walk(t.transform.position + new Vector3(40f, 0f, 0f));
            Collider col = P.GetComponent<Collider>();
            if (col == null)
            {
                col = P.GetComponentInChildren<Collider>();
            }

            float health = P.health;
            string name = t.name, at = Ref(t);
            int rolledBack = Coop.Current.Authority.RolledBack;
            AsPlayer(() => t.checkCollision(col));
            bool fired = t == null || t.triggered;
            for (float until = Time.time + 6f; Time.time < until
                && Coop.Current.Authority.Waiting > 0;)
            {
                yield return null;
            }

            yield return new WaitForSeconds(1.5f);
            r.Info =
                $"{name} {at} went off={fired}, health {health:0} -> "
                    + $"{P.health:0}, inBearTrap={P.inBearTrap}, rolled back "
                    + $"{Coop.Current.Authority.RolledBack - rolledBack}";
            if (!fired)
            {
                r.Fail($"{r.Info}: didn't go off");
                yield break;
            }

            if (a.Length > 1 && a[1] == "refused")
            {
                if (Coop.Current.Authority.RolledBack == rolledBack)
                {
                    r.Fail($"{r.Info}: the host didn't refuse it");
                }
                else if (P.inBearTrap)
                {
                    r.Fail($"{r.Info}: still held by a trap the host refused");
                }
                else if (P.health < health - 0.5f)
                {
                    r.Fail(
                        $"{r.Info}: the refused trap's bite wasn't given back");
                }
            }

            if (P.inBearTrap)
            {
                P.stopAllPerformingActionAnims();
            }

            // Well off the spot: back at the centre (60 from it) our player set
            // off the next trap placed there.
            yield return Walk(Centre + new Vector3(0f, 0f, 250f), 60f);
        }

        /// <summary>
        /// "trap-refuse race|gone|off" (host): refuse the client's next
        /// TrapFired requests (TrapSync.TestRefuse).
        /// </summary>
        private static IEnumerator TrapRefuse(string[] a, Result r)
        {
            var traps = Coop.Current.Traps;
            string testRefuse = a[0];
            if (a[0] == "off")
            {
                traps.TestRefuse = TrapTest.None;
                testRefuse = "as usual";
            }
            else if (a[0] == "race")
            {
                traps.TestRefuse = TrapTest.Race;
            }
            else
            {
                traps.TestRefuse = TrapTest.Gone;
            }

            r.Info = $"TrapFired requests: {testRefuse}";
            yield break;
        }

        /// <summary>"trap-place PREFAB DX DZ": place a trap there (centre +
        /// offset) the way the player does: the item in our hands, its proxy
        /// there, and Player.progressBarCompleted
        /// (MapScenarioItems1.PlaceReal).</summary>
        private static IEnumerator TrapPlace(string[] a, Result r)
        {
            var at = Centre + new Vector3(F(a[1]), 0f, F(a[2]));
            at.y = P.transform.position.y;
            // Not too near: one placed 50 from our player sprang on it at once
            // (the walk stops between half and all of the distance given).
            yield return Walk(at, 220f);
            GameObject go = null;
            yield return MapScenarioItems1.PlaceReal(a[0], at, r, g => go = g);
            if (r.Failure != null)
            {
                yield break;
            }

            yield return new WaitForSeconds(2f);
            if (go == null)
            {
                r.Info = "none";
            }
            else
            {
                r.Info =
                    $"{go.name} "
                        + $"{ObjRef.Of(go)} at {Fmt(go.transform.position)}";
            }

            if (go == null)
            {
                r.Fail($"{a[0]}: none placed and shared");
            }

            // off it
            yield return Walk(Centre + new Vector3(0f, 0f, 250f), 60f);
        }

        private static readonly
            AccessTools.FieldRef<Player, Item> TrapBeingDisarmed =
                AccessTools.FieldRefAccess<Player, Item>("trapBeingDisarmed");

        /// <summary>
        /// The progress bar's end as the player's action, with it as the trap
        /// being disarmed (Player.progressBarCompleted).
        /// </summary>
        private static void FinishDisarm(Item it) => AsPlayer(() =>
        {
            P.disarmingTrap = true;
            TrapBeingDisarmed(P) = it;
            P.progressBarCompleted();
        });

        /// <summary>"trap-disarm NAME": crouch over it and disarm it (the
        /// progress bar's end; a client's roll is the host's). It's either
        /// disarmed (gone, into our bags) or sprung.</summary>
        private static IEnumerator TrapDisarm(string[] a, Result r)
        {
            var t = Find<Trigger>(
                a[0],
                x => x.active && !x.triggered && x.canDisarm
                    && x.GetComponent<Item>() != null);
            if (t == null)
            {
                r.Fail($"no armed trap {a[0]}");
                yield break;
            }

            var item = t.GetComponent<Item>();
            string what = $"{t.name} {Ref(t)} at {Fmt(t.transform.position)}";
            yield return Walk(t.transform.position, 90f);
            FinishDisarm(item);
            float until = Time.realtimeSinceStartup + 4f;
            while (t != null && !t.triggered
                && Time.realtimeSinceStartup < until)
            {
                yield return null;
            }

            P.hidingInventory = false;
            yield return new WaitForSeconds(0.5f);
            string text = "still armed";
            if (t == null)
            {
                text = "disarmed (gone)";
            }
            else if (t.triggered)
            {
                text = "sprung (the roll failed)";
            }

            r.Info = $"{what}: {text}";
            if (t != null && !t.triggered)
            {
                r.Fail($"{r.Info}: nothing happened");
            }

            yield return Walk(Centre, 60f);
        }

        /// <summary>
        /// "explode NAME": shoot an exploding barrel or gas bottle from out of
        /// its reach (Explodes.onActivate).
        /// </summary>
        private static IEnumerator Explode(string[] a, Result r)
        {
            var e = Find<Explodes>(a[0], x => !BreakSync.Activated(x));
            if (e == null)
            {
                r.Fail($"nothing to explode called {a[0]}");
                yield break;
            }

            yield return Walk(
                AwayFrom(
                    e.transform.position, Mathf.Max(e.radius + 250f, 500f)),
                40f);
            string what = $"{e.name} {Ref(e)} at {Fmt(e.transform.position)}";
            AsPlayer(() => e.onActivate());
            yield return new WaitForSeconds(2f);
            string text;
            if (e == null)
            {
                text = "gone";
            }
            else
            {
                text = "activated=" + BreakSync.Activated(e);
            }

            r.Info = $"{what}: {text}";
            if (e != null && !BreakSync.Activated(e))
            {
                r.Fail($"{r.Info}: didn't go off");
            }

            yield return Walk(Centre, 60f);
        }

        /// <summary>
        /// "ignite NAME": set it alight (a Burn, as fire does); an explosive
        /// that goes off on fire goes off.
        /// </summary>
        private static IEnumerator Ignite(string[] a, Result r)
        {
            var it = Find<Item>(a[0]);
            if (it == null)
            {
                r.Fail($"nothing called {a[0]}");
                yield break;
            }

            var e = it.GetComponent<Explodes>();
            bool explosive = e != null;
            float dist = 200f;
            if (explosive)
            {
                dist = Mathf.Max(e.radius + 250f, 500f);
            }

            yield return Walk(AwayFrom(it.transform.position, dist), 40f);
            string what =
                $"{it.name} {Ref(it)} at {Fmt(it.transform.position)}";
            if (it.GetComponent<Burn>() == null)
            {
                it.gameObject.AddComponent<Burn>().burnTime = 5f;
            }

            yield return new WaitForSeconds(2f);
            bool exploded = explosive && (e == null || BreakSync.Activated(e));
            string text;
            if (it == null)
            {
                text = "gone";
            }
            else
            {
                text = $"burning={it.GetComponent<Burn>() != null}";
            }

            string explodedNote = "";
            if (explosive)
            {
                explodedNote = $" exploded={exploded}";
            }

            r.Info = $"{what}: {text}{explodedNote}";
            if (explosive && !exploded)
            {
                r.Fail($"{r.Info}: lit, but it didn't go off");
            }
            else if (!explosive && it != null
                && it.GetComponent<Burn>() == null)
            {
                r.Fail($"{r.Info}: not burning");
            }

            yield return Walk(Centre, 60f);
        }

        /// <summary>"throw-molotov NAME": throw a molotov at it from 300 away,
        /// as the player's throw does (ThrowSync); something there must catch
        /// fire.</summary>
        private static IEnumerator ThrowMolotov(string[] a, Result r)
        {
            var target =
                Find<Transform>(a[0], x => x.GetComponent<Item>() != null);
            if (target == null)
            {
                r.Fail($"no {a[0]}");
                yield break;
            }

            var tpos = target.position;
            yield return Walk(ThrowSpot(tpos, target, 380f), 30f);
            var db = Singleton<ItemsDatabase>.Instance;
            var mi = db.getItem("molotov", instantiate: false);
            if (mi == null || mi.item == null)
            {
                r.Fail("no molotov in the item database");
                yield break;
            }

            var from = P.transform.position;
            var dir = new Vector3(tpos.x - from.x, 0f, tpos.z - from.z);
            float dist = dir.magnitude;
            dir.Normalize();
            // Made inside our player's collider, it hit our player at once and
            // broke there, setting the thrower alight (Player.throwItem's held
            // item is the player's child until thrown). Make it a step ahead,
            // and let it pass through our player.
            from += dir * 50f;
            dist -= 50f;
            var go = Core.AddPrefab(
                mi.item, from, Quaternion.Euler(90f, 0f, 0f), null);
            var ti = go.GetComponent<ThrownItem>();
            var rb = go.GetComponent<Rigidbody>();
            if (ti == null || rb == null)
            {
                r.Fail("a molotov isn't a thrown item");
                yield break;
            }

            foreach (var mc in go.GetComponentsInChildren<Collider>())
            {
                foreach (var pc in P.GetComponentsInChildren<Collider>())
                {
                    Physics.IgnoreCollision(mc, pc);
                }
            }

            ti.landTarget = new Vector3(tpos.x, from.y, tpos.z);
            rb.drag = 2f;
            rb.velocity = dir * dist * 2.5f;
            ti.thrown = true;
            ti.objectThatSpawnedMe = P.transform;
            ti.setFallSpeed(dist);
            var ren = go.GetComponent<Renderer>();
            if (ren != null)
            {
                ren.enabled = true;
            }

            var before = new HashSet<Burn>(Object.FindObjectsOfType<Burn>());
            ThrowSync.Instance.Thrown("molotov", go, from);
            // Something near where it lands catches fire (not what was burning
            // already).
            float until = Time.realtimeSinceStartup + 15f;
            Burn lit = null;
            while (lit == null && Time.realtimeSinceStartup < until)
            {
                yield return new WaitForSeconds(0.25f);
                lit = Object.FindObjectsOfType<Burn>()
                    .Where(
                        b => b != null && !before.Contains(b)
                            && b.GetComponent<Player>() == null
                            && StateDump.Flat(b.transform.position - tpos)
                                < 350f).FirstOrDefault();
            }

            if (lit != null)
            {
                r.Info = $"{lit.name} {Ref(lit)} caught fire";
            }
            else
            {
                r.Info = $"nothing caught fire near {a[0]}";
            }

            if (lit == null)
            {
                r.Fail(r.Info);
            }

            yield return Walk(Centre, 60f);
        }
    }
}
