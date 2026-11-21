using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DarkwoodMP.Sync.Players;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DarkwoodMP.World
{
    /// <summary>The starter steps: doors, items, health, time.</summary>
    public static partial class MapScenario
    {
        /// <summary>
        /// "toggle-door [open|close]": the door nearest the centre, opened or
        /// closed (toggled by default).
        /// </summary>
        private static IEnumerator ToggleDoor(string[] a, Result r)
        {
            var p = Player.Instance;
            var door = Nearest(
                Object.FindObjectsOfType<Door>().Where(
                    d => !d.destroyed && ObjRef.Of(d.gameObject).Valid),
                Centre);
            if (door == null)
            {
                r.Fail("no door");
                yield break;
            }

            yield return Walk(door.transform.position);
            bool open;
            if (a.Length > 0)
            {
                open = a[0] == "open";
            }
            else
            {
                open = !door.opened;
            }

            if (open != door.opened)
            {
                AsPlayer(() =>
                {
                    if (open)
                    {
                        door.open(p.transform.position, p.transform, 0f);
                    }
                    else
                    {
                        door.close(p.transform);
                    }
                });
            }

            yield return new WaitForSeconds(0.5f);
            r.Info =
                $"{door.name} {ObjRef.Of(door.gameObject)} open={door.opened}";
            if (door.opened != open)
            {
                r.Fail($"{door.name} is still open={door.opened}");
            }
        }

        /// <summary>"take-item [TYPE] [--locked]": the first item (of TYPE)
        /// from the unlocked container nearest the centre (or a locked one too,
        /// with --locked), into our bags.</summary>
        private static IEnumerator TakeItem(string[] a, Result r)
        {
            var p = Player.Instance;
            // a locked one too (the host should refuse it)
            bool locked = a.Contains("--locked");
            a = a.Where(x => x != "--locked").ToArray();
            string type = null;
            if (a.Length > 0)
            {
                type = a[0];
            }

            bool Has(InvSlot s) => !InvItemClass.isNull(s.invItem)
                && (type == null || s.invItem.type == type);
            var inv = Nearest(
                Object.FindObjectsOfType<Inventory>().Where(
                    i => i.invType == Inventory.InvType.itemInv
                        && !i.isWorkbench
                        && !i.name.StartsWith("DroppedItem") && i.slots.Any(Has)
                        && (locked
                            || !(i.GetComponentInParent<Locked>() is Locked l
                                && l.locked)) && i != p.Inventory
                        && i != p.Hotbar
                        && !i.transform.IsChildOf(p.transform)), Centre);
            if (inv == null)
            {
                string wanted = type;
                if (wanted == null)
                {
                    wanted = "anything";
                }

                r.Fail($"no container with {wanted} in it");
                yield break;
            }

            yield return Walk(inv.transform.position);
            var slot = inv.slots.First(Has);
            var item = new InvItemClass(slot.invItem);
            p.openedItemInventory = inv; // as if its window were open
            yield return new WaitForSeconds(0.6f);
            int Count() =>
                Carried(p).Where(i => i.type == item.type).Sum(i => i.amount);
            int before = Count();
            AsPlayer(() =>
            {
                slot.removeItem();
                p.Inventory.addItemTypeToPlayer(
                    item.type, item.amount, dropIfNoRoom: true);
            });

            if (Count() <= before)
            {
                r.Fail(
                    $"took {item.type} x{item.amount} from {inv.name} "
                        + $"{ObjRef.Of(inv.gameObject)} but it isn't in our "
                        + "bags (full?)");
            }

            yield return new WaitForSeconds(0.6f);
            p.openedItemInventory = null;
            yield return new WaitForSeconds(0.5f);
            _lastTaken = item.type;
            r.Info =
                $"{item.type} x{item.amount} from {inv.name} "
                    + $"{ObjRef.Of(inv.gameObject)}";
        }

        private static string _lastTaken;

        /// <summary>
        /// "drop-item [TYPE] [AMOUNT]": drop one of TYPE (the last taken by
        /// default) from our bags.
        /// </summary>
        private static IEnumerator DropItem(string[] a, Result r)
        {
            var p = Player.Instance;
            string type;
            if (a.Length > 0)
            {
                type = a[0];
            }
            else
            {
                type = _lastTaken;
            }

            int amount = 1;
            if (a.Length > 1)
            {
                amount = int.Parse(a[1]);
            }

            if (type == null)
            {
                r.Fail("nothing to drop (take-item first, or give a type)");
                yield break;
            }

            var carried = Carried(p).FirstOrDefault(i => i.type == type);
            if (carried == null)
            {
                r.Fail($"not carrying {type}");
                yield break;
            }

            var item = new InvItemClass(carried)
            {
                amount = Math.Min(amount, carried.amount)
            };

            Transform dropped = null;
            AsPlayer(() =>
            {
                p.Inventory.removeItemAmountFromPlayer(type, item.amount);
                dropped = p.spawnDroppedInvItem(item);
            });

            // dropped things are shared once they've come to rest
            yield return new WaitForSeconds(2.5f);
            string spot = "?";
            if (dropped != null)
            {
                spot = Fmt(dropped.position);
            }

            string id = "";
            if (dropped != null)
            {
                id = ObjRef.Of(dropped.gameObject).ToString();
            }

            r.Info = $"{type} x{amount} at {spot} {id}";
        }

        /// <summary>"heal": our player back to full health, flames put out,
        /// bleeding and other effects (infection's damage, poison) stopped, out
        /// of a bear trap. The scenario does this after steps that hurt, since
        /// a death changes the story flags and resets things the dumps
        /// compare.</summary>
        private static IEnumerator Heal(string[] a, Result r)
        {
            var p = Player.Instance;
            if (p == null)
            {
                r.Fail("no player");
                yield break;
            }

            if (!p.alive || p.health <= 0f)
            {
                r.Fail($"our player is dead (health {p.health:0})");
                yield break;
            }

            float was = p.health;
            HealNow();
            yield return new WaitForSeconds(0.3f);
            r.Info = $"health {was:0} -> {p.health:0} of {p.maxHealth:0}";
        }

        /// <summary>"alive": our player and every other player are alive, and
        /// nobody died in this game's session (the log's "X died" notices, and
        /// the host's "A death didn't ..." lines).</summary>
        private static IEnumerator Alive(string[] a, Result r)
        {
            var p = Player.Instance;
            float health = 0f;
            if (p != null)
            {
                health = p.health;
            }

            var parts = new List<string> { $"we: health {health:0}" };
            if (p == null || !p.alive || p.health <= 0f)
            {
                float playerHealth = 0f;
                if (p != null)
                {
                    playerHealth = p.health;
                }

                r.Fail($"our player is dead (health {playerHealth:0})");
            }

            var ps = Plugin.Instance.Host.Players;
            if (ps != null)
            {
                foreach (var rp in ps.Remotes.Values)
                {
                    if (rp.Owner == PlayerSync.DummyId)
                    {
                        continue;
                    }

                    var last = rp.Last;
                    string percent = "?";
                    if (last != null)
                    {
                        percent = last.Health.ToString();
                    }

                    parts.Add(
                        $"{rp.Name}: alive {rp.Alive}, health {percent}%");
                    if (!rp.Alive || (last != null && last.Health <= 0))
                    {
                        r.Fail($"{rp.Name} is dead");
                    }
                }
            }

            var lines = MapScenarioIssues.LogLines();
            int from = lines.FindLastIndex(l => l.Contains("World ready"));
            // (Not our own earlier "[map] alive" failures, which quote those
            // lines.)
            var deaths = lines.Skip(Math.Max(0, from))
                .Where(
                    l => !l.Contains("[map]")
                        && ((l.Contains("[notice]") && l.TrimEnd()
                                .EndsWith(" died"))
                            || l.Contains("A death didn't"))).ToList();
            if (deaths.Count > 0)
            {
                string list =
                    string.Join(" | ", deaths.Select(l => l.Trim()));
                r.Fail("deaths in the log: " + $"{list}");
            }

            r.Info = string.Join("; ", parts);
            if (r.Failure != null)
            {
                r.Failure += "; " + r.Info;
            }

            yield break;
        }

        /// <summary>
        /// "time +H": move the clock on H hours (the host's; everyone follows).
        /// </summary>
        private static IEnumerator Time_(string[] a, Result r)
        {
            var s = Net.Session.Instance;
            if (s != null && s.IsClient)
            {
                r.Fail("the clock is the host's");
                yield break;
            }

            var ctl = Singleton<Controller>.Instance;
            if (ctl == null || a.Length < 1)
            {
                r.Fail("usage: time +H");
                yield break;
            }

            float h = F(a[0].TrimStart('+'));
            TestWorld.SetTime(Mathf.Repeat(ctl.CurrentTime + h * 60f, 1440f));
            yield return new WaitForSeconds(1f);
            r.Info = $"day {ctl.day} {ctl.CurrentTime / 60f:0.00} h";
        }
    }
}
