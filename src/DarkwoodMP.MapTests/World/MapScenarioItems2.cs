using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DarkwoodMP.Combat;
using HarmonyLib;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Steps of the two-game map test for items used through the game's own
    /// code: firearms fired and reloaded at a spawned dog (Player.attack,
    /// fireWeapon, reload), things thrown (Player.attack, throwItem), a lantern
    /// worn on the hotbar (its light around the other player), and map items
    /// read (the places on the others' maps). The order they run in is
    /// tools/tests/map_scenario_items2.py.
    ///
    /// The dogs are spawned by the host at fixed offsets from the centre
    /// (<see cref="Spots"/>) and found in the client's game as the host's enemy
    /// nearest that spot. Waits are in real time.
    /// </summary>
    public static partial class MapScenarioItems2
    {
        private static readonly Dictionary<string, Vector3> Spots =
            new Dictionary<string, Vector3>
            {
                // the dog things are thrown at
                ["meat"] = new Vector3(500f, 0f, 1300f),
                // the dog that's shot
                ["gun"] = new Vector3(-500f, 0f, 1300f),
                // open ground for a molotov
                ["fire"] = new Vector3(0f, 0f, -1300f),
                // away from the stations' lamps, for the lantern
                ["dark"] = new Vector3(-1300f, 0f, -600f),
            };

        private static Vector3 Spot(string which)
        {
            if (Spots.TryGetValue(which, out var off))
            {
                return Centre + off;
            }

            return Centre;
        }

        private static Player P => Player.Instance;
        private static string ItemName(InvItemClass i)
        {
            if (InvItemClass.isNull(i))
            {
                return "nothing";
            }

            return i.type;
        }

        private static readonly System.Reflection.MethodInfo AttackM =
            AccessTools.Method(
                typeof(Player), "attack", new[] { typeof(float) });
        private static readonly System.Reflection.MethodInfo ReloadM =
            AccessTools.Method(typeof(Player), "reload", Type.EmptyTypes);
        private static readonly
            AccessTools.FieldRef<Player, GameObject> HeldItem =
                AccessTools.FieldRefAccess<Player, GameObject>("heldItem");
        private static readonly
            AccessTools.FieldRef<Explodes, bool> ExplodesActivated =
                AccessTools.FieldRefAccess<Explodes, bool>("activated");

        /// <summary>
        /// Where the mouse points, as Core.mouseWorldPosition says (while a
        /// step aims at something; null: the real mouse).
        /// </summary>
        internal static Vector3? AimAt;
        internal static bool Recording;

        /// <summary>
        /// The sounds the host's characters heard lately
        /// (Character.heardSound): who, where, dangerous.
        /// </summary>
        internal static readonly
            List<(Character c, Vector3 pos, bool dangerous)> Heard =
                new List<(Character, Vector3, bool)>();

        /// <summary>
        /// "i2-safe on|off": our player is healed whenever it's below half
        /// health (MapScenarioSafePatch), so it can't die. Shot dogs bite back,
        /// a gas cloud drifts and a molotov's fire spreads: a test player dying
        /// changes the story flags the dumps compare, and nothing these steps
        /// check is about our player's health. (It isn't made invulnerable: a
        /// dog whose bites don't land keeps chasing its target and never goes
        /// for a lure.)
        /// </summary>
        internal static bool Safe;

        private static IEnumerator SafeStep(string[] a, Result r)
        {
            Safe = a.Length == 0 || a[0] != "off";
            if (Safe)
            {
                MapScenarioSafePatch.Postfix(P);
            }

            r.Info = $"safe {Safe}, health {P.health:0}";
            yield break;
        }

        public static void Register()
        {
            Add("i2-safe", SafeStep);
            Recording = true;
            Add("i2-dog", Dog);
            Add("i2-note", Note);
            Add("i2-fire", Fire, timeout: 90f);
            Add("i2-hurt", Hurt, timeout: 15f);
            Add("i2-shots-seen", ShotsSeen, timeout: 15f);
            Add("i2-throw", Throw, timeout: 40f);
            Add("i2-seen", Seen, timeout: 30f);
            Add("i2-heard", HeardStep, timeout: 15f);
            Add("i2-eats", Eats, timeout: 70f);
            Add("i2-gone", Gone, timeout: 30f);
            Add("i2-gassed", Gassed, timeout: 15f);
            Add("i2-kill", Kill, timeout: 20f);
            Add("i2-goto", (a, r) =>
            {
                string name = "";
                if (a.Length > 0)
                {
                    name = a[0];
                }

                return Walk(MapScenarioEnemies.FreeSpot(Spot(name)), 40f);
            }, timeout: 40f);

            Add("i2-lantern", Lantern, timeout: 15f);
            Add("i2-lit", Lit, timeout: 15f);
            Add("i2-map-pick", MapPick);
            Add("i2-map-use", MapUse);
            Add("i2-map-check", MapCheck, timeout: 20f);
        }

        // ---- helpers --------------------------------------------------------

        // host: by spot
        private static readonly Dictionary<string, Character> _dogs =
            new Dictionary<string, Character>();
        // client: by spot
        private static readonly Dictionary<string, uint> _dogIds =
            new Dictionary<string, uint>();

        /// <summary>
        /// The dog of that spot: the host's own, or the client's copy of it.
        /// </summary>
        private static Character DogOf(string name)
        {
            var s = Net.Session.Instance;
            var ne = NetEnemies.Instance;
            if (s == null || !s.IsClient)
            {
                if (_dogs.TryGetValue(name, out var d) && d != null)
                {
                    return d;
                }

                return null;
            }

            if (ne == null)
            {
                return null;
            }

            if (_dogIds.TryGetValue(name, out uint id))
            {
                var had = ne.PuppetOf(id);
                if (had != null && had.alive)
                {
                    return had;
                }

                // gone or dead: the host may have spawned another for this spot
                _dogIds.Remove(name);
            }

            var c = ne.PuppetCharacters().Where(
                x => x != null && x.name.StartsWith("Dog"))
                .OrderBy(x => StateDump.Flat(x.transform.position
                        - Spot(name))).FirstOrDefault();
            if (c != null
                && StateDump.Flat(c.transform.position - Spot(name)) < 600f
                && ne.NetIdOf(c, out id))
            {
                _dogIds[name] = id;
                return c;
            }

            return null;
        }

        private static IEnumerator WaitDog(string name, Action<Character> got)
        {
            float until = Time.realtimeSinceStartup + 6f;
            Character c = null;
            while ((c = DogOf(name)) == null
                && Time.realtimeSinceStartup < until)
            {
                yield return new WaitForSeconds(0.3f);
            }

            got(c);
        }

        private static void Face(Vector3 at)
        {
            var d = at - P.transform.position;
            d.y = 0f;
            if (d.sqrMagnitude > 1f)
            {
                P.transform.rotation = Quaternion.Euler(
                    90f, Quaternion.LookRotation(d).eulerAngles.y, 0f);
            }
        }

        /// <summary>
        /// A spot `dist` from `target` on the side of the centre (where we come
        /// from).
        /// </summary>
        private static Vector3 Stand(Vector3 target, float dist)
        {
            var d = Centre - target;
            d.y = 0f;
            if (d.sqrMagnitude < 1f)
            {
                d = Vector3.back;
            }

            d.Normalize();
            // The host to one side and the client to the other: standing on one
            // spot, each one's throws and shots hit the other player in the
            // way.
            var s = Net.Session.Instance;
            var across = new Vector3(-d.z, 0f, d.x);
            float sign = 1f;
            if (s != null && s.IsClient)
            {
                sign = -1f;
            }

            var side = across * sign;
            return MapScenarioEnemies.FreeSpot(
                target + (d + side).normalized * dist);
        }

        private static (InvSlot slot, InvItemClass was) _hotbarWas, _bagWas;

        /// <summary>
        /// TYPE in a hotbar slot (a free one, else the last, its item put back
        /// by Restore), selected and in our hands.
        /// </summary>
        private static IEnumerator TakeOut(
            string type, Result r, Action<InvItemClass> made = null)
        {
            var p = P;
            // A world bear trap on the way here holds our player and no item
            // comes out until it's freed (as HealNow frees it).
            if (p.inBearTrap)
            {
                p.stopAllPerformingActionAnims();
                yield return new WaitForSeconds(0.3f);
            }

            int free =
                p.Hotbar.slots.FindIndex(s => InvItemClass.isNull(s.invItem));
            if (free < 0)
            {
                free = p.Hotbar.slots.Count - 1;
            }

            var slot = p.Hotbar.slots[free];
            slot.inventory = p.Hotbar;
            if (!InvItemClass.isNull(slot.invItem))
            {
                _hotbarWas = (slot, new InvItemClass(slot.invItem));
                slot.removeItem();
            }

            slot.createItem(type, 1);
            if (made != null)
            {
                made(slot.invItem);
            }

            foreach (var s in p.Hotbar.slots)
            {
                s.deselect();
            }

            slot.select(noiseless: true);
            float until = Time.realtimeSinceStartup + 5f;
            while ((InvItemClass.isNull(p.currentItem)
                    || p.currentItem.type != type
                    || p.switchingItem) && Time.realtimeSinceStartup < until)
            {
                yield return null;
            }

            if (InvItemClass.isNull(p.currentItem)
                || p.currentItem.type != type)
            {
                r.Fail(
                    $"couldn't take the {type} out (holding "
                        + $"{ItemName(p.currentItem)})");
            }
        }

        /// <summary>
        /// N of TYPE in our bags (in a free slot, else the last, its item put
        /// back by Restore).
        /// </summary>
        private static void GiveBag(string type, int n)
        {
            var p = P;
            InvSlot slot = p.Inventory.slots.FirstOrDefault(
                s => InvItemClass.isNull(s.invItem));
            if (slot == null)
            {
                slot = p.Inventory.slots.Last();
            }

            slot.inventory = p.Inventory;
            if (!InvItemClass.isNull(slot.invItem))
            {
                _bagWas = (slot, new InvItemClass(slot.invItem));
                slot.removeItem();
            }

            slot.createItem(type, n);
            _bagSlot = slot;
        }

        private static InvSlot _bagSlot;

        /// <summary>
        /// The hotbar and bag slots TakeOut and GiveBag used, as they were.
        /// </summary>
        private static void Restore()
        {
            foreach (var (slot, was) in new[] { _hotbarWas, _bagWas })
            {
                if (slot == null)
                {
                    continue;
                }

                if (!InvItemClass.isNull(slot.invItem))
                {
                    slot.removeItem();
                }

                if (was != null)
                {
                    slot.createItem(was);
                }
            }

            _hotbarWas = _bagWas = (null, null);
        }

        private static List<string> LogSince(int mark, string pattern) =>
            MapScenarioIssues.LogLines().Skip(mark)
                .Where(l => Regex.IsMatch(l, pattern)).ToList();
    }

    /// <summary>
    /// The map test aims with the mouse at what a step points it at
    /// (Player.throwItem's distance, its facing).
    /// </summary>
    [HarmonyPatch(
        typeof(Core), nameof(Core.mouseWorldPosition), new[] { typeof(bool) })]
    static class MapScenarioAimPatch
    {
        static void Postfix(ref Vector3 __result)
        {
            var at = MapScenarioItems2.AimAt;
            if (at.HasValue)
            {
                __result = new Vector3(at.Value.x, __result.y, at.Value.z);
            }
        }
    }

    /// <summary>
    /// The map test's i2-safe: our player healed whenever it's below half
    /// health (in Player.setHealth, before the game checks for a death).
    /// </summary>
    [HarmonyPatch(typeof(Player), nameof(Player.setHealth))]
    static class MapScenarioSafePatch
    {
        internal static void Postfix(Player __instance)
        {
            if (!MapScenarioItems2.Safe || __instance == null
                || !__instance.alive)
            {
                return;
            }

            if (__instance.health < __instance.maxHealth * 0.5f)
            {
                __instance.health = __instance.maxHealth;
            }
        }
    }

    /// <summary>
    /// The map test: what the host's characters heard (Character.heardSound).
    /// </summary>
    [HarmonyPatch(typeof(Character), nameof(Character.heardSound), new[]
    {
        typeof(Vector3),
        typeof(float),
        typeof(bool),
        typeof(float),
        typeof(bool)
    })]
    static class MapScenarioHeardPatch
    {
        static void Postfix(
            Character __instance, Vector3 Position, bool dangerousSound)
        {
            if (!MapScenarioItems2.Recording)
            {
                return;
            }

            var h = MapScenarioItems2.Heard;
            if (h.Count > 200)
            {
                h.RemoveAt(0);
            }

            h.Add((__instance, Position, dangerousSound));
        }
    }
}
