using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;
using static DarkwoodMP.World.MapScenario;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Steps of the two-game map test for one of each shared thing in the test map (see MapScenario), each done
    /// with the game's own calls, as the player's own action where a player does it. Each fails if the thing
    /// didn't happen in its own game; whether it reached the other game shows in the dumps (StateDump), or, for
    /// what doesn't last (fire, "X is using this"), in an "expect-..." step run in the other game.
    ///
    /// Things are named by "NAME" (the nearest one of that name to the test area's centre) or "NAME@DX,DZ" (the
    /// one nearest to the centre plus that offset), the same in both games. Doors and windows are "N": the Nth
    /// nearest to the centre.
    /// </summary>
    public static class MapScenarioWorld
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
            // "trap-away": our player walks 250 north of the centre, off where traps are placed (trap-place 0 -60).
            Add("trap-away", (a, r) => Walk(Centre + new Vector3(0f, 0f, 250f), 60f), timeout: 30f);
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
            // "trade-seen" (host): our game took a client's trade (its log says who traded what): after "trade --earn".
            Add("trade-seen", TradeSeen);
            Add("spawn", Spawn);
            Add("kill-enemy", KillEnemy, timeout: 40f);
        }

        // ---- finding things ------------------------------------------------------------------------------------

        /// <summary>"NAME" or "NAME@DX,DZ": that one of these, nearest to the centre (or centre + offset).</summary>
        internal static T Find<T>(string spec, Func<T, bool> ok = null) where T : Component
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
            var near = Object.FindObjectsOfType<T>().Where(t => t != null && StateDump.Flat(t.transform.position - Centre) < StateDump.DefaultRadius && (ok == null || ok(t))).ToList();
            // That name exactly, else one whose name starts with it.
            var exact = near.Where(t => StateDump.Clean(t.gameObject.name) == name).ToList();
            IEnumerable<T> things;
            if (exact.Count > 0)
            {
                things = exact;
            }
            else
            {
                things = near.Where(t => StateDump.Clean(t.gameObject.name).StartsWith(name, StringComparison.Ordinal));
            }
            return Nearest(things, at);
        }

        /// <summary>The Nth nearest to the centre (broken ones counted too, so the numbers don't move).</summary>
        internal static T Nth<T>(string n) where T : Component =>
            Object.FindObjectsOfType<T>().Where(t => ObjRef.Of(t.gameObject).Valid)
                .OrderBy(t => Mathf.Round(StateDump.Flat(t.transform.position - Centre))).ThenBy(t => ObjRef.Of(t.gameObject).ToString(), StringComparer.Ordinal)
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
            P.Inventory.getAllItems().Concat(P.Hotbar.slots.Where(s => !InvItemClass.isNull(s.invItem)).Select(s => s.invItem));
        internal static int CarriedOf(string type) => Carried().Where(i => i.type == type).Sum(i => i.amount);
        internal static string Contents(Inventory inv) =>
            string.Join(",", inv.slots.Where(s => s != null && !InvItemClass.isNull(s.invItem)).Select(s => $"{s.invItem.type}x{s.invItem.amount}"));

        /// <summary>
        /// Where to throw a molotov at pos from, dist away: of the spots around it, the one farthest from anything else
        /// that burns (furniture, doors, windows), whose way back to the centre stays clear of pos. Standing on the
        /// centre's side of the target put the thrower beside the hideout's wardrobe or window, which the spilt
        /// gasoline set alight, and the thrower with it.
        /// </summary>
        private static Vector3 ThrowSpot(Vector3 pos, Component target, float dist)
        {
            var others = Object.FindObjectsOfType<Item>().Cast<Component>().Concat(Object.FindObjectsOfType<Door>()).Concat(Object.FindObjectsOfType<Window>())
                .Where(c => c != null && (target == null || !c.transform.IsChildOf(target.transform)) && StateDump.Flat(c.transform.position - pos) < dist + 400f)
                .Select(c => c.transform.position).ToList();
            Vector3 best = AwayFrom(pos, dist);
            float bestScore = float.MinValue;
            for (int deg = 0; deg < 360; deg += 20)
            {
                var spot = pos + Quaternion.Euler(0f, deg, 0f) * Vector3.forward * dist;
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
                // The way back: the nearest the walk from the spot to the centre comes to the fire.
                var seg = Centre - spot;
                seg.y = 0f;
                float t;
                if (seg.sqrMagnitude < 1f)
                {
                    t = 0f;
                }
                else
                {
                    t = Mathf.Clamp01(Vector3.Dot(new Vector3(pos.x - spot.x, 0f, pos.z - spot.z), seg) / seg.sqrMagnitude);
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

        /// <summary>A spot this far from pos, on the centre's side of it (to stand at while something goes off).</summary>
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

        // ---- messages the player is shown (for "X is using this") ---------------------------------------------

        internal static readonly List<(float at, string msg)> Messages = new List<(float, string)>();

        [HarmonyPatch(typeof(Player), nameof(Player.displayMessage), new[] { typeof(string), typeof(bool), typeof(bool) })]
        static class MessagePatch
        {
            static void Postfix(string message)
            {
                if (Messages.Count > 50)
                {
                    Messages.RemoveAt(0);
                }
                string message2 = message;
                if (message2 == null)
                {
                    message2 = "";
                }
                Messages.Add((Time.realtimeSinceStartup, message2));
            }
        }

        // ---- inspect ---------------------------------------------------------------------------------------------

        /// <summary>"inspect NAME...": what each is made of (components on it and its children), and its ObjRef.</summary>
        private static IEnumerator Inspect(string[] a, Result r)
        {
            var parts = new List<string>();
            foreach (var name in a)
            {
                var t = Find<Transform>(name, x => x.parent == null || StateDump.Clean(x.parent.name) != StateDump.Clean(x.name));
                if (t == null)
                {
                    parts.Add($"{name}: none");
                    continue;
                }
                var comps = t.GetComponentsInChildren<Component>(true).Where(c => c != null && !(c is Transform) && !(c is Renderer) && !(c is MeshFilter))
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
                    extra += $" fuel {g.fuel:0}/{g.maxFuel:0} powers {g.powerItems.Count}";
                }
                if (t.GetComponent<Saw>() is Saw s)
                {
                    extra += $" fuel {s.fuel:0}/{s.maxFuel:0}";
                }
                if (t.GetComponent<Explodes>() is Explodes e)
                {
                    extra += $" radius {e.radius:0} onIgnite {e.explodeOnIgnite}";
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
                    extra += $" item {it.name} switchable {it.switchable} on {it.isOn} light {it.isLight} destructible {it.destructible} health {it.health} disarmTime {it.disarmTime} searched {it.searched}";
                }
                if (t.GetComponentInChildren<Trigger>() is Trigger tr)
                {
                    extra += $" trigger {tr.name} active {tr.active} triggered {tr.triggered} canDisarm {tr.canDisarm}";
                }
                if (t.GetComponentInChildren<Locked>() is Locked lk)
                {
                    extra += $" locked {lk.locked}";
                }
                parts.Add($"{name} {ObjRef.Of(t.gameObject)} at {Fmt(t.position)}: {string.Join(" ", comps)};{extra}");
            }
            r.Info = string.Join(" | ", parts);
            Plugin.Log.LogInfo($"[map] inspect: {r.Info}");
            yield break;
        }

        // ---- doors and windows -----------------------------------------------------------------------------------

        /// <summary>The Nth of these (a[0]), or null after failing with the usage of the "what-..." steps.</summary>
        private static T NthArg<T>(string[] a, Result r, string what) where T : Component
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

        /// <summary>"door-lock N": lock it (as the story does; Door.lockMe).</summary>
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

        /// <summary>"door-pick N": open its lock with a lockpick, as the player does (Locked.tryToLockpick).</summary>
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
                P.Inventory.addItemTypeToPlayer("lockpick", 1, dropIfNoRoom: true);
            }
            yield return null;
            AsPlayer(() => l.tryToLockpick());
            yield return new WaitForSeconds(1.5f); // a refusal would lock it again
            r.Info = $"{d.name} {Ref(d)} locked={l.locked}";
            if (l.locked)
            {
                r.Fail($"{r.Info}: still locked");
            }
        }

        /// <summary>"door-barricade N": nail planks over it, as the hammer's three strokes do (Door.barricade(byPlayer)).</summary>
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
            r.Info = $"{d.name} {Ref(d)} barricaded={d.barricaded} health {d.barricadeHealth}";
            if (!d.barricaded)
            {
                r.Fail($"{r.Info}: not barricaded");
            }
        }

        /// <summary>"door-hit N DAMAGE": our player hits it (its barricade first).</summary>
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
            r.Info = $"{d.name} {Ref(d)} barricaded={d.barricaded} ({d.barricadeHealth}) health {d.health} broken={d.destroyed}";
            if (was.Same(WorldActions.DoorState.Of(d)))
            {
                r.Fail($"{r.Info}: the hit did nothing");
            }
        }

        /// <summary>"door-burn N": set it alight. The host's world burns it; a client's lights its copy and asks the
        /// host (FireSync.ClientIgnited, as its flamethrower does).</summary>
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
                if (Net.Session.Instance != null && Net.Session.Instance.IsClient)
                {
                    AsPlayer(() =>
                    {
                        FireSync instance = FireSync.Instance;
                        if (instance != null)
                        {
                            instance.ClientIgnited(d.gameObject);
                        }
                    });
                }
            }
            yield return new WaitForSeconds(1f);
            r.Info = $"{d.name} {Ref(d)} burning={d.GetComponent<Burn>() != null} health {d.health}";
            if (d.GetComponent<Burn>() == null)
            {
                r.Fail($"{r.Info}: not burning (refused?)");
            }
            yield return Walk(Centre, 60f); // away from the fire
        }

        /// <summary>"window-barricade N": planks over it, the hammer's three strokes (Window.barricade(0, byPlayer)).</summary>
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
            r.Info = $"{w.name} {Ref(w)} barricaded={w.barricaded} health {w.barricadeHealth}";
            if (!w.barricaded)
            {
                r.Fail($"{r.Info}: not barricaded");
            }
        }

        /// <summary>"window-hit N DAMAGE": our player hits its barricade.</summary>
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
            r.Info = $"{w.name} {Ref(w)} barricaded={w.barricaded} health {was} -> {w.barricadeHealth}";
            if (w.barricaded && w.barricadeHealth == was)
            {
                r.Fail($"{r.Info}: the hit did nothing");
            }
        }

        /// <summary>"expect-burning door N | window N | NAME [SECS]": it catches fire here within SECS (default 4).</summary>
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
            while (c != null && c.GetComponent<Burn>() == null && Time.realtimeSinceStartup < until)
            {
                yield return null;
            }
            if (c == null)
            {
                r.Info = "gone";
            }
            else
            {
                r.Info = $"{c.name} {Ref(c)} burning={c.GetComponent<Burn>() != null}";
            }
            if (c != null && c.GetComponent<Burn>() == null)
            {
                r.Fail($"{r.Info}: didn't catch fire here");
            }
        }

        // ---- containers -------------------------------------------------------------------------------------------

        internal static Inventory Container(string spec) =>
            Find<Inventory>(spec, i => i.invType == Inventory.InvType.itemInv && !i.isWorkbench && i != P.Inventory && i != P.Hotbar && !i.transform.IsChildOf(P.transform));

        /// <summary>"put-item CONTAINER [TYPE] [AMOUNT]": put something we carry (of TYPE) into it, as if its window were open.</summary>
        private static IEnumerator PutItem(string[] a, Result r)
        {
            var inv = Container(a[0]);
            if (inv == null)
            {
                r.Fail($"no container {a[0]}");
                yield break;
            }
            var item = Carried().FirstOrDefault(i => a.Length < 2 || i.type == a[1]);
            if (item == null)
            {
                string a2 = "anything";
                if (a.Length > 1)
                {
                    a2 = a[1];
                }
                r.Fail($"not carrying {a2}");
                yield break;
            }
            string type = item.type;
            int val1 = 1;
            if (a.Length > 2)
            {
                val1 = int.Parse(a[2]);
            }
            int amount = Math.Min(val1, item.amount);
            yield return Walk(inv.transform.position);
            P.openedItemInventory = inv;
            yield return new WaitForSeconds(0.8f); // a client asks what it holds first
            int before = inv.getAllItems().Where(i => i.type == type).Sum(i => i.amount);
            AsPlayer(() =>
            {
                P.Inventory.removeItemAmountFromPlayer(type, amount);
                inv.addItemType(type, amount);
            });
            yield return new WaitForSeconds(1f);
            P.openedItemInventory = null;
            yield return new WaitForSeconds(1f);
            int after = inv.getAllItems().Where(i => i.type == type).Sum(i => i.amount);
            r.Info = $"{type} x{amount} into {inv.name} {Ref(inv)}: [{Contents(inv)}]";
            if (after <= before)
            {
                r.Fail($"{r.Info}: it isn't in there (refused?)");
            }
        }

        /// <summary>"take-from CONTAINER [--refused]": take its first item. With --refused, the host should say no and
        /// the item go back.</summary>
        private static IEnumerator TakeFrom(string[] a, Result r)
        {
            bool refused = a.Contains("--refused");
            a = a.Where(x => x != "--refused").ToArray();
            var inv = Container(a[0]);
            if (inv == null)
            {
                r.Fail($"no container {a[0]}");
                yield break;
            }
            yield return Walk(inv.transform.position);
            P.openedItemInventory = inv;
            yield return new WaitForSeconds(0.8f);
            var slot = inv.slots.FirstOrDefault(s => s != null && !InvItemClass.isNull(s.invItem));
            if (slot == null)
            {
                P.openedItemInventory = null;
                r.Fail($"{inv.name} {Ref(inv)} is empty here");
                yield break;
            }
            var item = new InvItemClass(slot.invItem);
            int before = CarriedOf(item.type);
            AsPlayer(() =>
            {
                slot.removeItem();
                P.Inventory.addItemTypeToPlayer(item.type, item.amount, dropIfNoRoom: true);
            });
            yield return new WaitForSeconds(1.5f);
            P.openedItemInventory = null;
            yield return new WaitForSeconds(1f);
            int now = CarriedOf(item.type);
            r.Info = $"{item.type} x{item.amount} from {inv.name} {Ref(inv)}: we carry {before} -> {now}; it holds [{Contents(inv)}]";
            if (refused && now > before)
            {
                r.Fail($"{r.Info}: the host let us take it");
            }
            if (!refused && now <= before)
            {
                r.Fail($"{r.Info}: not in our bags");
            }
        }

        private static Inventory _held;

        /// <summary>"hold-open CONTAINER": open it and keep it open (until "release").</summary>
        private static IEnumerator HoldOpen(string[] a, Result r)
        {
            var inv = Container(a[0]);
            if (inv == null)
            {
                r.Fail($"no container {a[0]}");
                yield break;
            }
            yield return Walk(inv.transform.position);
            P.openedItemInventory = inv;
            _held = inv;
            yield return new WaitForSeconds(1f);
            r.Info = $"holding {inv.name} {Ref(inv)} open";
        }

        private static IEnumerator Release(string[] a, Result r)
        {
            if (P.openedItemInventory == _held)
            {
                P.openedItemInventory = null;
            }
            if (_held != null)
            {
                r.Info = $"closed {_held.name}";
            }
            else
            {
                r.Info = "nothing held";
            }
            _held = null;
            yield return new WaitForSeconds(0.5f);
        }

        /// <summary>"open-busy CONTAINER": open it while the other player has it: we should be told "X is using this".</summary>
        private static IEnumerator OpenBusy(string[] a, Result r)
        {
            var inv = Container(a[0]);
            if (inv == null)
            {
                r.Fail($"no container {a[0]}");
                yield break;
            }
            yield return Walk(inv.transform.position);
            float t0 = Time.realtimeSinceStartup;
            P.openedItemInventory = inv;
            string msg = null;
            while (msg == null && Time.realtimeSinceStartup < t0 + 3f)
            {
                msg = Messages.Where(m => m.at >= t0 && m.msg.Contains("is using this")).Select(m => m.msg).FirstOrDefault();
                yield return null;
            }
            P.openedItemInventory = null;
            yield return new WaitForSeconds(0.5f);
            string name = inv.name;
            string id = Ref(inv);
            string message = msg;
            if (message == null)
            {
                message = "no message";
            }
            r.Info = $"{name} {id}: {message}";
            if (msg == null)
            {
                r.Fail($"{r.Info}: we weren't told someone is using it");
            }
        }

        /// <summary>"pick-crate NAME": open a locked crate as the player does. One that's picked with the progress bar
        /// (Item.disarmTime, like the wooden loot crate): the bar's end, Player.progressBarCompleted with the crate as
        /// the trap being disarmed; the roll is the host's, so it's tried until it opens. One with a lock (Locked,
        /// like crate_locked_difficulty1): a lockpick (Locked.tryToLockpick).</summary>
        private static IEnumerator PickCrate(string[] a, Result r)
        {
            var crate = Find<Item>(a[0], i => WorldActions.Crate(i) != null && !i.searched || i.GetComponent<Locked>() is Locked lk && lk.locked);
            if (crate == null)
            {
                r.Fail($"no locked crate {a[0]}");
                yield break;
            }
            yield return Walk(crate.transform.position);
            var l = crate.GetComponent<Locked>();
            int tries = 0;
            if (WorldActions.Crate(crate) != null && !crate.searched)
            {
                while (!crate.searched && tries < 12)
                {
                    tries++;
                    FinishDisarm(crate);
                    yield return new WaitForSeconds(1.2f);
                }
                P.openedItemInventory = null;
                yield return new WaitForSeconds(1f);
                r.Info = $"{crate.name} {Ref(crate)} picked: searched={crate.searched} after {tries} tries (chance {crate.disarmChance:0.00})";
                if (!crate.searched)
                {
                    r.Fail($"{r.Info}: still shut");
                }
            }
            else
            {
                if (CarriedOf("lockpick") == 0)
                {
                    P.Inventory.addItemTypeToPlayer("lockpick", 1, dropIfNoRoom: true);
                }
                yield return null;
                AsPlayer(() => l.tryToLockpick());
                yield return new WaitForSeconds(1.5f); // a refusal would lock it again
                r.Info = $"{crate.name} {Ref(crate)} lockpicked: locked={l.locked}";
                if (l.locked)
                {
                    r.Fail($"{r.Info}: still locked");
                }
            }
        }

        /// <summary>"pickup TYPE": pick up a dropped item of TYPE with a click (Item.getDroppedItem).</summary>
        private static IEnumerator Pickup(string[] a, Result r)
        {
            var it = Find<Item>("DroppedItem", i => i.GetComponent<Inventory>() is Inventory inv && inv.slots.Count > 0 && !InvItemClass.isNull(inv.slots[0].invItem)
                && inv.slots[0].invItem.type == a[0]);
            if (it == null)
            {
                r.Fail($"no dropped {a[0]} here");
                yield break;
            }
            yield return Walk(it.transform.position);
            string what = $"{a[0]} {Ref(it)} at {Fmt(it.transform.position)}";
            int before = CarriedOf(a[0]);
            AsPlayer(() => it.getDroppedItem());
            yield return new WaitForSeconds(1.5f);
            int after = CarriedOf(a[0]);
            string state = "still there";
            if (it == null)
            {
                state = "gone";
            }
            r.Info = $"{what}: we carry {before} -> {after}, it's {state}";
            if (it != null || CarriedOf(a[0]) <= before)
            {
                r.Fail($"{r.Info}: not picked up");
            }
        }

        // ---- things in the world -----------------------------------------------------------------------------------

        /// <summary>"drag NAME DX DZ": take hold of it (Player.itemBeingDragged, as Item.startDragging sets) and move it.</summary>
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
            yield return new WaitForSeconds(2.5f); // where it came to rest goes out once settled
            r.Info = $"{it.name} {Ref(it)} from {Fmt(start)} to {Fmt(it.transform.position)}";
            if (!held)
            {
                string info = r.Info;
                DragSync drag = DragSync.Instance;
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
            else if (StateDump.Flat(it.transform.position - start) < StateDump.Flat(by) * 0.5f)
            {
                r.Fail($"{r.Info}: it didn't move");
            }
        }

        /// <summary>
        /// Holds it (Player.itemBeingDragged, as Item.startDragging sets) and moves it by `by` over secs, with the
        /// player behind it, then lets go. Stops moving it as soon as our hold is let go of (another player has it).
        /// held(true) if we held it to the end.
        /// </summary>
        private static IEnumerator DragFor(Item it, Vector3 by, float secs, Action<bool> held)
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
                P.transform.position = new Vector3(to.x, P.transform.position.y, to.z) - by.normalized * 70f;
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
        /// "drag-race NAME DX DZ AT": at epoch second AT take hold of it and drag it, as another player does at the same
        /// moment. Replies "got NAME ..." if our drag held to the end, "lost NAME ..." if it was let go of because the
        /// other player had it (the host decides who); either way where it came to rest.
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
            double Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
            if (Now() > at)
            {
                r.Fail($"walked past the moment ({Now() - at:0.0} s late)");
                yield break;
            }
            while (Now() < at)
            {
                yield return null;
            }
            int cancelled = DragSync.Cancelled;
            bool held = false;
            yield return DragFor(it, by, 1.5f, h => held = h);
            yield return new WaitForSeconds(3f);
            string text = "lost";
            if (held)
            {
                text = "got";
            }
            r.Info = $"{text} {it.name} {Ref(it)} ({DragSync.Cancelled - cancelled} drag(s) of ours let go of), at rest at {Fmt(it.transform.position)}";
            if (held == (DragSync.Cancelled > cancelled))
            {
                r.Fail($"{r.Info}: held to the end and let go of don't agree");
            }
        }

        /// <summary>"fuel NAME AMOUNT": pour gasoline into a generator or the saw, a unit at a time, as the player does.</summary>
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
            r.Info = $"{c.name} {Ref(c)} fuel {before:0.0} -> {after:0.0} (max {max:0})";
            if (after <= before + amount * 0.5f && before < max - 1f)
            {
                r.Fail($"{r.Info}: the fuel didn't go in");
            }
        }

        /// <summary>"switch NAME on|off": switch a lamp, generator, radio... (Item.turnOn / turnOff). A generator's
        /// lamps must follow.</summary>
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
                lamps = " lamps " + string.Join(",", gen.powerItems.Where(l => l != null).Select(l => $"{l.name}={l.isOn}"));
            }
            r.Info = $"{it.name} {Ref(it)} on={it.isOn}{lamps}";
            if (it.isOn != on)
            {
                r.Fail($"{r.Info}: didn't switch");
            }
        }

        /// <summary>"use NAME": what a player does with it: an InteractiveItem's switch, an Item's switch, or building
        /// it (Constructible.construct).</summary>
        private static IEnumerator Use(string[] a, Result r)
        {
            var t = Find<Transform>(a[0], x => x.GetComponentInChildren<Item>() != null || x.GetComponentInChildren<InteractiveItem>() != null || x.GetComponentInChildren<Constructible>() != null);
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
                r.Info = $"{t.name} {Ref(ii)} interactive on {was} -> {ii.isOn}";
                if (ii.isOn == was)
                {
                    r.Fail($"{r.Info}: didn't switch");
                }
            }
            else if (con != null && !con.constructed)
            {
                AsPlayer(() => con.construct(false, 0));
                yield return new WaitForSeconds(1f);
                bool built = con == null || con.constructed; // it removes itself once built, unless it's kept
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
                string item = it?.name;
                if (item == null)
                {
                    item = "-";
                }
                string constructed = con?.constructed.ToString();
                if (constructed == null)
                {
                    constructed = "-";
                }
                r.Fail($"{name} {id}: nothing to do with it (item {item}, constructed {constructed})");
            }
        }

        /// <summary>"hit NAME DAMAGE": our player hits a piece of furniture (Item.getHit).</summary>
        private static IEnumerator Hit(string[] a, Result r)
        {
            // "*": the nearest piece of plain furniture (no container, trap or explosive in it).
            Item it;
            if (a[0] == "*")
            {
                it = Nearest(Object.FindObjectsOfType<Item>().Where(i => i.destructible && !i.destroyed && i.health > 1 && !i.hasInventory && i.GetComponent<Explodes>() == null
                                    && i.GetComponent<Trigger>() == null && i.GetComponent<Character>() == null && ObjRef.Of(i.gameObject).Valid), Centre);
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
                r.Info = $"{it.name} {Ref(it)} health {was} -> {it.health} broken={it.destroyed}";
            }
            if (it != null && it.health == was && !it.destroyed)
            {
                r.Fail($"{r.Info}: the hit did nothing");
            }
        }

        /// <summary>"trap-fire NAME": our player steps into a trap (its trigger with our player's collider).</summary>
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
            AsPlayer(() => AccessTools.Method(typeof(Trigger), "OnAfterTrigger").Invoke(t, new object[] { col, false }));
            yield return new WaitForSeconds(1f);
            if (t == null)
            {
                r.Info = $"{a[0]} gone";
            }
            else
            {
                r.Info = $"{t.name} {Ref(t)} triggered={t.triggered} active={t.active}";
            }
            if (t != null && !t.triggered)
            {
                r.Fail($"{r.Info}: didn't go off");
            }
            // Out of it again (a bear trap holds the player that stepped in).
            yield return Walk(Centre, 60f);
        }

        /// <summary>
        /// "trap-step NAME [refused]": our player walks into a trap the game's way (Trigger.checkCollision with our
        /// player's collider: the bite, the hold, a vine trap's vine on us). With "refused" (the host was set to refuse
        /// it, "trap-refuse"), after the verdict our player must be out of any bear trap's hold with the health it had.
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
            int rolledBack = Authority.RolledBack;
            AsPlayer(() => t.checkCollision(col));
            bool fired = t == null || t.triggered;
            for (float until = Time.time + 6f; Time.time < until && Authority.Waiting > 0;)
            {
                yield return null;
            }
            yield return new WaitForSeconds(1.5f);
            r.Info = $"{name} {at} went off={fired}, health {health:0} -> {P.health:0}, inBearTrap={P.inBearTrap}, rolled back {Authority.RolledBack - rolledBack}";
            if (!fired)
            {
                r.Fail($"{r.Info}: didn't go off");
                yield break;
            }
            if (a.Length > 1 && a[1] == "refused")
            {
                if (Authority.RolledBack == rolledBack)
                {
                    r.Fail($"{r.Info}: the host didn't refuse it");
                }
                else if (P.inBearTrap)
                {
                    r.Fail($"{r.Info}: still held by a trap the host refused");
                }
                else if (P.health < health - 0.5f)
                {
                    r.Fail($"{r.Info}: the refused trap's bite wasn't given back");
                }
            }
            if (P.inBearTrap)
            {
                P.stopAllPerformingActionAnims();
            }
            // Well off the spot: back at the centre (60 from it) our player set off the next trap placed there.
            yield return Walk(Centre + new Vector3(0f, 0f, 250f), 60f);
        }

        /// <summary>"trap-refuse race|gone|off" (host): refuse the client's next TrapFired requests (TrapFiredPatch.TestRefuse).</summary>
        private static IEnumerator TrapRefuse(string[] a, Result r)
        {
            if (a[0] == "off")
            {
                TrapFiredPatch.TestRefuse = null;
            }
            else
            {
                TrapFiredPatch.TestRefuse = a[0];
            }
            string testRefuse = TrapFiredPatch.TestRefuse;
            if (testRefuse == null)
            {
                testRefuse = "as usual";
            }
            r.Info = $"TrapFired requests: {testRefuse}";
            yield break;
        }

        /// <summary>"trap-place PREFAB DX DZ": place a trap there (centre + offset) the way the player does: the item in
        /// our hands, its proxy there, and Player.progressBarCompleted (MapScenarioItems1.PlaceReal).</summary>
        private static IEnumerator TrapPlace(string[] a, Result r)
        {
            var at = Centre + new Vector3(F(a[1]), 0f, F(a[2]));
            at.y = P.transform.position.y;
            // Not too near: one placed 50 from our player sprang on it at once (the walk stops between half and all
            // of the distance given).
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
                r.Info = $"{go.name} {ObjRef.Of(go)} at {Fmt(go.transform.position)}";
            }
            if (go == null)
            {
                r.Fail($"{a[0]}: none placed and shared");
            }
            yield return Walk(Centre + new Vector3(0f, 0f, 250f), 60f); // off it
        }

        /// <summary>"place-furniture DX DZ": place a piece of furniture that can be broken there (centre + offset), as
        /// a placed trap is placed (shared as ObjSpawn): the first of a few prefabs that is breakable. Each is first
        /// tried unshared, in this game only, and only the one kept is placed as shared: a tried one that isn't kept
        /// is removed here with Object.Destroy, which no other game hears of, so it mustn't have been sent (a slow
        /// frame let the tried chair's ObjSpawn out before it was removed, and it stayed in the client's game).</summary>
        private static IEnumerator PlaceFurniture(string[] a, Result r)
        {
            var at = Centre + new Vector3(F(a[0]), 0f, F(a[1]));
            at.y = P.transform.position.y;
            yield return Walk(at, 150f);
            foreach (var prefab in new[] { "Furnitures/Cottage/chair_1", "Furnitures/Cottage/table_1", "Furnitures/Cottage/shelf" })
            {
                var tried = Core.AddPrefab(prefab, at, Quaternion.Euler(90f, 0f, 0f), Core.ItemContainer, worldSpace: true);
                // its health is set in Start
                yield return null;
                yield return null;
                Item t = null;
                if (tried != null)
                {
                    t = tried.GetComponent<Item>();
                }
                bool breakable = t != null && t.destructible && !t.destroyed && t.health > 1 && !t.hasInventory;
                if (tried != null)
                {
                    Object.Destroy(tried);
                }
                if (!breakable)
                {
                    continue;
                }
                yield return null; // the tried one is gone
                GameObject made;
                Containers.Placing = true;
                try
                {
                    made = Core.AddPrefab(prefab, at, Quaternion.Euler(90f, 0f, 0f), Core.ItemContainer, worldSpace: true);
                }
                finally
                {
                    Containers.Placing = false;
                }
                yield return new WaitForSeconds(2f);
                Item it = null;
                if (made != null)
                {
                    it = made.GetComponent<Item>();
                }
                if (it == null)
                {
                    r.Fail($"{prefab}: none placed");
                    yield break;
                }
                r.Info = $"{prefab} {ObjRef.Of(made)} at {Fmt(made.transform.position)}, health {it.health}";
                yield break;
            }
            r.Fail("none of the prefabs is breakable furniture");
        }

        private static Item _worldMade; // the chair world-furniture made in this game
        private static int _worldMadeHealth;

        /// <summary>"world-furniture DX DZ" (both): a breakable chair appears there (centre + offset) as the world makes
        /// things by itself (as a location spawns its characters): in each game on its own, not shared as ObjSpawn. The
        /// host gives it an id (HostIds), and the client's copy, made within its time window near the spot, is tied to it.</summary>
        private static IEnumerator WorldFurniture(string[] a, Result r)
        {
            var at = Centre + new Vector3(F(a[0]), 0f, F(a[1]));
            at.y = P.transform.position.y;
            yield return Walk(at, 150f);
            var made = Core.AddPrefab("Furnitures/Cottage/chair_1", at, Quaternion.Euler(90f, 0f, 0f), Core.ItemContainer, worldSpace: true);
            if (made != null)
            {
                Core.addToSaveable(made, isDynamic: true);
            }
            yield return new WaitForSeconds(3f); // its Start, then the host's id and the client's tie
            if (made != null)
            {
                _worldMade = made.GetComponent<Item>();
            }
            else
            {
                _worldMade = null;
            }
            if (_worldMade == null)
            {
                r.Fail("no chair made");
                yield break;
            }
            _worldMadeHealth = _worldMade.health;
            r.Info = $"{made.name} {ObjRef.Of(made)} at {Fmt(made.transform.position)}, health {_worldMade.health}, ids given {HostIds.Given}, tied {HostIds.Tied}";
        }

        /// <summary>"ids-tied [hit]" (both): the chair world-furniture made is named by the host's id (a Spawned ref that
        /// finds it here); with "hit", the host's hit on it came here too.</summary>
        private static IEnumerator IdsTied(string[] a, Result r)
        {
            if (_worldMade == null)
            {
                r.Fail("world-furniture made nothing here");
                yield break;
            }
            bool hit = a.Length > 0 && a[0] == "hit";
            float until = Time.realtimeSinceStartup + 5f;
            while (hit && _worldMade != null && _worldMade.health >= _worldMadeHealth && !_worldMade.destroyed && Time.realtimeSinceStartup < until)
            {
                yield return new WaitForSeconds(0.25f);
            }
            if (_worldMade == null)
            {
                r.Info = "gone";
                if (!hit)
                {
                    r.Fail("the chair is gone");
                }
                yield break;
            }
            var go = _worldMade.gameObject;
            var at = ObjRef.Of(go);
            r.Info = $"{go.name} {at} tied {ObjRef.IsShared(go)}, health {_worldMadeHealth} -> {_worldMade.health} broken={_worldMade.destroyed}";
            if (at.K != ObjRef.Kind.Spawned || !ObjRef.IsShared(go) || ObjRef.Resolve<Item>(at) != _worldMade)
            {
                r.Fail($"{r.Info}: not named by a host id");
            }
            else if (hit && _worldMade.health >= _worldMadeHealth && !_worldMade.destroyed)
            {
                r.Fail($"{r.Info}: the host's hit didn't come here");
            }
        }

        /// <summary>"ids-hit" (host): our player hits the chair world-furniture made.</summary>
        private static IEnumerator IdsHit(string[] a, Result r)
        {
            if (_worldMade == null)
            {
                r.Fail("world-furniture made nothing here");
                yield break;
            }
            yield return Walk(_worldMade.transform.position);
            AsPlayer(() => _worldMade.getHit(10, P.transform));
            yield return new WaitForSeconds(1f);
            if (_worldMade == null)
            {
                r.Info = "gone";
            }
            else
            {
                r.Info = $"health {_worldMadeHealth} -> {_worldMade.health} {ObjRef.Of(_worldMade.gameObject)}";
            }
        }

        private static readonly AccessTools.FieldRef<Player, Item> TrapBeingDisarmed = AccessTools.FieldRefAccess<Player, Item>("trapBeingDisarmed");

        /// <summary>The progress bar's end as the player's action, with it as the trap being disarmed (Player.progressBarCompleted).</summary>
        private static void FinishDisarm(Item it) => AsPlayer(() =>
        {
            P.disarmingTrap = true;
            TrapBeingDisarmed(P) = it;
            P.progressBarCompleted();
        });

        /// <summary>"trap-disarm NAME": crouch over it and disarm it (the progress bar's end; a client's roll is the
        /// host's). It's either disarmed (gone, into our bags) or sprung.</summary>
        private static IEnumerator TrapDisarm(string[] a, Result r)
        {
            var t = Find<Trigger>(a[0], x => x.active && !x.triggered && x.canDisarm && x.GetComponent<Item>() != null);
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
            while (t != null && !t.triggered && Time.realtimeSinceStartup < until)
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

        /// <summary>"explode NAME": shoot an exploding barrel or gas bottle from out of its reach (Explodes.onActivate).</summary>
        private static IEnumerator Explode(string[] a, Result r)
        {
            var e = Find<Explodes>(a[0], x => !BreakSync.Activated(x));
            if (e == null)
            {
                r.Fail($"nothing to explode called {a[0]}");
                yield break;
            }
            yield return Walk(AwayFrom(e.transform.position, Mathf.Max(e.radius + 250f, 500f)), 40f);
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

        /// <summary>"ignite NAME": set it alight (a Burn, as fire does); an explosive that goes off on fire goes off.</summary>
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
            string what = $"{it.name} {Ref(it)} at {Fmt(it.transform.position)}";
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
            string text2 = "";
            if (explosive)
            {
                text2 = $" exploded={exploded}";
            }
            r.Info = $"{what}: {text}{text2}";
            if (explosive && !exploded)
            {
                r.Fail($"{r.Info}: lit, but it didn't go off");
            }
            else if (!explosive && it != null && it.GetComponent<Burn>() == null)
            {
                r.Fail($"{r.Info}: not burning");
            }
            yield return Walk(Centre, 60f);
        }

        /// <summary>"throw-molotov NAME": throw a molotov at it from 300 away, as the player's throw does (ThrowSync);
        /// something there must catch fire.</summary>
        private static IEnumerator ThrowMolotov(string[] a, Result r)
        {
            var target = Find<Transform>(a[0], x => x.GetComponent<Item>() != null);
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
            // Made inside our player's collider, it hit our player at once and broke there, setting the thrower
            // alight (Player.throwItem's held item is the player's child until thrown). Make it a step ahead, and
            // let it pass through our player.
            from += dir * 50f;
            dist -= 50f;
            var go = Core.AddPrefab(mi.item, from, Quaternion.Euler(90f, 0f, 0f), null);
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
            // Something near where it lands catches fire (not what was burning already).
            float until = Time.realtimeSinceStartup + 15f;
            Burn lit = null;
            while (lit == null && Time.realtimeSinceStartup < until)
            {
                yield return new WaitForSeconds(0.25f);
                lit = Object.FindObjectsOfType<Burn>().Where(b => b != null && !before.Contains(b) && b.GetComponent<Player>() == null && StateDump.Flat(b.transform.position - tpos) < 350f).FirstOrDefault();
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

        // ---- the story, traders, enemies ---------------------------------------------------------------------------

        /// <summary>"flag N": flip the Nth story flag (Flags.setFlag).</summary>
        private static IEnumerator Flag(string[] a, Result r)
        {
            var ordered = StateDump.FlagsDict(Singleton<Flags>.Instance).Values.Where(f => f != null).OrderBy(f => f.name, StringComparer.Ordinal);
            int index = 0;
            if (a.Length > 0)
            {
                index = int.Parse(a[0]);
            }
            var flag = ordered.Skip(index).FirstOrDefault();
            if (flag == null)
            {
                r.Fail("no such flag");
                yield break;
            }
            bool to = !flag.isTrue;
            Singleton<Flags>.Instance.setFlag(flag.name, to);
            yield return new WaitForSeconds(0.5f);
            r.Info = $"{flag.name} -> {flag.isTrue}";
            if (flag.isTrue != to)
            {
                r.Fail($"{r.Info}: didn't change");
            }
        }

        /// <summary>"reputation N DELTA": change the Nth NPC's reputation.</summary>
        private static IEnumerator Reputation(string[] a, Result r)
        {
            var npc = Singleton<Flags>.Instance.npcStates.Where(n => n != null).OrderBy(n => n.name, StringComparer.Ordinal).Skip(int.Parse(a[0])).FirstOrDefault();
            if (npc == null)
            {
                r.Fail("no such NPC");
                yield break;
            }
            int was = npc.reputation;
            npc.reputation += int.Parse(a[1]);
            yield return new WaitForSeconds(0.5f);
            r.Info = $"{npc.name} reputation {was} -> {npc.reputation}";
        }

        /// <summary>"journal entry|note": add a journal entry or note this game hasn't got, as the game adds them.</summary>
        private static IEnumerator JournalStep(string[] a, Result r)
        {
            var journal = Singleton<UI>.Instance.journal;
            var jdb = Singleton<JournalDatabase>.Instance;
            if (jdb != null && jdb.journalEntriesDict.Count == 0)
            {
                jdb.populateDict();
            }
            if (jdb == null)
            {
                r.Fail("no journal database");
                yield break;
            }
            if (a.Length > 0 && a[0] == "note")
            {
                string note = jdb.notesDict.Keys.OrderBy(k => k, StringComparer.Ordinal).FirstOrDefault(k => !journal.notesDict.ContainsKey(k));
                if (note == null)
                {
                    r.Fail("every note is in already");
                    yield break;
                }
                journal.addNote(new Journal.Note { type = note });
                r.Info = $"note {note}";
                if (!journal.notesDict.ContainsKey(note))
                {
                    r.Fail($"{r.Info}: not added");
                }
            }
            else
            {
                string entry = jdb.journalEntriesDict.Keys.OrderBy(k => k, StringComparer.Ordinal).FirstOrDefault(k => !journal.journalEntriesDict.ContainsKey(k));
                if (entry == null)
                {
                    r.Fail("every entry is in already");
                    yield break;
                }
                journal.addJournalEntry(entry);
                r.Info = $"entry {entry}";
                if (!journal.journalEntriesDict.ContainsKey(entry))
                {
                    r.Fail($"{r.Info}: not added");
                }
            }
            yield return new WaitForSeconds(0.5f);
        }

        internal static NPC Trader() => Nearest(Object.FindObjectsOfType<NPC>().Where(n => n.trader && n.inventory != null), Centre);

        /// <summary>"trader-restock": the trader stocks up, as on a new day (the host's to do).</summary>
        private static IEnumerator TraderRestock(string[] a, Result r)
        {
            var npc = Trader();
            if (npc == null)
            {
                r.Fail("no trader here");
                yield break;
            }
            string was = Contents(npc.inventory);
            AccessTools.Method(typeof(NPC), "randomizeTraderInv").Invoke(npc, null);
            yield return new WaitForSeconds(1f);
            r.Info = $"{npc.name}: [{was}] -> [{Contents(npc.inventory)}]";
        }

        /// <summary>"trade": with the trade window open, give the trader things from our bags worth at least his
        /// cheapest thing and take that (as DialogueWindow.acceptTrade moves them), the difference in value onto the
        /// reputation as acceptTrade does. A client's trade goes to the host, which checks it against his stock; a
        /// refusal (rolled back here) fails the step. The dumps then compare his stock in both games.
        /// "trade --race AT": the same, at epoch second AT (players trading with him at the same moment); a refusal
        /// doesn't fail it, the reply says "got ..." or "refused ...".
        /// "trade --earn": his reputation is brought down to 0 (and that reaches the host), then our player earns as
        /// much as his cheapest thing is worth and, in the same frame, takes it for nothing, paid with that reputation:
        /// the host must have the earned reputation when it checks the trade (accepted).</summary>
        private static IEnumerator TradeSeen(string[] a, Result r)
        {
            yield return new WaitForSeconds(1f);
            var lines = MapScenarioIssues.LogLines();
            string took = lines.LastOrDefault(l => l.Contains(" traded with ") && l.Contains(": took "));
            string refused = lines.LastOrDefault(l => l.Contains("[authority] refused TraderTrade"));
            string took2 = took;
            if (took2 == null)
            {
                took2 = refused;
            }
            if (took2 == null)
            {
                took2 = "no client's trade here";
            }
            r.Info = took2.Trim();
            if (refused != null)
            {
                r.Fail($"refused: {refused.Trim()}");
            }
            else if (took == null)
            {
                r.Fail(r.Info);
            }
        }

        private static IEnumerator Trade(string[] a, Result r)
        {
            bool earn = Array.IndexOf(a, "--earn") >= 0;
            int ri = Array.IndexOf(a, "--race");
            double raceAt = 0;
            if (ri >= 0 && a.Length > ri + 1)
            {
                raceAt = double.Parse(a[ri + 1], CultureInfo.InvariantCulture);
            }
            var npc = Trader();
            if (npc == null)
            {
                r.Fail("no trader here");
                yield break;
            }
            yield return Walk(npc.transform.position, 150f);
            // His cheapest thing, paid for with things from our bags (not the hotbar: later steps use what we hold),
            // the most valuable first, until they're worth as much.
            var slot = npc.inventory.slots.Where(s => s != null && !InvItemClass.isNull(s.invItem) && (!earn || s.invItem.getSellValue() > 0)).OrderBy(s => s.invItem.getSellValue()).FirstOrDefault();
            if (slot == null)
            {
                r.Fail($"{npc.name} has nothing");
                yield break;
            }
            string boughtType = slot.invItem.type;
            int boughtValue = slot.invItem.getSellValue();
            var gave = new Dictionary<string, int>();
            int giveValue = 0;
            var ours = P.Inventory.getAllItems().Where(i => !InvItemClass.isNull(i) && !string.IsNullOrEmpty(i.type) && i.amount > 0 && i.type != boughtType && i.getSellValue() > 0).ToList();
            // One thing that pays for it, the cheapest such; else several, the most valuable first.
            var one = ours.Where(i => i.getSellValue() >= boughtValue).OrderBy(i => i.getSellValue()).FirstOrDefault();
            if (earn)
            {
                ours.Clear(); // nothing given: the reputation pays
            }
            List<InvItemClass> items;
            if (earn)
            {
                items = new List<InvItemClass>();
            }
            else if (one != null)
            {
                items = new List<InvItemClass> { one };
            }
            else
            {
                items = ours.OrderByDescending(i => i.getSellValue()).ToList();
            }
            foreach (var it in items)
            {
                for (int n = 0; n < Math.Max(1, it.amount) && giveValue < boughtValue; n++)
                {
                    gave.TryGetValue(it.type, out int had);
                    gave[it.type] = had + 1;
                    giveValue += it.getSellValue();
                }
            }
            int repChange = giveValue - boughtValue;
            string giveText = string.Join(",", gave.Select(kv => $"{kv.Key}x{kv.Value}"));
            if (earn)
            {
                // Down to 0 first, shared as any reputation change is, so the host's copy can't pay for the trade by itself.
                npc.reputation = 0;
                FlagsSync instance = FlagsSync.Instance;
                if (instance != null)
                {
                    instance.FlushNow();
                }
                yield return new WaitForSeconds(3f);
            }
            if (!earn && npc.reputation + repChange < 0)
            {
                r.Fail($"our bags ({giveText}: {giveValue}) don't pay for {boughtType} ({boughtValue}) at reputation {npc.reputation}");
                yield break;
            }
            P.inShop = true;
            P.talkedToNPC = npc;
            yield return new WaitForSeconds(0.8f);
            if (ri >= 0)
            {
                while (MapScenarioSavage.Now < raceAt)
                {
                    yield return null;
                }
                // What's cheapest may have changed meanwhile; the same slot as the others, as it is now.
                if (InvItemClass.isNull(slot.invItem) || slot.invItem.type != boughtType)
                {
                    P.inShop = false;
                    P.talkedToNPC = null;
                    r.Fail($"{boughtType} gone from his slot before the race");
                    yield break;
                }
            }
            string was = Contents(npc.inventory);
            int mark = MapScenarioIssues.LogMark();
            AsPlayer(() =>
            {
                if (earn)
                {
                    npc.reputation += boughtValue; // earned just now (as a dialogue's reward): only our game has it yet
                }
                // His thing to us, ours to him (exchangeTrader.transferAllItemsToPlayer, exchangePlayer.transferAllItemsTo).
                if (slot.invItem.amount > 1)
                {
                    slot.invItem.amount -= 1;
                }
                else
                {
                    slot.removeItem();
                }
                P.Inventory.addItemTypeToPlayer(boughtType, 1, dropIfNoRoom: true);
                foreach (var kv in gave)
                {
                    P.Inventory.removeItemAmountFromPlayer(kv.Key, kv.Value);
                    npc.inventory.addItemType(kv.Key, kv.Value);
                }
                npc.reputation += repChange;
                // What TraderTradePatch does after acceptTrade.
                TraderSync instance2 = TraderSync.Instance;
                if (instance2 != null)
                {
                    instance2.LocalTrade(npc, new Dictionary<string, int> { [boughtType] = 1 }, gave, repChange);
                }
            });
            yield return new WaitForSeconds(1.5f);
            P.inShop = false;
            P.talkedToNPC = null;
            yield return new WaitForSeconds(1f);
            string refused = MapScenarioIssues.LogLines().Skip(mark).FirstOrDefault(l => l.Contains("refused trade with"));
            string text;
            if (earn)
            {
                text = $"nothing, earned {boughtValue} reputation first";
            }
            else
            {
                text = giveText;
            }
            r.Info = $"{npc.name}: bought {boughtType} ({boughtValue}), gave {text} ({giveValue}): [{was}] -> [{Contents(npc.inventory)}], reputation {npc.reputation}";
            if (ri >= 0)
            {
                string value = "got ";
                if (refused != null)
                {
                    value = "refused ";
                }
                r.Info = value + r.Info;
                yield break;
            }
            if (refused != null)
            {
                r.Fail($"{r.Info}: {refused.Trim()}");
            }
            else if (Contents(npc.inventory) == was)
            {
                r.Fail($"{r.Info}: his stock didn't change");
            }
        }

        /// <summary>"spawn TYPE": the host spawns an enemy near its player (CharacterSpawner).</summary>
        private static IEnumerator Spawn(string[] a, Result r)
        {
            if (Net.Session.Instance != null && Net.Session.Instance.IsClient)
            {
                r.Fail("enemies are the host's");
                yield break;
            }
            string typeValue = "Dog";
            if (a.Length > 0)
            {
                typeValue = a[0];
            }
            var c = TestWorld.SpawnEnemy(typeValue);
            yield return new WaitForSeconds(1f);
            if (c != null)
            {
                r.Info = $"{c.name} {Ref(c)} at {Fmt(c.transform.position)}";
            }
            else
            {
                r.Info = "none";
            }
            if (c == null)
            {
                r.Fail("nothing spawned");
            }
        }

        /// <summary>The host's live enemy nearest to our player, within radius of the centre.</summary>
        internal static Character HostEnemyNear(float radius)
        {
            var ne = Combat.NetEnemies.Instance;
            if (ne == null)
            {
                return null;
            }
            return Nearest(Object.FindObjectsOfType<Character>().Where(x => x.alive && Combat.NetEnemies.Eligible(x) && ne.NetIdOf(x, out _)
                            && StateDump.Flat(x.transform.position - Centre) < radius), P.transform.position);
        }

        /// <summary>"kill-enemy": our player hits the host's enemy nearest to us until it's dead (a client's hits go to
        /// the host, capped by its weapon).</summary>
        private static IEnumerator KillEnemy(string[] a, Result r)
        {
            var c = HostEnemyNear(1500f);
            if (c == null)
            {
                r.Fail("no enemy of the host's here");
                yield break;
            }
            string what = $"{c.name} {Ref(c)}";
            int hits = 0;
            while (c != null && c.alive && hits < 40)
            {
                yield return Walk(c.transform.position, 80f);
                hits++;
                AsPlayer(() => c.getHit(60f, P.transform, false, true, true, true, false, false, false));
                yield return new WaitForSeconds(0.4f);
            }
            yield return new WaitForSeconds(3f); // a client settles the corpse 2 s after it dies
            bool alive = c != null && c.alive;
            bool searchable = c != null && c.GetComponent<Item>() != null;
            string at = "-";
            if (c != null)
            {
                at = Fmt(c.transform.position);
            }
            r.Info = $"{what}: alive={alive} after {hits} hits, searchable={searchable} at {at}";
            if (c != null && c.alive)
            {
                r.Fail($"{r.Info}: still alive");
            }
            yield return Walk(Centre, 60f);
        }
    }
}
