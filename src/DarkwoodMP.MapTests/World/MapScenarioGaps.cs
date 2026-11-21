using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Steps of the two-game map test for gaps in docs/INTERACTIONS.md that no
    /// other test reached: the dream clock (11), journal items handed over
    /// (61), the morning in the hideout (62, 63, 38), food rotting in an open
    /// container (64), vines (65), shadow-covered items (66) and a death bag's
    /// map marker (68). The order they run in is
    /// tools/tests/map_scenario_gaps.py.
    ///
    /// The things these steps add are put at fixed offsets from the centre
    /// (<see cref="Spots"/>), away from the stations, and found again in both
    /// games as the nearest of their kind to that spot. Waits are in real time:
    /// a dialogue may slow or stop the game's time.
    /// </summary>
    public static partial class MapScenarioGaps
    {
        private static List<NPC> _awayTraders = new List<NPC>();

        private static readonly Dictionary<string, Vector3> Spots =
            new Dictionary<string, Vector3>
            {
                ["rot"] = new Vector3(-700f, 0f, 700f),
                ["shadow"] = new Vector3(700f, 0f, -700f),
                ["vine"] = new Vector3(-700f, 0f, -700f),
                ["transport"] = new Vector3(600f, 0f, 600f),
            };

        private static Vector3 Spot(string which) => Centre + Spots[which];

        public static void Register()
        {
            MapScenario.Add("gaps-setup", Setup);
            // 11 dreams
            MapScenario.Add("dream", Dream, timeout: 30f);
            MapScenario.Add("clock-note", (a, r) =>
            {
                _noted = Singleton<Controller>.Instance.CurrentTime;
                r.Info = $"{_noted}";
                return Nothing();
            });

            MapScenario.Add("clock-check", ClockCheck, timeout: 30f);
            // 61 journal
            MapScenario.Add("journal-pick", JournalPick);
            MapScenario.Add("journal-add", JournalAdd);
            MapScenario.Add("journal-remove", JournalRemove);
            MapScenario.Add("journal-has", JournalHas, timeout: 20f);
            // 62, 63, 38 the morning
            MapScenario.Add("an-start", AnStart, timeout: 40f);
            MapScenario.Add("an-dawn", AnDawn);
            // "trader-away" (both games): the test area's night trader goes. In
            // the game he is only ever in the hideout in the morning; two
            // copies of one trader at once (the station's and the morning's)
            // get their stock by name (TraderSync), on a different copy in each
            // game.
            // He is switched off, not destroyed (TraderSync only finds active
            // traders), and "trader-back" switches him on again after the
            // morning steps, for the night trader's kill
            // (map_scenario_fixchecks.py) at the end.
            MapScenario.Add("trader-away", (a, r) =>
            {
                _awayTraders = StateDump.All<NPC>()
                    .Where(
                        n => n.trader && n.name == "nightTrader"
                            && n.gameObject.activeSelf
                            && StateDump.Flat(n.transform.position - Centre)
                                < 2000f).ToList();
                foreach (var n in _awayTraders)
                {
                    n.gameObject.SetActive(false);
                }

                r.Info = $"{_awayTraders.Count} switched off";
                return Nothing();
            });

            MapScenario.Add("trader-back", (a, r) =>
            {
                int n = 0;
                foreach (var t in _awayTraders)
                {
                    if (t != null)
                    {
                        t.gameObject.SetActive(true);
                        n++;
                    }
                }

                _awayTraders.Clear();
                r.Info = $"{n} back";
                return Nothing();
            });

            MapScenario.Add("an-end", AnEnd, timeout: 20f);
            MapScenario.Add("an-check", AnCheck, timeout: 20f);
            MapScenario.Add("an-clock", AnClock, timeout: 40f);
            MapScenario.Add("talk", Talk, timeout: 30f);
            MapScenario.Add("talk-end", TalkEnd, timeout: 20f);
            MapScenario.Add("trade-open", TradeOpen, timeout: 20f);
            MapScenario.Add("talk-free", TalkFree, timeout: 30f);
            // 64 rot
            MapScenario.Add("rot-open", RotOpen, timeout: 60f);
            MapScenario.Add("rot-host", RotHost);
            // 65 vines
            MapScenario.Add("vine-make", VineMake);
            MapScenario.Add("vine-cut", VineCut, timeout: 20f);
            MapScenario.Add("vine-check", VineCheck, timeout: 20f);
            // 66 shadows
            MapScenario.Add("shadow-ready", ShadowReady);
            MapScenario.Add("shadow-hit", ShadowHit, timeout: 20f);
            MapScenario.Add("shadow-check", ShadowCheck, timeout: 30f);
            MapScenario.Add("shadow-reload", ShadowReload);
            // 68 death bag marker
            MapScenario.Add("bag-drop", BagDrop, timeout: 20f);
            MapScenario.Add("bag-check", BagCheck, timeout: 20f);
            MapScenario.Add("bag-take", BagTake, timeout: 30f);

            StateDump.AddSection("vines", ctx =>
            {
                foreach (var cp in StateDump.All<ChainParent>())
                {
                    if (!ctx.Near(cp.transform.position))
                    {
                        continue;
                    }

                    var e = ctx.Add(cp.gameObject);
                    e["attached"] = cp.attached;
                    e["health"] = StateDump.R(cp.health, 1f);
                }
            });

            StateDump.AddSection("shadows", ctx =>
            {
                var crate = ShadowCrate();
                if (crate != null && ctx.Near(crate.transform.position))
                {
                    var e = ctx.Add(crate.gameObject);
                    e["shadow"] = crate.GetComponent<ShadowArmor>() != null;
                    e["health"] = crate.health;
                    e["destroyed"] = crate.destroyed;
                }

                foreach (var sa in StateDump.All<ShadowArmor>())
                {
                    if (sa.GetComponent<Item>() == null
                        || sa.GetComponent<Character>() != null
                        || !ctx.Near(sa.transform.position))
                    {
                        continue;
                    }

                    if (crate != null && sa.gameObject == crate.gameObject)
                    {
                        continue;
                    }

                    ctx.Add(sa.gameObject)["shadow"] = true;
                }
            });

            StateDump.AddSection("deathbags", ctx =>
            {
                foreach (var d in StateDump.All<DeathDrop>())
                {
                    if (!ctx.Near(d.transform.position))
                    {
                        continue;
                    }

                    var e = ctx.Add(d.gameObject);
                    if (d.additionalMapMarker != null)
                    {
                        e["marker"] = (object)StateDump.Pos(
                            d.additionalMapMarker.transform.position);
                    }
                    else
                    {
                        e["marker"] = "none";
                    }
                }
            });

            StateDump.AddSection("afternight", ctx =>
            {
                var c = Singleton<Controller>.Instance;
                if (c != null)
                {
                    ctx.Set("morning", new Dictionary<string, object>
                    {
                        ["afterNight"] = c.isAfterNight
                    });
                }
            });
        }

        private static IEnumerator Nothing() => MapScenarioIssues.Nothing();
        private static List<string> LogSince(int mark) =>
            MapScenarioIssues.LogLines().Skip(Math.Max(0, mark)).ToList();
        /// <summary>
        /// The log lines since `mark` that say something was refused (not this
        /// test's own [map] lines).
        /// </summary>
        private static List<string> Refused(int mark) => LogSince(mark)
            .Where(l => l.Contains("refused") && !l.Contains("[map]")).ToList();
        private static T NearestAll<T>(Vector3 to, Func<T, bool> ok)
            where T : Component =>
            MapScenarioIssues.NearestAll(to, ok);

        private static GameObject Place(
            string prefab, Vector3 at, bool save = true)
        {
            var go = Core.AddPrefab(
                prefab, at, Quaternion.Euler(90f, 0f, 0f), Core.ItemContainer,
                worldSpace: true);
            if (go != null && save)
            {
                Core.addToSaveable(go, isDynamic: true, assignID: true);
            }

            return go;
        }

        // ---- setup (host, before the join) ----------------------------------

        private const string RotPrefab =
            "Furnitures/Cottage/Wardrobe_low_small_01";
        private static readonly string[] ShadowPrefabs =
        {
            "Locations/med_hideout_05/"
                + "LootContainer_MetalCrate_med_hideout_05_level",
            "Loot_containers/_Locations/LootContainer_WoodenCrate_1A"
        };

        private static Inventory RotWardrobe() => NearestAll<Inventory>(
            Spot("rot"),
            i => i.invType == Inventory.InvType.itemInv
                && i.name.StartsWith(
                    "Wardrobe_low_small_01", StringComparison.Ordinal)
                && StateDump.Flat(i.transform.position - Spot("rot")) < 200f);
        private static Item ShadowCrate() => NearestAll<Item>(
            Spot("shadow"),
            i => StateDump.Flat(i.transform.position - Spot("shadow")) < 120f
                && i.GetComponent<Inventory>() != null);

        /// <summary>The first item type (by name) that rots in a container: one
        /// with a rotten form whose durability drains. One that isn't an
        /// experience item (mushrooms) if there is one: those drain only while
        /// the game's clock runs, which on a joiner it never does (the clock is
        /// the host's).</summary>
        private static string RotFood(out string all)
        {
            var db = Singleton<ItemsDatabase>.Instance;
            var found = new List<(string type, bool exp)>();
            foreach (var t in db.itemsDict.Keys.OrderBy(
                k => k, StringComparer.Ordinal))
            {
                InvItem it;
                try
                {
                    it = db.getItem(t, instantiate: false);
                }
                catch
                {
                    continue;
                }

                if (it != null && it.rottenItem != null
                    && it.durabilityDrain > 0f && it.maxDurability > 0f)
                {
                    found.Add((t, it.isExpItem));
                }
            }

            all = string.Join(",", found.Select(f =>
            {
                string mark = "";
                if (f.exp)
                {
                    mark = "(exp)";
                }

                return f.type + mark;
            }));

            string type =
                found.Where(f => !f.exp).Select(f => f.type).FirstOrDefault();
            if (type == null)
            {
                type = found.Select(f => f.type).FirstOrDefault();
            }

            return type;
        }

        /// <summary>"gaps-setup" (host, before the join): a wardrobe with
        /// rotting food in it; a crate with a shadow on it (one that dies from
        /// a few hits, and not from light).</summary>
        private static IEnumerator Setup(string[] a, Result r)
        {
            var made = new List<string>();
            var w = Place(RotPrefab, Spot("rot"));
            if (w == null)
            {
                r.Fail($"couldn't place {RotPrefab}");
                yield break;
            }

            GameObject crate = null;
            string used = null;
            foreach (var pf in ShadowPrefabs)
            {
                if (Resources.Load("Prefabs/" + pf) == null)
                {
                    continue;
                }

                crate = Place(pf, Spot("shadow"));
                if (crate != null)
                {
                    used = pf;
                    break;
                }
            }

            if (crate == null)
            {
                r.Fail("couldn't place a crate for the shadow");
                yield break;
            }

            yield return new WaitForSecondsRealtime(0.5f);

            Inventory inv = w.GetComponent<Inventory>();
            if (inv == null)
            {
                inv = w.GetComponentInChildren<Inventory>();
            }

            string food = RotFood(out string foods);
            // Not a failure here (that would stop the whole run): rot-open
            // fails later instead.
            if (inv == null || food == null)
            {
                int known = Singleton<ItemsDatabase>.Instance.itemsDict.Count;
                made.Add(
                    $"no inventory in the wardrobe ({inv != null}) or no "
                        + "rotting food (of "
                        + $"{known} items: {foods})");
            }
            else
            {
                if (inv.slots.Count == 0)
                {
                    inv.initSlots();
                }

                foreach (var s in inv.slots)
                {
                    if (!InvItemClass.isNull(s.invItem))
                    {
                        s.removeItem();
                    }
                }

                var max = Singleton<ItemsDatabase>.Instance.getItem(
                    food, instantiate: false).maxDurability;
                // durability 1: whole (the game multiplies it by the maximum)
                inv.slots[0].createItem(food, 1);
                made.Add(
                    "wardrobe "
                        + $"{ObjRef.Of(inv.gameObject)} "
                        + $"at {Fmt(w.transform.position)} with {food} "
                        + $"({max:0.##}; rotting: {foods})");
            }

            var item = crate.GetComponent<Item>();
            ShadowArmor sa = crate.GetComponent<ShadowArmor>();
            if (sa == null)
            {
                sa = crate.AddComponent<ShadowArmor>();
            }

            sa.maxHealth = 30f;
            sa.health = 30f;
            sa.normalDamageMod = 1f;
            sa.lightDamage = 0f;
            AccessTools.FieldRefAccess<ShadowArmor, float>("destHealth")(sa) =
                30f;
            if (item != null)
            {
                item.shadowArmor = sa;
            }

            made.Add(
                $"shadow crate {used} "
                    + $"{ObjRef.Of(crate)} at {Fmt(crate.transform.position)} "
                    + $"(item {item != null})");
            r.Info = string.Join("; ", made);
        }
    }
}
