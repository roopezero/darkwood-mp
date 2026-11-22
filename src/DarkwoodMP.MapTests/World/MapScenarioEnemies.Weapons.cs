using System;
using System.Collections;
using System.Linq;
using DarkwoodMP.Combat;
using HarmonyLib;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;
using Object = UnityEngine.Object;

namespace DarkwoodMP.World
{
    /// The client's fight steps' weapons: taking one out, swinging it, and
    /// the corpse a cut leaves.
    public static partial class MapScenarioEnemies
    {
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
            NetEnemies instance = null;
            if (Coop.Current != null)
            {
                instance = Coop.Current.Enemies;
            }

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
