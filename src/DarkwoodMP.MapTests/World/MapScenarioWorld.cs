using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;
using static DarkwoodMP.World.MapScenario;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Steps of the two-game map test for one of each shared thing in the test
    /// map (see MapScenario), each done with the game's own calls, as the
    /// player's own action where a player does it. Each fails if the thing
    /// didn't happen in its own game; whether it reached the other game shows
    /// in the dumps (StateDump), or, for what doesn't last (fire,
    /// "X is using this"), in an "expect-..." step run in the other game.
    ///
    /// Things are named by "NAME" (the nearest one of that name to the test
    /// area's centre) or "NAME@DX,DZ" (the one nearest to the centre plus that
    /// offset), the same in both games. Doors and windows are "N": the Nth
    /// nearest to the centre.
    /// </summary>
    public static partial class MapScenarioWorld
    {
        public static void Register()
        {
            Add("inspect", Inspect);
            // Doors and windows.
            Add("door-lock", DoorLock);
            Add("door-pick", DoorPick);
            Add("door-barricade", DoorBarricade);
            Add("door-hit", DoorHit);
            Add("door-burn", DoorBurn);
            Add("window-barricade", WindowBarricade);
            Add("window-hit", WindowHit);
            Add("expect-burning", ExpectBurning);
            // Containers.
            Add("put-item", PutItem);
            Add("take-from", TakeFrom);
            Add("hold-open", HoldOpen);
            Add("release", Release);
            Add("open-busy", OpenBusy);
            Add("pick-crate", PickCrate, timeout: 40f);
            Add("pickup", Pickup);
            // Things in the world.
            Add("drag", Drag);
            Add("drag-race", DragRace, timeout: 40f);
            Add("fuel", Fuel);
            Add("switch", Switch);
            Add("use", Use);
            Add("hit", Hit);
            Add("trap-fire", TrapFire);
            Add("trap-place", TrapPlace);
            Add("trap-step", TrapStep, timeout: 40f);
            Add("trap-refuse", TrapRefuse);
            // "trap-away": our player walks 250 north of the centre, off where
            // traps are placed (trap-place 0 -60).
            Add(
                "trap-away",
                (a, r) => Walk(Centre + new Vector3(0f, 0f, 250f), 60f),
                timeout: 30f);
            Add("place-furniture", PlaceFurniture);
            Add("world-furniture", WorldFurniture);
            Add("ids-tied", IdsTied);
            Add("ids-hit", IdsHit);
            Add("trap-disarm", TrapDisarm, timeout: 40f);
            Add("explode", Explode);
            Add("ignite", Ignite);
            Add("throw-molotov", ThrowMolotov, timeout: 45f);
            // The story, traders, enemies.
            Add("flag", Flag);
            Add("reputation", Reputation);
            Add("journal", JournalStep);
            Add("trader-restock", TraderRestock);
            Add("trade", Trade);
            // "trade-seen" (host): our game took a client's trade (its log says
            // who traded what): after "trade --earn".
            Add("trade-seen", TradeSeen);
            Add("spawn", Spawn);
            Add("kill-enemy", KillEnemy, timeout: 40f);
        }

        // ---- finding things -------------------------------------------------

        /// <summary>
        /// "NAME" or "NAME@DX,DZ": that one of these, nearest to the centre (or
        /// centre + offset).
        /// </summary>
        internal static T Find<T>(string spec, Func<T, bool> ok =
            null) where T : Component
        {
            string name = spec;
            var at = Centre;
            int i = spec.IndexOf('@');
            if (i >= 0)
            {
                name = spec.Substring(0, i);
                var d = spec.Substring(i + 1).Split(',');
                at += new Vector3(F(d[0]), 0f, F(d[1]));
            }

            var near = Object.FindObjectsOfType<T>()
                .Where(
                    t => t != null
                        && StateDump.Flat(t.transform.position - Centre)
                            < StateDump.DefaultRadius
                        && (ok == null || ok(t))).ToList();
            // That name exactly, else one whose name starts with it.
            var exact = near.Where(
                t => StateDump.Clean(t.gameObject.name) == name).ToList();
            IEnumerable<T> things;
            if (exact.Count > 0)
            {
                things = exact;
            }
            else
            {
                things = near.Where(
                    t => StateDump.Clean(t.gameObject.name).StartsWith(
                        name, StringComparison.Ordinal));
            }

            return Nearest(things, at);
        }

        /// <summary>
        /// The Nth nearest to the centre (broken ones counted too, so the
        /// numbers don't move).
        /// </summary>
        internal static T Nth<T>(string n) where T : Component =>
            Object.FindObjectsOfType<T>().Where(
                t => ObjRef.Of(t.gameObject).Valid)
                .OrderBy(
                    t => Mathf.Round(
                        StateDump.Flat(t.transform.position - Centre)))
                .ThenBy(t => ObjRef.Of(t.gameObject)
                    .ToString(), StringComparer.Ordinal)
                .Skip(int.Parse(n)).FirstOrDefault();

        internal static string Ref(Component c)
        {
            if (c == null)
            {
                return "-";
            }

            return ObjRef.Of(c.gameObject).ToString();
        }

        private static Player P => Player.Instance;

        private static IEnumerable<InvItemClass> Carried() =>
            P.Inventory.getAllItems()
                .Concat(
                    P.Hotbar.slots.Where(s =>
                        !InvItemClass.isNull(s.invItem)).Select(
                            s => s.invItem));
        internal static int CarriedOf(string type) =>
            Carried().Where(i => i.type == type).Sum(i => i.amount);
        internal static string Contents(Inventory inv) =>
            string.Join(
                ",",
                inv.slots.Where(
                    s => s != null
                        && !InvItemClass.isNull(s.invItem)).Select(
                            s => $"{s.invItem.type}x{s.invItem.amount}"));

        /// <summary>
        /// Where to throw a molotov at pos from, dist away: of the spots around
        /// it, the one farthest from anything else that burns (furniture,
        /// doors, windows), whose way back to the centre stays clear of pos.
        /// Standing on the centre's side of the target put the thrower beside
        /// the hideout's wardrobe or window, which the spilt gasoline set
        /// alight, and the thrower with it.
        /// </summary>
        private static Vector3 ThrowSpot(
            Vector3 pos, Component target, float dist)
        {
            var others = Object.FindObjectsOfType<Item>().Cast<Component>()
                .Concat(Object.FindObjectsOfType<Door>())
                .Concat(Object.FindObjectsOfType<Window>())
                .Where(
                    c => c != null
                        && (target == null || !c.transform.IsChildOf(
                            target.transform))
                        && StateDump.Flat(c.transform.position - pos)
                            < dist + 400f)
                .Select(c => c.transform.position).ToList();
            Vector3 best = AwayFrom(pos, dist);
            float bestScore = float.MinValue;
            for (int deg = 0; deg < 360; deg += 20)
            {
                var spot = pos
                    + Quaternion.Euler(0f, deg, 0f) * Vector3.forward * dist;
                spot.y = pos.y;
                float clear;
                if (others.Count == 0)
                {
                    clear = 1000f;
                }
                else
                {
                    clear = others.Min(o => StateDump.Flat(o - spot));
                }

                // The way back: the nearest the walk from the spot to the
                // centre comes to the fire.
                var seg = Centre - spot;
                seg.y = 0f;
                float t;
                if (seg.sqrMagnitude < 1f)
                {
                    t = 0f;
                }
                else
                {
                    t = Mathf.Clamp01(
                        Vector3.Dot(
                            new Vector3(pos.x - spot.x, 0f, pos.z - spot.z),
                            seg) / seg.sqrMagnitude);
                }

                float back = StateDump.Flat(spot + seg * t - pos);
                float score = Mathf.Min(clear, back);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = spot;
                }
            }

            return best;
        }

        /// <summary>
        /// A spot this far from pos, on the centre's side of it (to stand at
        /// while something goes off).
        /// </summary>
        private static Vector3 AwayFrom(Vector3 pos, float dist)
        {
            var d = pos - Centre;
            d.y = 0f;
            if (d.sqrMagnitude < 1f)
            {
                d = Vector3.forward;
            }

            return pos - d.normalized * dist;
        }

        // ---- messages the player is shown (for "X is using this") -----------

        internal static readonly List<(float at, string msg)> Messages =
            new List<(float, string)>();

        [HarmonyPatch(
            typeof(Player), nameof(Player.displayMessage),
            new[] { typeof(string), typeof(bool), typeof(bool) })]
        static class MessagePatch
        {
            static void Postfix(string message)
            {
                if (Messages.Count > 50)
                {
                    Messages.RemoveAt(0);
                }

                string logged = message;
                if (logged == null)
                {
                    logged = "";
                }

                Messages.Add((Time.realtimeSinceStartup, logged));
            }
        }

        // ---- inspect --------------------------------------------------------

        /// <summary>
        /// "inspect NAME...": what each is made of (components on it and its
        /// children), and its ObjRef.
        /// </summary>
        private static IEnumerator Inspect(string[] a, Result r)
        {
            var parts = new List<string>();
            foreach (var name in a)
            {
                var t = Find<Transform>(
                    name,
                    x => x.parent == null
                        || StateDump.Clean(x.parent.name) != StateDump.Clean(
                            x.name));
                if (t == null)
                {
                    parts.Add($"{name}: none");
                    continue;
                }

                var comps = t.GetComponentsInChildren<Component>(true).Where(
                    c => c != null && !(c is Transform) && !(c is Renderer)
                        && !(c is MeshFilter))
                    .Select(c =>
                    {
                        string prefix;
                        if (c.transform == t)
                        {
                            prefix = "";
                        }
                        else
                        {
                            prefix = c.name + ".";
                        }

                        return prefix + c.GetType().Name;
                    }).Distinct().Take(14);

                string extra = "";
                if (t.GetComponent<Generator>() is Generator g)
                {
                    extra +=
                        $" fuel {g.fuel:0}/{g.maxFuel:0} powers "
                            + $"{g.powerItems.Count}";
                }

                if (t.GetComponent<Saw>() is Saw s)
                {
                    extra += $" fuel {s.fuel:0}/{s.maxFuel:0}";
                }

                if (t.GetComponent<Explodes>() is Explodes e)
                {
                    extra +=
                        $" radius {e.radius:0} onIgnite {e.explodeOnIgnite}";
                }

                if (t.GetComponent<NPC>() is NPC n)
                {
                    bool trader = n.trader;
                    string stock = "-";
                    if (n.inventory != null)
                    {
                        stock = Contents(n.inventory);
                    }

                    extra += $" trader {trader} stock [{stock}]";
                }

                if (t.GetComponentInChildren<Item>() is Item it)
                {
                    extra +=
                        $" item {it.name} switchable {it.switchable} on "
                            + $"{it.isOn} light {it.isLight} destructible "
                            + $"{it.destructible} health {it.health} "
                            + $"disarmTime {it.disarmTime} searched "
                            + $"{it.searched}";
                }

                if (t.GetComponentInChildren<Trigger>() is Trigger tr)
                {
                    extra +=
                        $" trigger {tr.name} active {tr.active} triggered "
                            + $"{tr.triggered} canDisarm {tr.canDisarm}";
                }

                if (t.GetComponentInChildren<Locked>() is Locked lk)
                {
                    extra += $" locked {lk.locked}";
                }

                parts.Add(
                    $"{name} "
                        + $"{ObjRef.Of(t.gameObject)} at {Fmt(t.position)}: "
                        + $"{string.Join(" ", comps)};{extra}");
            }

            r.Info = string.Join(" | ", parts);
            Plugin.Log.LogInfo($"[map] inspect: {r.Info}");
            yield break;
        }

        // ---- doors and windows ----------------------------------------------

        /// <summary>
        /// The Nth of these (a[0]), or null after failing with the usage of the
        /// "what-..." steps.
        /// </summary>
        private static T NthArg<T>(string[] a, Result r, string what)
            where T : Component
        {
            T t = null;
            if (a.Length > 0)
            {
                t = Nth<T>(a[0]);
            }

            if (t == null)
            {
                r.Fail($"usage: {what}-... N (no such {what})");
            }

            return t;
        }
    }
}
