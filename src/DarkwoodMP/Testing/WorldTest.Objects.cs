using DarkwoodMP.World.Inventories;
using DarkwoodMP.World.Rolls;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DarkwoodMP.World.Scripted;
using UnityEngine;

namespace DarkwoodMP.World
{
    /// <summary>
    /// The world test's object and world event steps (WorldTest).
    /// </summary>
    public static partial class WorldTest
    {
        /// <summary>
        /// Smaller world objects (World.ObjectActions and friends): what some
        /// prefabs are made of; a dropped item picked up with a click; a
        /// gasoline trail poured and set alight; a note picked up off the
        /// ground; an InteractiveItem switched; a feeder used; a lure eaten;
        /// the saw cutting a log. Each that's here goes to the observer.
        /// </summary>
        private static IEnumerator ObjectsStep(Player player, InvItemClass item)
        {
            var at = player.transform.position;
            foreach (var name in new[]
            {
                "Items/GasolineTrail",
                "Items/Gasoline",
                "Items/DroppedItem",
                "Objects/_Unique/deathDrop"
            })
            {
                var pf = Resources.Load("Prefabs/" + name) as GameObject;
                Inventory pinv = null;
                if (pf != null)
                {
                    pinv = pf.GetComponent<Inventory>();
                }

                Item pitem = null;
                if (pf != null)
                {
                    pitem = pf.GetComponent<Item>();
                }

                string components = Parts(pf);
                string removeWhenEmpty = "-";
                if (pinv != null)
                {
                    removeWhenEmpty = pinv.removeWhenEmpty.ToString();
                }

                string isDroppedItem = "-";
                if (pitem != null)
                {
                    isDroppedItem = pitem.isDroppedItem.ToString();
                }

                Plugin.Log.LogInfo(
                    $"[world test] prefab {name}: {components}; "
                        + $"removeWhenEmpty={removeWhenEmpty}, "
                        + $"isDroppedItem={isDroppedItem}");
            }

            InvItem lureItem = null;
            if (Singleton<ItemsDatabase>.Instance != null)
            {
                var drops = new List<string>();
                var lures = new List<string>();
                foreach (var (k, it) in DbItems())
                {
                    if (it.dropOnReleaseAim)
                    {
                        drops.Add(k);
                    }

                    if (it.item is GameObject ig
                        && ig.GetComponent<Lure>() != null)
                    {
                        string recoverNote = "";
                        if (it.recoverableAfterThrown)
                        {
                            recoverNote = " (recoverable)";
                        }

                        lures.Add(k + recoverNote);
                        if (lureItem == null)
                        {
                            lureItem = it;
                        }
                    }
                }

                Plugin.Log.LogInfo(
                    "[world test] items dropped on releasing aim: "
                        + $"{string.Join(", ", drops)}; lures: "
                        + $"{string.Join(", ", lures)}");
            }

            // A dropped item picked up again with a click
            // (Item.getDroppedItem).
            var dropped = player.spawnDroppedInvItem(new InvItemClass(item));
            yield return new WaitForSeconds(2.5f); // shared once at rest
            Item di = null;
            if (dropped != null)
            {
                di = dropped.GetComponent<Item>();
            }

            if (di != null)
            {
                Plugin.Log.LogInfo(
                    $"[world test] clicking dropped {item.type} "
                        + $"{ObjRef.Of(di.gameObject)}");
                di.getDroppedItem();
                yield return new WaitForSeconds(1f);
                string text = "still there";
                if (dropped == null)
                {
                    text = "gone";
                }

                Plugin.Log.LogInfo(
                    $"[world test] dropped item after the click: {text}");
            }

            // A gasoline trail, poured as Player.waitToSpillLiquid does, then
            // set alight.
            var trail = Core.AddPrefab(
                Containers.GasolineTrail, at + player.transform.up * 40f,
                Quaternion.Euler(90f, 0f, 0f), Core.ItemContainer);
            if (trail != null)
            {
                Core.addToSaveable(trail, isDynamic: true);
            }

            yield return new WaitForSeconds(1f);
            if (trail != null)
            {
                Plugin.Log.LogInfo(
                    "[world test] gasoline trail "
                        + $"{ObjRef.Of(trail)}: {Parts(trail)}");
                var liquid = trail.GetComponent<Liquid>();
                if (liquid != null)
                {
                    // as a match, a torch or a molotov lights it
                    liquid.startBurning();
                }

                Plugin.Log.LogInfo(
                    "[world test] gasoline trail lit: "
                        + $"burning={(liquid != null && liquid.burning)}");
                yield return new WaitForSeconds(1f);
            }

            // A note lying in the world, picked up.
            var noteItem = Object.FindObjectsOfType<JournalNoteReference>()
                .Where(
                    n => !n.dontDestroy && n.GetComponent<Item>() != null
                        && n.gameObject.scene.IsValid()
                        && ObjRef.Of(n.gameObject).Valid)
                .OrderBy(n => (n.transform.position - at).sqrMagnitude)
                .FirstOrDefault();
            if (noteItem != null)
            {
                Plugin.Log.LogInfo(
                    $"[world test] picking up note {noteItem.name} "
                        + $"{ObjRef.Of(noteItem.gameObject)} "
                        + $"at {noteItem.transform.position}");
                noteItem.GetComponent<Item>().activate();
                yield return new WaitForSeconds(0.5f);
                Singleton<UI>.Instance.journal.hideNote();
                yield return new WaitForSeconds(1f);
            }
            else
            {
                Plugin.Log.LogInfo("[world test] no note lying in the world");
            }

            // An InteractiveItem's switch, there and back.
            var ii = Nearest(
                Object.FindObjectsOfType<InteractiveItem>().Where(
                    x => ObjRef.Of(x.gameObject).Valid),
                at);
            if (ii != null)
            {
                var ui =
                    Singleton<UI>.Instance.transform.Find("InteractiveItem");
                Plugin.Log.LogInfo(
                    $"[world test] switching {ii.name} "
                        + $"{ObjRef.Of(ii.gameObject)}, on={ii.isOn}");
                ii.switchMe();
                if (ui != null)
                {
                    ui.gameObject.SetActive(false);
                }

                yield return new WaitForSeconds(1f);
                Plugin.Log.LogInfo(
                    $"[world test] switched {ii.name}: on={ii.isOn}");
            }
            else
            {
                Plugin.Log.LogInfo("[world test] no InteractiveItem here");
            }

            // A feeder.
            var feeder = Nearest(Object.FindObjectsOfType<Feeder>(), at);
            if (feeder != null)
            {
                Plugin.Log.LogInfo(
                    $"[world test] feeder {feeder.name} "
                        + $"{ObjRef.Of(feeder.gameObject)}: "
                        + $"enabled={feeder.isEnabled}, full={feeder.Active}");
                if (!feeder.isEnabled)
                {
                    // as building it does (Scripted builds it first, shared as
                    // Construct)
                    feeder.enable();
                }

                feeder.makeActive();
                feeder.activate();
                yield return new WaitForSeconds(1f);
            }
            else
            {
                Plugin.Log.LogInfo("[world test] no feeder here");
            }

            // A lure eaten up (as the host's animals do, Lure.removeHealth).
            if (lureItem != null)
            {
                var lureGo = Core.AddPrefab(
                    lureItem.item, at + new Vector3(60f, 0f, 60f),
                    Quaternion.Euler(90f, 0f, 0f), Core.ItemContainer,
                    worldSpace: true);
                Lure lure = null;
                if (lureGo != null)
                {
                    lure = lureGo.GetComponent<Lure>();
                }

                if (lure != null)
                {
                    yield return new WaitForSeconds(0.5f);
                    Plugin.Log.LogInfo(
                        $"[world test] lure {lureGo.name} {ObjRef.Of(lureGo)} "
                            + "eaten");
                    lure.removeHealth(lure.health + 1, null);
                    yield return new WaitForSeconds(1f);
                }
            }

            // The saw cutting a log.
            var saw = Nearest(Object.FindObjectsOfType<Saw>(), at);
            if (saw != null && saw.GetComponent<Inventory>() != null)
            {
                var sinv = saw.GetComponent<Inventory>();
                saw.fuel = Mathf.Max(saw.fuel, saw.fuelToConvert + 5f);
                if (InvItemClass.isNull(sinv.getItem("woodLog")))
                {
                    sinv.addItemType("woodLog", 1);
                }

                float before = saw.fuel;
                if (saw.canConvert())
                {
                    try
                    {
                        saw.convert();
                    }
                    catch (System.NullReferenceException)
                    {
                        // Saw.refresh wants the saw's window open; the fuel is
                        // taken before it
                    }
                }

                Plugin.Log.LogInfo(
                    $"[world test] saw {saw.name} "
                        + $"{ObjRef.Of(saw.gameObject)}: "
                        + $"fuel {before:0.0} -> {saw.fuel:0.0}");
                yield return new WaitForSeconds(1f);
            }
            else
            {
                Plugin.Log.LogInfo("[world test] no saw here");
            }
        }

        /// <summary>
        /// The world's own rolls, as the host makes them: a new day's weather,
        /// the night scenario and one of its events, the dawn's random objects,
        /// and the shadows' lights at this hideout (if it has a generator).
        /// Each goes to the observer (NightScenario, NightEvent, RandomObjects,
        /// Weather).
        /// </summary>
        private static IEnumerator WorldEventsStep(Player player)
        {
            var rain = Singleton<Rain>.Instance;
            if (rain != null)
            {
                rain.setUpNextRain();
                rain.onNewDay();
                Plugin.Log.LogInfo(
                    $"[world test] weather rolled: rain {rain.rainToday} at "
                        + $"{rain.timeToStart:0} for {rain.duration:0}");
            }

            var ns = Singleton<NightScenarios>.Instance;
            if (ns != null)
            {
                if (ns.currentScenario == null)
                {
                    ns.setCurrentScenario();
                }

                var sc = ns.currentScenario;
                string scenario = "none";
                if (sc != null)
                {
                    scenario = sc.name;
                }

                Plugin.Log.LogInfo($"[world test] night scenario {scenario}");
                CustomEvent ev = null;
                if (sc != null)
                {
                    ev = sc.customEventAndInts.Select(e =>
                    {
                        if (e != null)
                        {
                            return e.customEvent;
                        }

                        return null;
                    }).FirstOrDefault(
                        e => e != null && e.theEvent != null
                            && e.theEvent.type
                                == RandomEvent.Type.locationEvent);
                }

                var big = player.whereAmI.bigLocation;
                if (ev != null && big != null)
                {
                    ev.fire(force: true);
                    Plugin.Log.LogInfo(
                        $"[world test] night event {ev.name} fired at "
                            + $"{big.name}");
                }
                else
                {
                    string eventName = "none";
                    if (ev != null)
                    {
                        eventName = ev.name;
                    }

                    string locationName = "none";
                    if (big != null)
                    {
                        locationName = big.name;
                    }

                    Plugin.Log.LogInfo(
                        "[world test] no night event fired (event "
                            + $"{eventName}, location {locationName})");
                }
            }

            var gen = Singleton<WorldGenerator>.Instance;
            if (gen != null)
            {
                RandomWorldObjects instance =
                    Singleton<RandomWorldObjects>.Instance;
                if (instance != null)
                {
                    instance.resetObjects();
                }

                gen.despawnRandomObjects();
                gen.spawnRandomObjects();
                var rwo = Singleton<RandomWorldObjects>.Instance;
                int made = gen.worldChunks.Sum(c =>
                {
                    if (c != null)
                    {
                        return c.GetComponentsInChildren<SaveableObject>()
                            .Count(
                                o => rwo != null
                                    && rwo.objects.Any(
                                        r => r != null && r.prefab != null
                                            && r.prefab.name == o.name));
                    }

                    return 0;
                });

                int count = -1;
                if (rwo != null)
                {
                    count = rwo.objects.Count;
                }

                Plugin.Log.LogInfo(
                    "[world test] random objects respawned in "
                        + $"{gen.worldChunks.Count} chunks: {count} kinds, "
                        + $"{made} in the chunks now");
            }

            CoopSession coop = Coop.Current;
            bool lights = coop != null
                && coop.WorldEvents.ShadowLightsAnyTime(player);
            string text = "not here (no generator with lamps)";
            if (lights)
            {
                text = "sent";
            }

            Plugin.Log.LogInfo($"[world test] shadow lights {text}");
            // the random objects go 2 seconds after they're made; the weather
            // within a second
            yield return new WaitForSeconds(4f);
        }

        /// <summary>
        /// Triggers a player walks into or uses, near pos, that haven't fired
        /// and whose events have no steps of the player's own.
        /// </summary>
        private static List<(EventTriggers et, int i)> AreaTriggers(
            Vector3 pos) =>
            Object.FindObjectsOfType<EventTriggers>()
                .SelectMany(et => et.eventTriggers.Select((t, i) => (et, t, i)))
                .Where(
                    x => x.t != null
                        && (x.t.type == global::EventTrigger.Type.area
                            || x.t.type
                                == global::EventTrigger.Type.onActivate
                            || x.t.type
                                == global::EventTrigger.Type.onExamine)
                        && Unfired(x.t) && x.t.requirementsMet()
                        && x.t.gameEvents.events.All(
                            e => e != null
                                && !ScriptedEvents.IsPlayerStep(e.type))
                        && ObjRef.Of(x.et.gameObject).Valid)
                .OrderBy(x => (x.et.transform.position - pos).sqrMagnitude)
                .Select(x => (x.et, x.i)).ToList();

        /// <summary>
        /// A trigger that hasn't fired, isn't turned off, and has events of its
        /// own that haven't run.
        /// </summary>
        private static bool Unfired(global::EventTrigger t) =>
            !t.fired && !t.disabled && t.gameEvents != null
                && !t.gameEvents.fired && t.gameEvents.events.Count > 0;

        private static string Steps(GameEvents ge) => string.Join(
            ",",
            ge.events.Where(e => e != null).Select(e => e.type.ToString()));
    }
}
