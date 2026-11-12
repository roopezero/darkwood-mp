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
            Add("spawn", Spawn);
            Add("kill-enemy", KillEnemy, timeout: 40f);
        }

        // ---- finding things ------------------------------------------------------------------------------------

        private static string Clean(string n) => n.Replace("(Clone)", "").Trim();

        /// <summary>"NAME" or "NAME@DX,DZ": that one of these, nearest to the centre (or centre + offset).</summary>
        internal static T Find<T>(string spec, Func<T, bool> ok = null) where T : Component
        {
            string name = spec; var at = Centre;
            int i = spec.IndexOf('@');
            if (i >= 0)
            {
                name = spec.Substring(0, i);
                var d = spec.Substring(i + 1).Split(',');
                at += new Vector3(F(d[0]), 0f, F(d[1]));
            }
            var near = Object.FindObjectsOfType<T>().Where(t => t != null && StateDump.Flat(t.transform.position - Centre) < StateDump.DefaultRadius && (ok == null || ok(t))).ToList();
            // That name exactly, else one whose name starts with it.
            var exact = near.Where(t => Clean(t.gameObject.name) == name).ToList();
            return Nearest(exact.Count > 0 ? exact : near.Where(t => Clean(t.gameObject.name).StartsWith(name, StringComparison.Ordinal)), at);
        }

        /// <summary>The Nth nearest to the centre (broken ones counted too, so the numbers don't move).</summary>
        internal static T Nth<T>(string n) where T : Component =>
            Object.FindObjectsOfType<T>().Where(t => ObjRef.Of(t.gameObject).Valid)
                .OrderBy(t => Mathf.Round(StateDump.Flat(t.transform.position - Centre))).ThenBy(t => ObjRef.Of(t.gameObject).ToString(), StringComparer.Ordinal)
                .Skip(int.Parse(n)).FirstOrDefault();

        private static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);
        private static string Fmt(Vector3 p) => FormattableString.Invariant($"{p.x:0} {p.z:0}");
        internal static string Ref(Component c) => c == null ? "-" : ObjRef.Of(c.gameObject).ToString();
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
            Vector3 best = AwayFrom(pos, dist); float bestScore = float.MinValue;
            for (int deg = 0; deg < 360; deg += 20)
            {
                var spot = pos + Quaternion.Euler(0f, deg, 0f) * Vector3.forward * dist;
                spot.y = pos.y;
                float clear = others.Count == 0 ? 1000f : others.Min(o => StateDump.Flat(o - spot));
                // The way back: the nearest the walk from the spot to the centre comes to the fire.
                var seg = Centre - spot; seg.y = 0f;
                float t = seg.sqrMagnitude < 1f ? 0f : Mathf.Clamp01(Vector3.Dot(new Vector3(pos.x - spot.x, 0f, pos.z - spot.z), seg) / seg.sqrMagnitude);
                float back = StateDump.Flat(spot + seg * t - pos);
                float score = Mathf.Min(clear, back);
                if (score > bestScore) { bestScore = score; best = spot; }
            }
            return best;
        }

        /// <summary>A spot this far from pos, on the centre's side of it (to stand at while something goes off).</summary>
        private static Vector3 AwayFrom(Vector3 pos, float dist)
        {
            var d = pos - Centre; d.y = 0f;
            if (d.sqrMagnitude < 1f) d = Vector3.forward;
            return pos - d.normalized * dist;
        }

        // ---- messages the player is shown (for "X is using this") ---------------------------------------------

        internal static readonly List<(float at, string msg)> Messages = new List<(float, string)>();

        [HarmonyPatch(typeof(Player), nameof(Player.displayMessage), new[] { typeof(string), typeof(bool), typeof(bool) })]
        static class MessagePatch
        {
            static void Postfix(string message)
            {
                if (Messages.Count > 50) Messages.RemoveAt(0);
                Messages.Add((Time.realtimeSinceStartup, message ?? ""));
            }
        }

        // ---- inspect ---------------------------------------------------------------------------------------------

        /// <summary>"inspect NAME...": what each is made of (components on it and its children), and its ObjRef.</summary>
        private static IEnumerator Inspect(string[] a, Result r)
        {
            var parts = new List<string>();
            foreach (var name in a)
            {
                var t = Find<Transform>(name, x => x.parent == null || Clean(x.parent.name) != Clean(x.name));
                if (t == null) { parts.Add($"{name}: none"); continue; }
                var comps = t.GetComponentsInChildren<Component>(true).Where(c => c != null && !(c is Transform) && !(c is Renderer) && !(c is MeshFilter))
                    .Select(c => (c.transform == t ? "" : c.name + ".") + c.GetType().Name).Distinct().Take(14);
                string extra = "";
                if (t.GetComponent<Generator>() is Generator g) extra += $" fuel {g.fuel:0}/{g.maxFuel:0} powers {g.powerItems.Count}";
                if (t.GetComponent<Saw>() is Saw s) extra += $" fuel {s.fuel:0}/{s.maxFuel:0}";
                if (t.GetComponent<Explodes>() is Explodes e) extra += $" radius {e.radius:0} onIgnite {e.explodeOnIgnite}";
                if (t.GetComponent<NPC>() is NPC n) extra += $" trader {n.trader} stock [{(n.inventory != null ? Contents(n.inventory) : "-")}]";
                if (t.GetComponentInChildren<Item>() is Item it) extra += $" item {it.name} switchable {it.switchable} on {it.isOn} light {it.isLight} destructible {it.destructible} health {it.health} disarmTime {it.disarmTime} searched {it.searched}";
                if (t.GetComponentInChildren<Trigger>() is Trigger tr) extra += $" trigger {tr.name} active {tr.active} triggered {tr.triggered} canDisarm {tr.canDisarm}";
                if (t.GetComponentInChildren<Locked>() is Locked lk) extra += $" locked {lk.locked}";
                parts.Add($"{name} {ObjRef.Of(t.gameObject)} at {Fmt(t.position)}: {string.Join(" ", comps)};{extra}");
            }
            r.Info = string.Join(" | ", parts);
            Plugin.Log.LogInfo($"[map] inspect: {r.Info}");
            yield break;
        }

        // ---- doors and windows -----------------------------------------------------------------------------------

        private static Door DoorArg(string[] a, Result r)
        {
            var d = a.Length > 0 ? Nth<Door>(a[0]) : null;
            if (d == null) r.Fail("usage: door-... N (no such door)");
            return d;
        }

        /// <summary>"door-lock N": lock it (as the story does; Door.lockMe).</summary>
        private static IEnumerator DoorLock(string[] a, Result r)
        {
            var d = DoorArg(a, r); if (d == null) yield break;
            if (d.opened) { d.close(P.transform); yield return new WaitForSeconds(0.5f); }
            d.lockMe("");
            yield return new WaitForSeconds(0.5f);
            var l = d.GetComponent<Locked>();
            r.Info = $"{d.name} {Ref(d)} locked={l != null && l.locked}";
            if (l == null || !l.locked) r.Fail($"{r.Info}: not locked");
        }

        /// <summary>"door-pick N": open its lock with a lockpick, as the player does (Locked.tryToLockpick).</summary>
        private static IEnumerator DoorPick(string[] a, Result r)
        {
            var d = DoorArg(a, r); if (d == null) yield break;
            var l = d.GetComponent<Locked>();
            if (l == null || !l.locked) { r.Fail($"{d.name} {Ref(d)} isn't locked here"); yield break; }
            yield return Walk(d.transform.position);
            if (CarriedOf("lockpick") == 0) P.Inventory.addItemTypeToPlayer("lockpick", 1, dropIfNoRoom: true);
            yield return null;
            AsPlayer(() => l.tryToLockpick());
            yield return new WaitForSeconds(1.5f); // a refusal would lock it again
            r.Info = $"{d.name} {Ref(d)} locked={l.locked}";
            if (l.locked) r.Fail($"{r.Info}: still locked");
        }

        /// <summary>"door-barricade N": nail planks over it, as the hammer's three strokes do (Door.barricade(byPlayer)).</summary>
        private static IEnumerator DoorBarricade(string[] a, Result r)
        {
            var d = DoorArg(a, r); if (d == null) yield break;
            yield return Walk(d.transform.position);
            if (d.opened) { AsPlayer(() => d.close(P.transform)); yield return new WaitForSeconds(0.5f); }
            for (int i = 0; i < 3 && !d.barricaded; i++) { AsPlayer(() => d.barricade(byPlayer: true)); yield return new WaitForSeconds(0.3f); }
            yield return new WaitForSeconds(1f);
            r.Info = $"{d.name} {Ref(d)} barricaded={d.barricaded} health {d.barricadeHealth}";
            if (!d.barricaded) r.Fail($"{r.Info}: not barricaded");
        }

        /// <summary>"door-hit N DAMAGE": our player hits it (its barricade first).</summary>
        private static IEnumerator DoorHit(string[] a, Result r)
        {
            var d = DoorArg(a, r); if (d == null) yield break;
            int dmg = a.Length > 1 ? int.Parse(a[1]) : 20;
            yield return Walk(d.transform.position);
            var was = WorldActions.DoorState.Of(d);
            AsPlayer(() => d.getHit(dmg, P.transform));
            yield return new WaitForSeconds(1f);
            r.Info = $"{d.name} {Ref(d)} barricaded={d.barricaded} ({d.barricadeHealth}) health {d.health} broken={d.destroyed}";
            if (was.Same(WorldActions.DoorState.Of(d))) r.Fail($"{r.Info}: the hit did nothing");
        }

        /// <summary>"door-burn N": set it alight. The host's world burns it; a client's lights its copy and asks the
        /// host (FireSync.ClientIgnited, as its flamethrower does).</summary>
        private static IEnumerator DoorBurn(string[] a, Result r)
        {
            var d = DoorArg(a, r); if (d == null) yield break;
            yield return Walk(d.transform.position, 200f);
            if (d.GetComponent<Burn>() == null)
            {
                var b = d.gameObject.AddComponent<Burn>();
                b.burnTime = 5f;
                if (Net.Session.Instance != null && Net.Session.Instance.IsClient) AsPlayer(() => FireSync.Instance?.ClientIgnited(d.gameObject));
            }
            yield return new WaitForSeconds(1f);
            r.Info = $"{d.name} {Ref(d)} burning={d.GetComponent<Burn>() != null} health {d.health}";
            if (d.GetComponent<Burn>() == null) r.Fail($"{r.Info}: not burning (refused?)");
            yield return Walk(Centre, 60f); // away from the fire
        }

        private static Window WindowArg(string[] a, Result r)
        {
            var w = a.Length > 0 ? Nth<Window>(a[0]) : null;
            if (w == null) r.Fail("usage: window-... N (no such window)");
            return w;
        }

        /// <summary>"window-barricade N": planks over it, the hammer's three strokes (Window.barricade(0, byPlayer)).</summary>
        private static IEnumerator WindowBarricade(string[] a, Result r)
        {
            var w = WindowArg(a, r); if (w == null) yield break;
            yield return Walk(w.transform.position);
            for (int i = 0; i < 3 && !w.barricaded; i++) { AsPlayer(() => w.barricade(0, byPlayer: true)); yield return new WaitForSeconds(0.3f); }
            yield return new WaitForSeconds(1f);
            r.Info = $"{w.name} {Ref(w)} barricaded={w.barricaded} health {w.barricadeHealth}";
            if (!w.barricaded) r.Fail($"{r.Info}: not barricaded");
        }

        /// <summary>"window-hit N DAMAGE": our player hits its barricade.</summary>
        private static IEnumerator WindowHit(string[] a, Result r)
        {
            var w = WindowArg(a, r); if (w == null) yield break;
            int dmg = a.Length > 1 ? int.Parse(a[1]) : 20;
            if (!w.barricaded) { r.Fail($"{w.name} {Ref(w)} isn't barricaded here"); yield break; }
            yield return Walk(w.transform.position);
            int was = w.barricadeHealth;
            AsPlayer(() => w.getHit(dmg, P.transform));
            yield return new WaitForSeconds(1f);
            r.Info = $"{w.name} {Ref(w)} barricaded={w.barricaded} health {was} -> {w.barricadeHealth}";
            if (w.barricaded && w.barricadeHealth == was) r.Fail($"{r.Info}: the hit did nothing");
        }

        /// <summary>"expect-burning door N | window N | NAME [SECS]": it catches fire here within SECS (default 4).</summary>
        private static IEnumerator ExpectBurning(string[] a, Result r)
        {
            Component c = a[0] == "door" ? Nth<Door>(a[1]) : a[0] == "window" ? (Component)Nth<Window>(a[1]) : Find<Item>(a[0]);
            int at = a[0] == "door" || a[0] == "window" ? 2 : 1;
            float secs = a.Length > at ? F(a[at]) : 4f;
            if (c == null) { r.Fail($"no {string.Join(" ", a)}"); yield break; }
            float until = Time.realtimeSinceStartup + secs;
            while (c != null && c.GetComponent<Burn>() == null && Time.realtimeSinceStartup < until) yield return null;
            r.Info = c == null ? "gone" : $"{c.name} {Ref(c)} burning={c.GetComponent<Burn>() != null}";
            if (c != null && c.GetComponent<Burn>() == null) r.Fail($"{r.Info}: didn't catch fire here");
        }

        // ---- containers -------------------------------------------------------------------------------------------

        internal static Inventory Container(string spec) =>
            Find<Inventory>(spec, i => i.invType == Inventory.InvType.itemInv && !i.isWorkbench && i != P.Inventory && i != P.Hotbar && !i.transform.IsChildOf(P.transform));

        /// <summary>"put-item CONTAINER [TYPE] [AMOUNT]": put something we carry (of TYPE) into it, as if its window were open.</summary>
        private static IEnumerator PutItem(string[] a, Result r)
        {
            var inv = Container(a[0]);
            if (inv == null) { r.Fail($"no container {a[0]}"); yield break; }
            var item = Carried().FirstOrDefault(i => a.Length < 2 || i.type == a[1]);
            if (item == null) { r.Fail($"not carrying {(a.Length > 1 ? a[1] : "anything")}"); yield break; }
            string type = item.type;
            int amount = Math.Min(a.Length > 2 ? int.Parse(a[2]) : 1, item.amount);
            yield return Walk(inv.transform.position);
            P.openedItemInventory = inv;
            yield return new WaitForSeconds(0.8f); // a client asks what it holds first
            int before = inv.getAllItems().Where(i => i.type == type).Sum(i => i.amount);
            AsPlayer(() => { P.Inventory.removeItemAmountFromPlayer(type, amount); inv.addItemType(type, amount); });
            yield return new WaitForSeconds(1f);
            P.openedItemInventory = null;
            yield return new WaitForSeconds(1f);
            int after = inv.getAllItems().Where(i => i.type == type).Sum(i => i.amount);
            r.Info = $"{type} x{amount} into {inv.name} {Ref(inv)}: [{Contents(inv)}]";
            if (after <= before) r.Fail($"{r.Info}: it isn't in there (refused?)");
        }

        /// <summary>"take-from CONTAINER [--refused]": take its first item. With --refused, the host should say no and
        /// the item go back.</summary>
        private static IEnumerator TakeFrom(string[] a, Result r)
        {
            bool refused = a.Contains("--refused");
            a = a.Where(x => x != "--refused").ToArray();
            var inv = Container(a[0]);
            if (inv == null) { r.Fail($"no container {a[0]}"); yield break; }
            yield return Walk(inv.transform.position);
            P.openedItemInventory = inv;
            yield return new WaitForSeconds(0.8f);
            var slot = inv.slots.FirstOrDefault(s => s != null && !InvItemClass.isNull(s.invItem));
            if (slot == null) { P.openedItemInventory = null; r.Fail($"{inv.name} {Ref(inv)} is empty here"); yield break; }
            var item = new InvItemClass(slot.invItem);
            int before = CarriedOf(item.type);
            AsPlayer(() => { slot.removeItem(); P.Inventory.addItemTypeToPlayer(item.type, item.amount, dropIfNoRoom: true); });
            yield return new WaitForSeconds(1.5f);
            P.openedItemInventory = null;
            yield return new WaitForSeconds(1f);
            int now = CarriedOf(item.type);
            r.Info = $"{item.type} x{item.amount} from {inv.name} {Ref(inv)}: we carry {before} -> {now}; it holds [{Contents(inv)}]";
            if (refused && now > before) r.Fail($"{r.Info}: the host let us take it");
            if (!refused && now <= before) r.Fail($"{r.Info}: not in our bags");
        }

        private static Inventory _held;

        /// <summary>"hold-open CONTAINER": open it and keep it open (until "release").</summary>
        private static IEnumerator HoldOpen(string[] a, Result r)
        {
            var inv = Container(a[0]);
            if (inv == null) { r.Fail($"no container {a[0]}"); yield break; }
            yield return Walk(inv.transform.position);
            P.openedItemInventory = inv;
            _held = inv;
            yield return new WaitForSeconds(1f);
            r.Info = $"holding {inv.name} {Ref(inv)} open";
        }

        private static IEnumerator Release(string[] a, Result r)
        {
            if (P.openedItemInventory == _held) P.openedItemInventory = null;
            r.Info = _held != null ? $"closed {_held.name}" : "nothing held";
            _held = null;
            yield return new WaitForSeconds(0.5f);
        }

        /// <summary>"open-busy CONTAINER": open it while the other player has it: we should be told "X is using this".</summary>
        private static IEnumerator OpenBusy(string[] a, Result r)
        {
            var inv = Container(a[0]);
            if (inv == null) { r.Fail($"no container {a[0]}"); yield break; }
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
            r.Info = $"{inv.name} {Ref(inv)}: {msg ?? "no message"}";
            if (msg == null) r.Fail($"{r.Info}: we weren't told someone is using it");
        }

        /// <summary>"pick-crate NAME": open a locked crate as the player does. One that's picked with the progress bar
        /// (Item.disarmTime, like the wooden loot crate): the bar's end, Player.progressBarCompleted with the crate as
        /// the trap being disarmed; the roll is the host's, so it's tried until it opens. One with a lock (Locked,
        /// like crate_locked_difficulty1): a lockpick (Locked.tryToLockpick).</summary>
        private static IEnumerator PickCrate(string[] a, Result r)
        {
            var crate = Find<Item>(a[0], i => WorldActions.Crate(i) != null && !i.searched || i.GetComponent<Locked>() is Locked lk && lk.locked);
            if (crate == null) { r.Fail($"no locked crate {a[0]}"); yield break; }
            yield return Walk(crate.transform.position);
            var l = crate.GetComponent<Locked>();
            int tries = 0;
            if (WorldActions.Crate(crate) != null && !crate.searched)
            {
                var beingDisarmed = AccessTools.FieldRefAccess<Player, Item>("trapBeingDisarmed");
                while (!crate.searched && tries < 12)
                {
                    tries++;
                    AsPlayer(() =>
                    {
                        P.disarmingTrap = true;
                        beingDisarmed(P) = crate;
                        P.progressBarCompleted();
                    });
                    yield return new WaitForSeconds(1.2f);
                }
                P.openedItemInventory = null;
                yield return new WaitForSeconds(1f);
                r.Info = $"{crate.name} {Ref(crate)} picked: searched={crate.searched} after {tries} tries (chance {crate.disarmChance:0.00})";
                if (!crate.searched) r.Fail($"{r.Info}: still shut");
            }
            else
            {
                if (CarriedOf("lockpick") == 0) P.Inventory.addItemTypeToPlayer("lockpick", 1, dropIfNoRoom: true);
                yield return null;
                AsPlayer(() => l.tryToLockpick());
                yield return new WaitForSeconds(1.5f); // a refusal would lock it again
                r.Info = $"{crate.name} {Ref(crate)} lockpicked: locked={l.locked}";
                if (l.locked) r.Fail($"{r.Info}: still locked");
            }
        }

        /// <summary>"pickup TYPE": pick up a dropped item of TYPE with a click (Item.getDroppedItem).</summary>
        private static IEnumerator Pickup(string[] a, Result r)
        {
            var it = Find<Item>("DroppedItem", i => i.GetComponent<Inventory>() is Inventory inv && inv.slots.Count > 0 && !InvItemClass.isNull(inv.slots[0].invItem)
                && inv.slots[0].invItem.type == a[0]);
            if (it == null) { r.Fail($"no dropped {a[0]} here"); yield break; }
            yield return Walk(it.transform.position);
            string what = $"{a[0]} {Ref(it)} at {Fmt(it.transform.position)}";
            int before = CarriedOf(a[0]);
            AsPlayer(() => it.getDroppedItem());
            yield return new WaitForSeconds(1.5f);
            r.Info = $"{what}: we carry {before} -> {CarriedOf(a[0])}, it's {(it == null ? "gone" : "still there")}";
            if (it != null || CarriedOf(a[0]) <= before) r.Fail($"{r.Info}: not picked up");
        }

        // ---- things in the world -----------------------------------------------------------------------------------

        /// <summary>"drag NAME DX DZ": take hold of it (Player.itemBeingDragged, as Item.startDragging sets) and move it.</summary>
        private static IEnumerator Drag(string[] a, Result r)
        {
            var it = Find<Item>(a[0], i => i.draggable);
            if (it == null) { r.Fail($"nothing draggable called {a[0]}"); yield break; }
            var by = new Vector3(F(a[1]), 0f, F(a[2]));
            yield return Walk(it.transform.position, 80f);
            var start = it.transform.position;
            P.itemBeingDragged = it;
            for (float t = 0f; t < 1f; t += Time.deltaTime)
            {
                var to = start + by * Mathf.Min(1f, t);
                it.transform.position = to;
                var rb = it.GetComponent<Rigidbody>(); if (rb != null) rb.position = to;
                P.transform.position = new Vector3(to.x, P.transform.position.y, to.z) - by.normalized * 70f;
                yield return null;
            }
            it.transform.position = start + by;
            yield return new WaitForSeconds(0.3f);
            P.itemBeingDragged = null;
            yield return new WaitForSeconds(2.5f); // where it came to rest goes out once settled
            r.Info = $"{it.name} {Ref(it)} from {Fmt(start)} to {Fmt(it.transform.position)}";
            if (StateDump.Flat(it.transform.position - start) < StateDump.Flat(by) * 0.5f) r.Fail($"{r.Info}: it didn't move");
        }

        /// <summary>"fuel NAME AMOUNT": pour gasoline into a generator or the saw, a unit at a time, as the player does.</summary>
        private static IEnumerator Fuel(string[] a, Result r)
        {
            float amount = a.Length > 1 ? F(a[1]) : 10f;
            var g = Find<Generator>(a[0]);
            var s = g == null ? Find<Saw>(a[0]) : null;
            if (g == null && s == null) { r.Fail($"no generator or saw {a[0]}"); yield break; }
            Component c = (Component)g ?? s;
            yield return Walk(c.transform.position);
            float before = g != null ? g.fuel : s.fuel;
            for (int i = 0; i < amount; i++)
            {
                AsPlayer(() => { if (g != null) g.addFuel(1f); else s.addFuel(1f); });
                yield return new WaitForSeconds(0.05f);
            }
            yield return new WaitForSeconds(1.5f); // a refused pour is set back
            float after = g != null ? g.fuel : s.fuel;
            float max = g != null ? g.maxFuel : s.maxFuel;
            r.Info = $"{c.name} {Ref(c)} fuel {before:0.0} -> {after:0.0} (max {max:0})";
            if (after <= before + amount * 0.5f && before < max - 1f) r.Fail($"{r.Info}: the fuel didn't go in");
        }

        /// <summary>"switch NAME on|off": switch a lamp, generator, radio... (Item.turnOn / turnOff). A generator's
        /// lamps must follow.</summary>
        private static IEnumerator Switch(string[] a, Result r)
        {
            var it = Find<Item>(a[0], i => i.switchable);
            if (it == null) { r.Fail($"nothing switchable called {a[0]}"); yield break; }
            bool on = a.Length < 2 || a[1] == "on";
            yield return Walk(it.transform.position);
            AsPlayer(() => { if (on) it.turnOn(); else it.turnOff(); });
            yield return new WaitForSeconds(1.5f);
            var gen = it.GetComponent<Generator>();
            string lamps = gen != null ? " lamps " + string.Join(",", gen.powerItems.Where(l => l != null).Select(l => $"{l.name}={l.isOn}")) : "";
            r.Info = $"{it.name} {Ref(it)} on={it.isOn}{lamps}";
            if (it.isOn != on) r.Fail($"{r.Info}: didn't switch");
        }

        /// <summary>"use NAME": what a player does with it: an InteractiveItem's switch, an Item's switch, or building
        /// it (Constructible.construct).</summary>
        private static IEnumerator Use(string[] a, Result r)
        {
            var t = Find<Transform>(a[0], x => x.GetComponentInChildren<Item>() != null || x.GetComponentInChildren<InteractiveItem>() != null || x.GetComponentInChildren<Constructible>() != null);
            if (t == null) { r.Fail($"nothing usable called {a[0]}"); yield break; }
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
                if (ii.isOn == was) r.Fail($"{r.Info}: didn't switch");
            }
            else if (con != null && !con.constructed)
            {
                AsPlayer(() => con.construct(false, 0));
                yield return new WaitForSeconds(1f);
                bool built = con == null || con.constructed; // it removes itself once built, unless it's kept
                r.Info = $"{t.name} {Ref(t)} built={built}";
                if (!built) r.Fail($"{r.Info}: not built");
            }
            else if (it != null && it.switchable)
            {
                bool was = it.isOn;
                AsPlayer(() => { if (was) it.turnOff(); else it.turnOn(); });
                yield return new WaitForSeconds(1f);
                r.Info = $"{it.name} {Ref(it)} on {was} -> {it.isOn}";
                if (it.isOn == was) r.Fail($"{r.Info}: didn't switch");
            }
            else r.Fail($"{t.name} {Ref(t)}: nothing to do with it (item {it?.name ?? "-"}, constructed {con?.constructed.ToString() ?? "-"})");
        }

        /// <summary>"hit NAME DAMAGE": our player hits a piece of furniture (Item.getHit).</summary>
        private static IEnumerator Hit(string[] a, Result r)
        {
            // "*": the nearest piece of plain furniture (no container, trap or explosive in it).
            var it = a[0] == "*"
                ? Nearest(Object.FindObjectsOfType<Item>().Where(i => i.destructible && !i.destroyed && i.health > 1 && !i.hasInventory && i.GetComponent<Explodes>() == null
                    && i.GetComponent<Trigger>() == null && i.GetComponent<Character>() == null && ObjRef.Of(i.gameObject).Valid), Centre)
                : Find<Item>(a[0], i => i.destructible && !i.destroyed);
            if (it == null) { r.Fail($"nothing breakable called {a[0]}"); yield break; }
            int dmg = a.Length > 1 ? int.Parse(a[1]) : 10;
            yield return Walk(it.transform.position);
            int was = it.health;
            AsPlayer(() => it.getHit(dmg, P.transform));
            yield return new WaitForSeconds(1f);
            r.Info = it == null ? $"{a[0]} gone" : $"{it.name} {Ref(it)} health {was} -> {it.health} broken={it.destroyed}";
            if (it != null && it.health == was && !it.destroyed) r.Fail($"{r.Info}: the hit did nothing");
        }

        private static Trigger TrapNamed(string spec) => Find<Trigger>(spec);

        /// <summary>"trap-fire NAME": our player steps into a trap (its trigger with our player's collider).</summary>
        private static IEnumerator TrapFire(string[] a, Result r)
        {
            var t = Find<Trigger>(a[0], x => x.active && !x.triggered);
            if (t == null) { r.Fail($"no armed trap {a[0]}"); yield break; }
            yield return Walk(t.transform.position);
            var col = P.GetComponent<Collider>() ?? P.GetComponentInChildren<Collider>();
            AsPlayer(() => AccessTools.Method(typeof(Trigger), "OnAfterTrigger").Invoke(t, new object[] { col, false }));
            yield return new WaitForSeconds(1f);
            r.Info = t == null ? $"{a[0]} gone" : $"{t.name} {Ref(t)} triggered={t.triggered} active={t.active}";
            if (t != null && !t.triggered) r.Fail($"{r.Info}: didn't go off");
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
            if (t == null) { r.Fail($"no armed trap {a[0]}"); yield break; }
            yield return Walk(t.transform.position + new Vector3(40f, 0f, 0f));
            var col = P.GetComponent<Collider>() ?? P.GetComponentInChildren<Collider>();
            float health = P.health;
            string name = t.name, at = Ref(t);
            int rolledBack = Authority.RolledBack;
            AsPlayer(() => t.checkCollision(col));
            bool fired = t == null || t.triggered;
            for (float until = Time.time + 6f; Time.time < until && Authority.Waiting > 0;) yield return null;
            yield return new WaitForSeconds(1.5f);
            r.Info = $"{name} {at} went off={fired}, health {health:0} -> {P.health:0}, inBearTrap={P.inBearTrap}, rolled back {Authority.RolledBack - rolledBack}";
            if (!fired) { r.Fail($"{r.Info}: didn't go off"); yield break; }
            if (a.Length > 1 && a[1] == "refused")
            {
                if (Authority.RolledBack == rolledBack) r.Fail($"{r.Info}: the host didn't refuse it");
                else if (P.inBearTrap) r.Fail($"{r.Info}: still held by a trap the host refused");
                else if (P.health < health - 0.5f) r.Fail($"{r.Info}: the refused trap's bite wasn't given back");
            }
            if (P.inBearTrap) P.stopAllPerformingActionAnims();
            // Well off the spot: back at the centre (60 from it) our player set off the next trap placed there.
            yield return Walk(Centre + new Vector3(0f, 0f, 250f), 60f);
        }

        /// <summary>"trap-refuse race|gone|off" (host): refuse the client's next TrapFired requests (TrapFiredPatch.TestRefuse).</summary>
        private static IEnumerator TrapRefuse(string[] a, Result r)
        {
            TrapFiredPatch.TestRefuse = a[0] == "off" ? null : a[0];
            r.Info = $"TrapFired requests: {(TrapFiredPatch.TestRefuse ?? "as usual")}";
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
            if (r.Failure != null) yield break;
            yield return new WaitForSeconds(2f);
            r.Info = go == null ? "none" : $"{go.name} {ObjRef.Of(go)} at {Fmt(go.transform.position)}";
            if (go == null) r.Fail($"{a[0]}: none placed and shared");
            yield return Walk(Centre + new Vector3(0f, 0f, 250f), 60f); // off it
        }

        /// <summary>"place-furniture DX DZ": place a piece of furniture that can be broken there (centre + offset), as
        /// a placed trap is placed (shared as ObjSpawn): the first of a few prefabs that is breakable.</summary>
        private static IEnumerator PlaceFurniture(string[] a, Result r)
        {
            var at = Centre + new Vector3(F(a[0]), 0f, F(a[1]));
            at.y = P.transform.position.y;
            yield return Walk(at, 150f);
            foreach (var prefab in new[] { "Furnitures/Cottage/chair_1", "Furnitures/Cottage/table_1", "Furnitures/Cottage/shelf" })
            {
                GameObject made;
                Containers.Placing = true;
                try { made = Core.AddPrefab(prefab, at, Quaternion.Euler(90f, 0f, 0f), Core.ItemContainer, worldSpace: true); }
                finally { Containers.Placing = false; }
                yield return null; yield return null; // its health is set in Start
                var it = made != null ? made.GetComponent<Item>() : null;
                if (it != null && it.destructible && !it.destroyed && it.health > 1 && !it.hasInventory)
                {
                    yield return new WaitForSeconds(2f);
                    r.Info = $"{prefab} {ObjRef.Of(made)} at {Fmt(made.transform.position)}, health {it.health}";
                    yield break;
                }
                if (made != null) Object.Destroy(made);
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
            if (made != null) Core.addToSaveable(made, isDynamic: true);
            yield return new WaitForSeconds(3f); // its Start, then the host's id and the client's tie
            _worldMade = made != null ? made.GetComponent<Item>() : null;
            if (_worldMade == null) { r.Fail("no chair made"); yield break; }
            _worldMadeHealth = _worldMade.health;
            r.Info = $"{made.name} {ObjRef.Of(made)} at {Fmt(made.transform.position)}, health {_worldMade.health}, ids given {HostIds.Given}, tied {HostIds.Tied}";
        }

        /// <summary>"ids-tied [hit]" (both): the chair world-furniture made is named by the host's id (a Spawned ref that
        /// finds it here); with "hit", the host's hit on it came here too.</summary>
        private static IEnumerator IdsTied(string[] a, Result r)
        {
            if (_worldMade == null) { r.Fail("world-furniture made nothing here"); yield break; }
            bool hit = a.Length > 0 && a[0] == "hit";
            float until = Time.realtimeSinceStartup + 5f;
            while (hit && _worldMade != null && _worldMade.health >= _worldMadeHealth && !_worldMade.destroyed && Time.realtimeSinceStartup < until)
                yield return new WaitForSeconds(0.25f);
            if (_worldMade == null) { r.Info = "gone"; if (!hit) r.Fail("the chair is gone"); yield break; }
            var go = _worldMade.gameObject;
            var at = ObjRef.Of(go);
            r.Info = $"{go.name} {at} tied {ObjRef.IsShared(go)}, health {_worldMadeHealth} -> {_worldMade.health} broken={_worldMade.destroyed}";
            if (at.K != ObjRef.Kind.Spawned || !ObjRef.IsShared(go) || ObjRef.Resolve<Item>(at) != _worldMade) r.Fail($"{r.Info}: not named by a host id");
            else if (hit && _worldMade.health >= _worldMadeHealth && !_worldMade.destroyed) r.Fail($"{r.Info}: the host's hit didn't come here");
        }

        /// <summary>"ids-hit" (host): our player hits the chair world-furniture made.</summary>
        private static IEnumerator IdsHit(string[] a, Result r)
        {
            if (_worldMade == null) { r.Fail("world-furniture made nothing here"); yield break; }
            yield return Walk(_worldMade.transform.position);
            AsPlayer(() => _worldMade.getHit(10, P.transform));
            yield return new WaitForSeconds(1f);
            r.Info = _worldMade == null ? "gone" : $"health {_worldMadeHealth} -> {_worldMade.health} {ObjRef.Of(_worldMade.gameObject)}";
        }

        /// <summary>"trap-disarm NAME": crouch over it and disarm it (the progress bar's end; a client's roll is the
        /// host's). It's either disarmed (gone, into our bags) or sprung.</summary>
        private static IEnumerator TrapDisarm(string[] a, Result r)
        {
            var t = Find<Trigger>(a[0], x => x.active && !x.triggered && x.canDisarm && x.GetComponent<Item>() != null);
            if (t == null) { r.Fail($"no armed trap {a[0]}"); yield break; }
            var item = t.GetComponent<Item>();
            string what = $"{t.name} {Ref(t)} at {Fmt(t.transform.position)}";
            yield return Walk(t.transform.position, 90f);
            var beingDisarmed = AccessTools.FieldRefAccess<Player, Item>("trapBeingDisarmed");
            AsPlayer(() =>
            {
                P.disarmingTrap = true;
                beingDisarmed(P) = item;
                P.progressBarCompleted();
            });
            float until = Time.realtimeSinceStartup + 4f;
            while (t != null && !t.triggered && Time.realtimeSinceStartup < until) yield return null;
            P.hidingInventory = false;
            yield return new WaitForSeconds(0.5f);
            r.Info = $"{what}: {(t == null ? "disarmed (gone)" : t.triggered ? "sprung (the roll failed)" : "still armed")}";
            if (t != null && !t.triggered) r.Fail($"{r.Info}: nothing happened");
            yield return Walk(Centre, 60f);
        }

        /// <summary>"explode NAME": shoot an exploding barrel or gas bottle from out of its reach (Explodes.onActivate).</summary>
        private static IEnumerator Explode(string[] a, Result r)
        {
            var e = Find<Explodes>(a[0], x => !BreakSync.Activated(x));
            if (e == null) { r.Fail($"nothing to explode called {a[0]}"); yield break; }
            yield return Walk(AwayFrom(e.transform.position, Mathf.Max(e.radius + 250f, 500f)), 40f);
            string what = $"{e.name} {Ref(e)} at {Fmt(e.transform.position)}";
            AsPlayer(() => e.onActivate());
            yield return new WaitForSeconds(2f);
            r.Info = $"{what}: {(e == null ? "gone" : "activated=" + BreakSync.Activated(e))}";
            if (e != null && !BreakSync.Activated(e)) r.Fail($"{r.Info}: didn't go off");
            yield return Walk(Centre, 60f);
        }

        /// <summary>"ignite NAME": set it alight (a Burn, as fire does); an explosive that goes off on fire goes off.</summary>
        private static IEnumerator Ignite(string[] a, Result r)
        {
            var it = Find<Item>(a[0]);
            if (it == null) { r.Fail($"nothing called {a[0]}"); yield break; }
            var e = it.GetComponent<Explodes>();
            bool explosive = e != null;
            yield return Walk(AwayFrom(it.transform.position, explosive ? Mathf.Max(e.radius + 250f, 500f) : 200f), 40f);
            string what = $"{it.name} {Ref(it)} at {Fmt(it.transform.position)}";
            if (it.GetComponent<Burn>() == null) it.gameObject.AddComponent<Burn>().burnTime = 5f;
            yield return new WaitForSeconds(2f);
            bool exploded = explosive && (e == null || BreakSync.Activated(e));
            r.Info = $"{what}: {(it == null ? "gone" : $"burning={it.GetComponent<Burn>() != null}")}{(explosive ? $" exploded={exploded}" : "")}";
            if (explosive && !exploded) r.Fail($"{r.Info}: lit, but it didn't go off");
            else if (!explosive && it != null && it.GetComponent<Burn>() == null) r.Fail($"{r.Info}: not burning");
            yield return Walk(Centre, 60f);
        }

        /// <summary>"throw-molotov NAME": throw a molotov at it from 300 away, as the player's throw does (ThrowSync);
        /// something there must catch fire.</summary>
        private static IEnumerator ThrowMolotov(string[] a, Result r)
        {
            var target = Find<Transform>(a[0], x => x.GetComponent<Item>() != null);
            if (target == null) { r.Fail($"no {a[0]}"); yield break; }
            var tpos = target.position;
            yield return Walk(ThrowSpot(tpos, target, 380f), 30f);
            var db = Singleton<ItemsDatabase>.Instance;
            var mi = db.getItem("molotov", instantiate: false);
            if (mi == null || mi.item == null) { r.Fail("no molotov in the item database"); yield break; }
            var from = P.transform.position;
            var dir = new Vector3(tpos.x - from.x, 0f, tpos.z - from.z);
            float dist = dir.magnitude; dir.Normalize();
            // Made inside our player's collider, it hit our player at once and broke there, setting the thrower
            // alight (Player.throwItem's held item is the player's child until thrown). Make it a step ahead, and
            // let it pass through our player.
            from += dir * 50f; dist -= 50f;
            var go = Core.AddPrefab(mi.item, from, Quaternion.Euler(90f, 0f, 0f), null);
            var ti = go.GetComponent<ThrownItem>();
            var rb = go.GetComponent<Rigidbody>();
            if (ti == null || rb == null) { r.Fail("a molotov isn't a thrown item"); yield break; }
            foreach (var mc in go.GetComponentsInChildren<Collider>())
                foreach (var pc in P.GetComponentsInChildren<Collider>()) Physics.IgnoreCollision(mc, pc);
            ti.landTarget = new Vector3(tpos.x, from.y, tpos.z);
            rb.drag = 2f;
            rb.velocity = dir * dist * 2.5f;
            ti.thrown = true;
            ti.objectThatSpawnedMe = P.transform;
            ti.setFallSpeed(dist);
            var ren = go.GetComponent<Renderer>(); if (ren != null) ren.enabled = true;
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
            r.Info = lit != null ? $"{lit.name} {Ref(lit)} caught fire" : $"nothing caught fire near {a[0]}";
            if (lit == null) r.Fail(r.Info);
            yield return Walk(Centre, 60f);
        }

        // ---- the story, traders, enemies ---------------------------------------------------------------------------

        private static Dictionary<string, Flags.Flag> FlagsDict() =>
            AccessTools.FieldRefAccess<Flags, Dictionary<string, Flags.Flag>>("flagsDict")(Singleton<Flags>.Instance);

        /// <summary>"flag N": flip the Nth story flag (Flags.setFlag).</summary>
        private static IEnumerator Flag(string[] a, Result r)
        {
            var flag = FlagsDict().Values.Where(f => f != null).OrderBy(f => f.name, StringComparer.Ordinal).Skip(a.Length > 0 ? int.Parse(a[0]) : 0).FirstOrDefault();
            if (flag == null) { r.Fail("no such flag"); yield break; }
            bool to = !flag.isTrue;
            Singleton<Flags>.Instance.setFlag(flag.name, to);
            yield return new WaitForSeconds(0.5f);
            r.Info = $"{flag.name} -> {flag.isTrue}";
            if (flag.isTrue != to) r.Fail($"{r.Info}: didn't change");
        }

        /// <summary>"reputation N DELTA": change the Nth NPC's reputation.</summary>
        private static IEnumerator Reputation(string[] a, Result r)
        {
            var npc = Singleton<Flags>.Instance.npcStates.Where(n => n != null).OrderBy(n => n.name, StringComparer.Ordinal).Skip(int.Parse(a[0])).FirstOrDefault();
            if (npc == null) { r.Fail("no such NPC"); yield break; }
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
            if (jdb != null && jdb.journalEntriesDict.Count == 0) jdb.populateDict();
            if (jdb == null) { r.Fail("no journal database"); yield break; }
            if (a.Length > 0 && a[0] == "note")
            {
                string note = jdb.notesDict.Keys.OrderBy(k => k, StringComparer.Ordinal).FirstOrDefault(k => !journal.notesDict.ContainsKey(k));
                if (note == null) { r.Fail("every note is in already"); yield break; }
                journal.addNote(new Journal.Note { type = note });
                r.Info = $"note {note}";
                if (!journal.notesDict.ContainsKey(note)) r.Fail($"{r.Info}: not added");
            }
            else
            {
                string entry = jdb.journalEntriesDict.Keys.OrderBy(k => k, StringComparer.Ordinal).FirstOrDefault(k => !journal.journalEntriesDict.ContainsKey(k));
                if (entry == null) { r.Fail("every entry is in already"); yield break; }
                journal.addJournalEntry(entry);
                r.Info = $"entry {entry}";
                if (!journal.journalEntriesDict.ContainsKey(entry)) r.Fail($"{r.Info}: not added");
            }
            yield return new WaitForSeconds(0.5f);
        }

        internal static NPC Trader() => Nearest(Object.FindObjectsOfType<NPC>().Where(n => n.trader && n.inventory != null), Centre);

        /// <summary>"trader-restock": the trader stocks up, as on a new day (the host's to do).</summary>
        private static IEnumerator TraderRestock(string[] a, Result r)
        {
            var npc = Trader();
            if (npc == null) { r.Fail("no trader here"); yield break; }
            string was = Contents(npc.inventory);
            AccessTools.Method(typeof(NPC), "randomizeTraderInv").Invoke(npc, null);
            yield return new WaitForSeconds(1f);
            r.Info = $"{npc.name}: [{was}] -> [{Contents(npc.inventory)}]";
        }

        /// <summary>"trade": with the trade window open, give the trader one thing from our bags worth at least his
        /// first thing and take that (as DialogueWindow.acceptTrade moves them), the difference in value onto the
        /// reputation as acceptTrade does. A client's trade goes to the host, which checks it against his stock; a
        /// refusal (rolled back here) fails the step. The dumps then compare his stock in both games.
        /// "trade --race AT": the same, at epoch second AT (players trading with him at the same moment); a refusal
        /// doesn't fail it, the reply says "got ..." or "refused ...".</summary>
        private static IEnumerator Trade(string[] a, Result r)
        {
            int ri = Array.IndexOf(a, "--race");
            double raceAt = ri >= 0 && a.Length > ri + 1 ? double.Parse(a[ri + 1], CultureInfo.InvariantCulture) : 0;
            var npc = Trader();
            if (npc == null) { r.Fail("no trader here"); yield break; }
            yield return Walk(npc.transform.position, 150f);
            // His cheapest thing, paid for with things from our bags (not the hotbar: later steps use what we hold),
            // the most valuable first, until they're worth as much.
            var slot = npc.inventory.slots.Where(s => s != null && !InvItemClass.isNull(s.invItem)).OrderBy(s => s.invItem.getSellValue()).FirstOrDefault();
            if (slot == null) { r.Fail($"{npc.name} has nothing"); yield break; }
            string boughtType = slot.invItem.type;
            int boughtValue = slot.invItem.getSellValue();
            var gave = new Dictionary<string, int>();
            int giveValue = 0;
            var ours = P.Inventory.getAllItems().Where(i => !InvItemClass.isNull(i) && !string.IsNullOrEmpty(i.type) && i.amount > 0 && i.type != boughtType && i.getSellValue() > 0).ToList();
            // One thing that pays for it, the cheapest such; else several, the most valuable first.
            var one = ours.Where(i => i.getSellValue() >= boughtValue).OrderBy(i => i.getSellValue()).FirstOrDefault();
            foreach (var it in one != null ? new List<InvItemClass> { one } : ours.OrderByDescending(i => i.getSellValue()).ToList())
                for (int n = 0; n < Math.Max(1, it.amount) && giveValue < boughtValue; n++)
                {
                    gave.TryGetValue(it.type, out int had);
                    gave[it.type] = had + 1;
                    giveValue += it.getSellValue();
                }
            int repChange = giveValue - boughtValue;
            string giveText = string.Join(",", gave.Select(kv => $"{kv.Key}x{kv.Value}"));
            if (npc.reputation + repChange < 0) { r.Fail($"our bags ({giveText}: {giveValue}) don't pay for {boughtType} ({boughtValue}) at reputation {npc.reputation}"); yield break; }
            P.inShop = true;
            P.talkedToNPC = npc;
            yield return new WaitForSeconds(0.8f);
            if (ri >= 0)
            {
                while (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0 < raceAt) yield return null;
                // What's cheapest may have changed meanwhile; the same slot as the others, as it is now.
                if (InvItemClass.isNull(slot.invItem) || slot.invItem.type != boughtType) { P.inShop = false; P.talkedToNPC = null; r.Fail($"{boughtType} gone from his slot before the race"); yield break; }
            }
            string was = Contents(npc.inventory);
            int mark = MapScenarioIssues.LogMark();
            AsPlayer(() =>
            {
                // His thing to us, ours to him (exchangeTrader.transferAllItemsToPlayer, exchangePlayer.transferAllItemsTo).
                if (slot.invItem.amount > 1) slot.invItem.amount -= 1; else slot.removeItem();
                P.Inventory.addItemTypeToPlayer(boughtType, 1, dropIfNoRoom: true);
                foreach (var kv in gave) { P.Inventory.removeItemAmountFromPlayer(kv.Key, kv.Value); npc.inventory.addItemType(kv.Key, kv.Value); }
                npc.reputation += repChange;
                // What TraderTradePatch does after acceptTrade.
                TraderSync.Instance?.LocalTrade(npc, new Dictionary<string, int> { [boughtType] = 1 }, gave, repChange);
            });
            yield return new WaitForSeconds(1.5f);
            P.inShop = false;
            P.talkedToNPC = null;
            yield return new WaitForSeconds(1f);
            string refused = MapScenarioIssues.LogLines().Skip(mark).FirstOrDefault(l => l.Contains("refused trade with"));
            r.Info = $"{npc.name}: bought {boughtType} ({boughtValue}), gave {giveText} ({giveValue}): [{was}] -> [{Contents(npc.inventory)}], reputation {npc.reputation}";
            if (ri >= 0) { r.Info = (refused != null ? "refused " : "got ") + r.Info; yield break; }
            if (refused != null) r.Fail($"{r.Info}: {refused.Trim()}");
            else if (Contents(npc.inventory) == was) r.Fail($"{r.Info}: his stock didn't change");
        }

        /// <summary>"spawn TYPE": the host spawns an enemy near its player (CharacterSpawner).</summary>
        private static IEnumerator Spawn(string[] a, Result r)
        {
            if (Net.Session.Instance != null && Net.Session.Instance.IsClient) { r.Fail("enemies are the host's"); yield break; }
            var c = TestWorld.SpawnEnemy(a.Length > 0 ? a[0] : "Dog");
            yield return new WaitForSeconds(1f);
            r.Info = c != null ? $"{c.name} {Ref(c)} at {Fmt(c.transform.position)}" : "none";
            if (c == null) r.Fail("nothing spawned");
        }

        /// <summary>"kill-enemy": our player hits the host's enemy nearest to us until it's dead (a client's hits go to
        /// the host, capped by its weapon).</summary>
        private static IEnumerator KillEnemy(string[] a, Result r)
        {
            var ne = Combat.NetEnemies.Instance;
            var c = Nearest(Object.FindObjectsOfType<Character>().Where(x => x.alive && Combat.NetEnemies.Eligible(x) && ne != null && ne.NetIdOf(x, out _)
                && StateDump.Flat(x.transform.position - Centre) < 1500f), P.transform.position);
            if (c == null) { r.Fail("no enemy of the host's here"); yield break; }
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
            r.Info = $"{what}: alive={(c != null && c.alive)} after {hits} hits, searchable={(c != null && c.GetComponent<Item>() != null)} at {(c != null ? Fmt(c.transform.position) : "-")}";
            if (c != null && c.alive) r.Fail($"{r.Info}: still alive");
            yield return Walk(Centre, 60f);
        }
    }
}
