using System.Collections;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;

namespace DarkwoodMP.World
{
    /// <summary>Steps for doors and windows.</summary>
    public static partial class MapScenarioWorld
    {
        /// <summary>
        /// "door-lock N": lock it (as the story does; Door.lockMe).
        /// </summary>
        private static IEnumerator DoorLock(string[] a, Result r)
        {
            var d = NthArg<Door>(a, r, "door");
            if (d == null)
            {
                yield break;
            }

            if (d.opened)
            {
                d.close(P.transform);
                yield return new WaitForSeconds(0.5f);
            }

            d.lockMe("");
            yield return new WaitForSeconds(0.5f);
            var l = d.GetComponent<Locked>();
            r.Info = $"{d.name} {Ref(d)} locked={l != null && l.locked}";
            if (l == null || !l.locked)
            {
                r.Fail($"{r.Info}: not locked");
            }
        }

        /// <summary>
        /// "door-pick N": open its lock with a lockpick, as the player does
        /// (Locked.tryToLockpick).
        /// </summary>
        private static IEnumerator DoorPick(string[] a, Result r)
        {
            var d = NthArg<Door>(a, r, "door");
            if (d == null)
            {
                yield break;
            }

            var l = d.GetComponent<Locked>();
            if (l == null || !l.locked)
            {
                r.Fail($"{d.name} {Ref(d)} isn't locked here");
                yield break;
            }

            yield return Walk(d.transform.position);
            if (CarriedOf("lockpick") == 0)
            {
                P.Inventory.addItemTypeToPlayer(
                    "lockpick", 1, dropIfNoRoom: true);
            }

            yield return null;
            AsPlayer(() => l.tryToLockpick());
            // a refusal would lock it again
            yield return new WaitForSeconds(1.5f);
            r.Info = $"{d.name} {Ref(d)} locked={l.locked}";
            if (l.locked)
            {
                r.Fail($"{r.Info}: still locked");
            }
        }

        /// <summary>
        /// "door-barricade N": nail planks over it, as the hammer's three
        /// strokes do (Door.barricade(byPlayer)).
        /// </summary>
        private static IEnumerator DoorBarricade(string[] a, Result r)
        {
            var d = NthArg<Door>(a, r, "door");
            if (d == null)
            {
                yield break;
            }

            yield return Walk(d.transform.position);
            if (d.opened)
            {
                AsPlayer(() => d.close(P.transform));
                yield return new WaitForSeconds(0.5f);
            }

            for (int i = 0; i < 3 && !d.barricaded; i++)
            {
                AsPlayer(() => d.barricade(byPlayer: true));
                yield return new WaitForSeconds(0.3f);
            }

            yield return new WaitForSeconds(1f);
            r.Info =
                $"{d.name} {Ref(d)} barricaded={d.barricaded} health "
                    + $"{d.barricadeHealth}";
            if (!d.barricaded)
            {
                r.Fail($"{r.Info}: not barricaded");
            }
        }

        /// <summary>
        /// "door-hit N DAMAGE": our player hits it (its barricade first).
        /// </summary>
        private static IEnumerator DoorHit(string[] a, Result r)
        {
            var d = NthArg<Door>(a, r, "door");
            if (d == null)
            {
                yield break;
            }

            int dmg = 20;
            if (a.Length > 1)
            {
                dmg = int.Parse(a[1]);
            }

            yield return Walk(d.transform.position);
            var was = WorldActions.DoorState.Of(d);
            AsPlayer(() => d.getHit(dmg, P.transform));
            yield return new WaitForSeconds(1f);
            r.Info =
                $"{d.name} {Ref(d)} barricaded={d.barricaded} "
                    + $"({d.barricadeHealth}) health {d.health} "
                    + $"broken={d.destroyed}";
            if (was.Same(WorldActions.DoorState.Of(d)))
            {
                r.Fail($"{r.Info}: the hit did nothing");
            }
        }

        /// <summary>"door-burn N": set it alight. The host's world burns it; a
        /// client's lights its copy and asks the host (Fire.ClientIgnited,
        /// as its flamethrower does).</summary>
        private static IEnumerator DoorBurn(string[] a, Result r)
        {
            var d = NthArg<Door>(a, r, "door");
            if (d == null)
            {
                yield break;
            }

            yield return Walk(d.transform.position, 200f);
            if (d.GetComponent<Burn>() == null)
            {
                var b = d.gameObject.AddComponent<Burn>();
                b.burnTime = 5f;
                if (Net.Session.Instance != null
                    && Net.Session.Instance.IsClient)
                {
                    AsPlayer(() =>
                    {
                        CoopSession coop = Coop.Current;
                        if (coop != null)
                        {
                            coop.Fire.ClientIgnited(d.gameObject);
                        }
                    });
                }
            }

            yield return new WaitForSeconds(1f);
            r.Info =
                $"{d.name} {Ref(d)} burning={d.GetComponent<Burn>() != null} "
                    + $"health {d.health}";
            if (d.GetComponent<Burn>() == null)
            {
                r.Fail($"{r.Info}: not burning (refused?)");
            }

            yield return Walk(Centre, 60f); // away from the fire
        }

        /// <summary>
        /// "window-barricade N": planks over it, the hammer's three strokes
        /// (Window.barricade(0, byPlayer)).
        /// </summary>
        private static IEnumerator WindowBarricade(string[] a, Result r)
        {
            var w = NthArg<Window>(a, r, "window");
            if (w == null)
            {
                yield break;
            }

            yield return Walk(w.transform.position);
            for (int i = 0; i < 3 && !w.barricaded; i++)
            {
                AsPlayer(() => w.barricade(0, byPlayer: true));
                yield return new WaitForSeconds(0.3f);
            }

            yield return new WaitForSeconds(1f);
            r.Info =
                $"{w.name} {Ref(w)} barricaded={w.barricaded} health "
                    + $"{w.barricadeHealth}";
            if (!w.barricaded)
            {
                r.Fail($"{r.Info}: not barricaded");
            }
        }

        /// <summary>
        /// "window-hit N DAMAGE": our player hits its barricade.
        /// </summary>
        private static IEnumerator WindowHit(string[] a, Result r)
        {
            var w = NthArg<Window>(a, r, "window");
            if (w == null)
            {
                yield break;
            }

            int dmg = 20;
            if (a.Length > 1)
            {
                dmg = int.Parse(a[1]);
            }

            if (!w.barricaded)
            {
                r.Fail($"{w.name} {Ref(w)} isn't barricaded here");
                yield break;
            }

            yield return Walk(w.transform.position);
            int was = w.barricadeHealth;
            AsPlayer(() => w.getHit(dmg, P.transform));
            yield return new WaitForSeconds(1f);
            r.Info =
                $"{w.name} {Ref(w)} barricaded={w.barricaded} health {was} -> "
                    + $"{w.barricadeHealth}";
            if (w.barricaded && w.barricadeHealth == was)
            {
                r.Fail($"{r.Info}: the hit did nothing");
            }
        }

        /// <summary>
        /// "expect-burning door N | window N | NAME [SECS]": it catches fire
        /// here within SECS (default 4).
        /// </summary>
        private static IEnumerator ExpectBurning(string[] a, Result r)
        {
            Component c;
            if (a[0] == "door")
            {
                c = Nth<Door>(a[1]);
            }
            else if (a[0] == "window")
            {
                c = (Component)Nth<Window>(a[1]);
            }
            else
            {
                c = Find<Item>(a[0]);
            }

            int at = 1;
            if (a[0] == "door" || a[0] == "window")
            {
                at = 2;
            }

            float secs = 4f;
            if (a.Length > at)
            {
                secs = F(a[at]);
            }

            if (c == null)
            {
                r.Fail($"no {string.Join(" ", a)}");
                yield break;
            }

            float until = Time.realtimeSinceStartup + secs;
            while (c != null && c.GetComponent<Burn>() == null
                && Time.realtimeSinceStartup < until)
            {
                yield return null;
            }

            if (c == null)
            {
                r.Info = "gone";
            }
            else
            {
                r.Info =
                    $"{c.name} {Ref(c)} "
                        + $"burning={c.GetComponent<Burn>() != null}";
            }

            if (c != null && c.GetComponent<Burn>() == null)
            {
                r.Fail($"{r.Info}: didn't catch fire here");
            }
        }
    }
}
