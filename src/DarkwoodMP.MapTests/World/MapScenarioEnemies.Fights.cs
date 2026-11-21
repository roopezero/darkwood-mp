using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DarkwoodMP.Combat;
using HarmonyLib;
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
            var ne = NetEnemies.Instance;
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

        /// <summary>
        /// A melee weapon that can cut a character in half
        /// (InvItem.canCutInHalf), from the game's item list.
        /// </summary>
        private static string CutWeapon()
        {
            var db = Singleton<ItemsDatabase>.Instance;
            if (db == null)
            {
                return null;
            }

            foreach (var type in db.itemsDict.Keys.OrderBy(
                k => k, StringComparer.Ordinal))
            {
                InvItem it = null;
                try
                {
                    it = db.getItem(type, instantiate: false);
                }
                catch
                {
                }

                if (it != null && it.canCutInHalf && it.isMelee)
                {
                    return type;
                }
            }

            return null;
        }

        /// <summary>
        /// How long a fight lasts at most once the hitting starts (s).
        /// </summary>
        private const float FightTime = 25f;

        /// <summary>
        /// What one hit of the weapon in hand does at most: its damage with
        /// upgrades, doubled by the Strong skill (within
        /// WeaponRules.DamageCap).
        /// </summary>
        private static float HitDamage(Player p)
        {
            var it = p.currentItem;
            if (InvItemClass.isNull(it) || it.baseClass == null)
            {
                return 10f;
            }

            return Mathf.Max(
                10f,
                Mathf.Max(
                    it.getModdedDamage(it.baseClass.damage),
                    it.getModdedDamage(it.baseClass.specialDamage)) * 2f);
        }

        /// <summary>
        /// Take a weapon of this type out (made in an empty hotbar slot), as
        /// the game's hotbar select does.
        /// </summary>
        internal static IEnumerator TakeOut(Player p, string type, Result r)
        {
            for (int attempt = 0; attempt < 4; attempt++)
            {
                if (!InvItemClass.isNull(p.currentItem)
                    && p.currentItem.type == type && !p.switchingItem)
                {
                    yield break;
                }

                InvSlot slot = p.Hotbar.slots.FirstOrDefault(
                    s => !InvItemClass.isNull(s.invItem)
                        && s.invItem.type == type);
                if (slot == null)
                {
                    slot = p.Hotbar.slots.FirstOrDefault(
                        s => InvItemClass.isNull(s.invItem));
                }

                if (slot == null)
                {
                    slot = p.Hotbar.slots.FirstOrDefault(s => !s.selected);
                }

                if (slot == null)
                {
                    slot = p.Hotbar.slots[0];
                }

                slot.inventory = p.Hotbar;
                // A full hotbar (earlier steps left a torch and a flashlight in
                // it): make room in a slot that isn't in hand, or the selected
                // weapon was taken out again instead.
                if (!InvItemClass.isNull(slot.invItem)
                    && slot.invItem.type != type)
                {
                    slot.removeItem();
                }

                // A weapon wears out over the fights: a fresh one when it's
                // worn (a broken one can't be taken out).
                if (!InvItemClass.isNull(slot.invItem)
                    && slot.invItem.type == type
                    && slot.invItem.baseClass != null
                    && slot.invItem.baseClass.hasDurability
                    && slot.invItem.durability
                        < slot.invItem.baseClass.maxDurability * 0.5f)
                {
                    slot.invItem.durability =
                        slot.invItem.baseClass.maxDurability;
                }

                if (InvItemClass.isNull(slot.invItem))
                {
                    slot.createItem(type, 1);
                }

                foreach (var s in p.Hotbar.slots)
                {
                    s.deselect();
                }

                slot.select(noiseless: true);
                float t = Time.realtimeSinceStartup + 2.5f;
                while ((InvItemClass.isNull(p.currentItem)
                        || p.currentItem.type != type
                        || p.switchingItem) && Time.realtimeSinceStartup < t)
                {
                    yield return null;
                }
            }

            if (InvItemClass.isNull(p.currentItem)
                || p.currentItem.type != type)
            {
                string holding;
                if (InvItemClass.isNull(p.currentItem))
                {
                    holding = "nothing";
                }
                else
                {
                    holding = p.currentItem.type;
                }

                r.Fail($"couldn't take the {type} out (holding {holding}, "
                        + $"switching {p.switchingItem}; hotbar: " +
                       string.Join(", ", p.Hotbar.slots.Select(s =>
                       {
                           if (InvItemClass.isNull(s.invItem))
                           {
                               return "-";
                           }

                           string text = "";
                           if (s.selected)
                           {
                               text = " selected";
                           }

                           return $"{s.invItem.type} "
                               + $"({s.invItem.durability:0}){text}";
                       })) + ")");
            }
        }

        private static readonly System.Reflection.MethodInfo AttackMethod =
            AccessTools.Method(
                typeof(Player), "attack", new[] { typeof(float) });

        private static void Swing(Player p, Vector3 at)
        {
            var d = at - p.transform.position;
            d.y = 0f;
            if (d.sqrMagnitude > 1f)
            {
                p.transform.rotation = Quaternion.Euler(
                    90f, Quaternion.LookRotation(d).eulerAngles.y, 0f);
            }

            AsPlayer(() => AttackMethod.Invoke(p, new object[] { 1f }));
        }

        /// <summary>
        /// "ee-corpse" (client): search the body of what we fought, if the game
        /// makes it searchable; take what's in it.
        /// </summary>
        private static IEnumerator Corpse(string[] a, Result r)
        {
            var p = P;
            NetEnemies instance = NetEnemies.Instance;
            Character c = null;
            if (instance != null)
            {
                c = instance.CharacterOf(_fought);
            }

            if (c == null)
            {
                r.Info = $"#{_fought} is gone here (no corpse)";
                yield break;
            }

            if (NetEnemies.InPlay(c))
            {
                r.Fail($"{c.name} #{_fought} is still alive here");
                yield break;
            }

            // settled (CopyStateRules.DeathAnimTime)
            yield return new WaitForSeconds(2.5f);
            var item = c.GetComponent<Item>();
            var inv = c.GetComponentInChildren<Inventory>(true);
            if (item == null || inv == null)
            {
                r.Info =
                    $"{c.name} #{_fought}: not searchable (Item "
                        + $"{item != null}, Inventory {inv != null})";
                yield break;
            }

            yield return Walk(c.transform.position, 90f);
            int mark = MapScenarioIssues.LogMark();
            p.openedItemInventory = inv;
            yield return new WaitForSeconds(1.5f);
            var slot =
                inv.slots.FirstOrDefault(s => !InvItemClass.isNull(s.invItem));
            string took = "nothing in it";
            if (slot != null)
            {
                var it = new InvItemClass(slot.invItem);
                AsPlayer(() =>
                {
                    slot.removeItem();
                    p.Inventory.addItemTypeToPlayer(
                        it.type, it.amount, dropIfNoRoom: true);
                });

                took = $"took {it.type} x{it.amount}";
                yield return new WaitForSeconds(0.8f);
            }

            p.openedItemInventory = null;
            yield return new WaitForSeconds(1.5f);
            var refused = MapScenarioIssues.LogLines().Skip(Math.Max(0, mark))
                .FirstOrDefault(l => l.Contains("refused"));
            r.Info = $"{c.name} #{_fought}: {took} (children {Children(c)})";
            if (refused != null)
            {
                r.Fail($"{r.Info}; {refused}");
            }
        }
    }
}
