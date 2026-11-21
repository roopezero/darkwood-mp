using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;
using static DarkwoodMP.World.MapScenario;

namespace DarkwoodMP.World
{
    /// <summary>Steps for things dragged, fuelled, used and hit.</summary>
    public static partial class MapScenarioWorld
    {
        /// This session's DragSync; null without a session.
        private static DragSync Drags()
        {
            CoopSession coop = Coop.Current;
            if (coop == null)
            {
                return null;
            }

            return coop.Drags;
        }

        /// Drags of ours let go of this session (0 without one).
        private static int Cancelled()
        {
            DragSync drags = Drags();
            if (drags == null)
            {
                return 0;
            }

            return drags.Cancelled;
        }

        // ---- things in the world --------------------------------------------

        /// <summary>
        /// "drag NAME DX DZ": take hold of it (Player.itemBeingDragged, as
        /// Item.startDragging sets) and move it.
        /// </summary>
        private static IEnumerator Drag(string[] a, Result r)
        {
            var it = Find<Item>(a[0], i => i.draggable);
            if (it == null)
            {
                r.Fail($"nothing draggable called {a[0]}");
                yield break;
            }

            var by = new Vector3(F(a[1]), 0f, F(a[2]));
            yield return Walk(it.transform.position, 80f);
            var start = it.transform.position;
            bool held = false;
            yield return DragFor(it, by, 1f, h => held = h);
            // where it came to rest goes out once settled
            yield return new WaitForSeconds(2.5f);
            r.Info =
                $"{it.name} {Ref(it)} from {Fmt(start)} to "
                    + $"{Fmt(it.transform.position)}";
            if (!held)
            {
                string info = r.Info;
                DragSync drag = Drags();
                string owner = null;
                if (drag != null)
                {
                    owner = drag.OtherOwner(ObjRef.Of(it.gameObject));
                }

                if (owner == null)
                {
                    owner = "nobody";
                }

                r.Fail($"{info}: our drag was let go of ({owner} has it)");
            }
            else if (StateDump.Flat(it.transform.position - start)
                < StateDump.Flat(by) * 0.5f)
            {
                r.Fail($"{r.Info}: it didn't move");
            }
        }

        /// <summary>
        /// Holds it (Player.itemBeingDragged, as Item.startDragging sets) and
        /// moves it by `by` over secs, with the player behind it, then lets go.
        /// Stops moving it as soon as our hold is let go of (another player has
        /// it). held(true) if we held it to the end.
        /// </summary>
        private static IEnumerator DragFor(
            Item it, Vector3 by, float secs, Action<bool> held)
        {
            var start = it.transform.position;
            P.itemBeingDragged = it;
            for (float t = 0f; t < secs; t += Time.deltaTime)
            {
                yield return null;
                if (P.itemBeingDragged != it)
                {
                    held(false);
                    yield break;
                }

                var to = start + by * Mathf.Min(1f, t / secs);
                it.transform.position = to;
                var rb = it.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.position = to;
                }

                P.transform.position = new Vector3(
                    to.x, P.transform.position.y, to.z) - by.normalized * 70f;
            }

            it.transform.position = start + by;
            yield return new WaitForSeconds(0.3f);
            bool kept = P.itemBeingDragged == it;
            if (kept)
            {
                P.itemBeingDragged = null;
            }

            held(kept);
        }

        /// <summary>
        /// "drag-race NAME DX DZ AT": at epoch second AT take hold of it and
        /// drag it, as another player does at the same moment. Replies
        /// "got NAME ..." if our drag held to the end, "lost NAME ..." if it
        /// was let go of because the other player had it (the host decides
        /// who); either way where it came to rest.
        /// </summary>
        private static IEnumerator DragRace(string[] a, Result r)
        {
            var it = Find<Item>(a[0], i => i.draggable);
            if (it == null)
            {
                r.Fail($"nothing draggable called {a[0]}");
                yield break;
            }

            var by = new Vector3(F(a[1]), 0f, F(a[2]));
            double at = double.Parse(a[3], CultureInfo.InvariantCulture);
            yield return Walk(it.transform.position, 80f);
            double Now() =>
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
            if (Now() > at)
            {
                r.Fail($"walked past the moment ({Now() - at:0.0} s late)");
                yield break;
            }

            while (Now() < at)
            {
                yield return null;
            }

            int cancelled = Cancelled();
            bool held = false;
            yield return DragFor(it, by, 1.5f, h => held = h);
            yield return new WaitForSeconds(3f);
            string text = "lost";
            if (held)
            {
                text = "got";
            }

            r.Info =
                $"{text} {it.name} {Ref(it)} ({Cancelled() - cancelled} "
                    + "drag(s) of ours let go of), at rest at "
                    + $"{Fmt(it.transform.position)}";
            if (held == (Cancelled() > cancelled))
            {
                r.Fail($"{r.Info}: held to the end and let go of don't agree");
            }
        }

        /// <summary>
        /// "fuel NAME AMOUNT": pour gasoline into a generator or the saw, a
        /// unit at a time, as the player does.
        /// </summary>
        private static IEnumerator Fuel(string[] a, Result r)
        {
            float amount = 10f;
            if (a.Length > 1)
            {
                amount = F(a[1]);
            }

            var g = Find<Generator>(a[0]);
            Saw s = null;
            if (g == null)
            {
                s = Find<Saw>(a[0]);
            }

            if (g == null && s == null)
            {
                r.Fail($"no generator or saw {a[0]}");
                yield break;
            }

            Component c = (Component)g;
            if (c == null)
            {
                c = s;
            }

            yield return Walk(c.transform.position);
            float before;
            if (g != null)
            {
                before = g.fuel;
            }
            else
            {
                before = s.fuel;
            }

            for (int i = 0; i < amount; i++)
            {
                AsPlayer(() =>
                {
                    if (g != null)
                    {
                        g.addFuel(1f);
                    }
                    else
                    {
                        s.addFuel(1f);
                    }
                });

                yield return new WaitForSeconds(0.05f);
            }

            yield return new WaitForSeconds(1.5f); // a refused pour is set back
            float after;
            if (g != null)
            {
                after = g.fuel;
            }
            else
            {
                after = s.fuel;
            }

            float max;
            if (g != null)
            {
                max = g.maxFuel;
            }
            else
            {
                max = s.maxFuel;
            }

            r.Info =
                $"{c.name} {Ref(c)} fuel {before:0.0} -> {after:0.0} (max "
                    + $"{max:0})";
            if (after <= before + amount * 0.5f && before < max - 1f)
            {
                r.Fail($"{r.Info}: the fuel didn't go in");
            }
        }

        /// <summary>"switch NAME on|off": switch a lamp, generator, radio...
        /// (Item.turnOn / turnOff). A generator's lamps must follow.</summary>
        private static IEnumerator Switch(string[] a, Result r)
        {
            var it = Find<Item>(a[0], i => i.switchable);
            if (it == null)
            {
                r.Fail($"nothing switchable called {a[0]}");
                yield break;
            }

            bool on = a.Length < 2 || a[1] == "on";
            yield return Walk(it.transform.position);
            AsPlayer(() =>
            {
                if (on)
                {
                    it.turnOn();
                }
                else
                {
                    it.turnOff();
                }
            });

            yield return new WaitForSeconds(1.5f);
            var gen = it.GetComponent<Generator>();
            string lamps = "";
            if (gen != null)
            {
                lamps = " lamps " + string.Join(
                    ",",
                    gen.powerItems.Where(l => l != null).Select(
                        l => $"{l.name}={l.isOn}"));
            }

            r.Info = $"{it.name} {Ref(it)} on={it.isOn}{lamps}";
            if (it.isOn != on)
            {
                r.Fail($"{r.Info}: didn't switch");
            }
        }

        /// <summary>"use NAME": what a player does with it: an
        /// InteractiveItem's switch, an Item's switch, or building it
        /// (Constructible.construct).</summary>
        private static IEnumerator Use(string[] a, Result r)
        {
            var t = Find<Transform>(
                a[0],
                x => x.GetComponentInChildren<Item>() != null
                    || x.GetComponentInChildren<InteractiveItem>() != null
                    || x.GetComponentInChildren<Constructible>() != null);
            if (t == null)
            {
                r.Fail($"nothing usable called {a[0]}");
                yield break;
            }

            yield return Walk(t.position);
            var ii = t.GetComponentInChildren<InteractiveItem>();
            var con = t.GetComponentInChildren<Constructible>();
            var it = t.GetComponentInChildren<Item>();
            if (ii != null)
            {
                bool was = ii.isOn;
                AsPlayer(() => ii.switchMe());
                yield return new WaitForSeconds(1f);
                r.Info =
                    $"{t.name} {Ref(ii)} interactive on {was} -> {ii.isOn}";
                if (ii.isOn == was)
                {
                    r.Fail($"{r.Info}: didn't switch");
                }
            }
            else if (con != null && !con.constructed)
            {
                AsPlayer(() => con.construct(false, 0));
                yield return new WaitForSeconds(1f);
                // it removes itself once built, unless it's kept
                bool built = con == null || con.constructed;
                r.Info = $"{t.name} {Ref(t)} built={built}";
                if (!built)
                {
                    r.Fail($"{r.Info}: not built");
                }
            }
            else if (it != null && it.switchable)
            {
                bool was = it.isOn;
                AsPlayer(() =>
                {
                    if (was)
                    {
                        it.turnOff();
                    }
                    else
                    {
                        it.turnOn();
                    }
                });

                yield return new WaitForSeconds(1f);
                r.Info = $"{it.name} {Ref(it)} on {was} -> {it.isOn}";
                if (it.isOn == was)
                {
                    r.Fail($"{r.Info}: didn't switch");
                }
            }
            else
            {
                string name = t.name;
                string id = Ref(t);
                Item found = it;
                string item = null;
                if (found != null)
                {
                    item = found.name;
                }

                if (item == null)
                {
                    item = "-";
                }

                Constructible build = con;
                string constructed = null;
                if (build != null)
                {
                    constructed = build.constructed.ToString();
                }

                if (constructed == null)
                {
                    constructed = "-";
                }

                r.Fail(
                    $"{name} {id}: nothing to do with it (item {item}, "
                        + $"constructed {constructed})");
            }
        }

        /// <summary>
        /// "hit NAME DAMAGE": our player hits a piece of furniture
        /// (Item.getHit).
        /// </summary>
        private static IEnumerator Hit(string[] a, Result r)
        {
            // "*": the nearest piece of plain furniture (no container, trap or
            // explosive in it).
            Item it;
            if (a[0] == "*")
            {
                it = Nearest(
                    Object.FindObjectsOfType<Item>().Where(
                        i => i.destructible && !i.destroyed && i.health > 1
                            && !i.hasInventory
                            && i.GetComponent<Explodes>() == null
                            && i.GetComponent<Trigger>() == null
                            && i.GetComponent<Character>() == null
                            && ObjRef.Of(i.gameObject).Valid), Centre);
            }
            else
            {
                it = Find<Item>(a[0], i => i.destructible && !i.destroyed);
            }

            if (it == null)
            {
                r.Fail($"nothing breakable called {a[0]}");
                yield break;
            }

            int dmg = 10;
            if (a.Length > 1)
            {
                dmg = int.Parse(a[1]);
            }

            yield return Walk(it.transform.position);
            int was = it.health;
            AsPlayer(() => it.getHit(dmg, P.transform));
            yield return new WaitForSeconds(1f);
            if (it == null)
            {
                r.Info = $"{a[0]} gone";
            }
            else
            {
                r.Info =
                    $"{it.name} {Ref(it)} health {was} -> {it.health} "
                        + $"broken={it.destroyed}";
            }

            if (it != null && it.health == was && !it.destroyed)
            {
                r.Fail($"{r.Info}: the hit did nothing");
            }
        }
    }
}
