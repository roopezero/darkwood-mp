using System.Collections;
using System.Linq;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;
using Object = UnityEngine.Object;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Steps of the two-game map test for the village shortcut in chapter 1 (tools/tests/map_scenario_shortcut.py).
    ///
    /// The shortcut is the location sub_village_shortcut_01 (named VillageShortcut in the save): a hole in the
    /// village's fence. Going through the hole on the village side (holeBig_destFarm2, an onActivate trigger that
    /// moves the player to teleportPoint_farm) also fires the world event openVillageShortcut, whose two steps show
    /// the open hole on the farm side (holeBig_destVillage), its ground and colliders, and hide the closed one
    /// (level_locked). From then on holeBig_destVillage takes a player from the farm side into the village.
    /// The village is a separate location (outside_village_ch1_01, its own scene): holeBig_destFarm2 and
    /// teleportPoint_village are there, so they exist in a game only once its player has been to the village; the
    /// farm side (the rest of the shortcut) is in the world, in the farm next to it.
    /// </summary>
    public static class MapScenarioShortcut
    {
        private const string Event = "openVillageShortcut";
        private const string Out = "sub_village_shortcut_01_holeBig_destFarm2";
        private const string In = "sub_village_shortcut_01_holeBig_destVillage";
        private const string Locked = "sub_village_shortcut_01_level_locked";
        private const string ToFarm = "sub_village_shortcut_01_teleportPoint_farm";
        private const string ToVillage = "sub_village_shortcut_01_teleportPoint_village";

        public static void Register()
        {
            // "vs-info": what this game has of the shortcut.
            Add("vs-info", (a, r) => Info(r, State()));
            // "vs-open": our player goes through the hole on the village side (it opens the shortcut).
            Add("vs-open", (a, r) => Through(Out, ToFarm, r), timeout: 60f);
            // "vs-check open|closed": the shortcut is open (or closed) in this game.
            Add("vs-check", Check, timeout: 20f);
            // "vs-pass": our player goes through the open hole on the farm side, into the village.
            Add("vs-pass", (a, r) => Through(In, ToVillage, r), timeout: 60f);
            // "vs-reset": close it again in this game (the world event not fired, the objects as before), for the next case.
            Add("vs-reset", Reset);
            // "vs-leave": back to the world if we're in the village.
            Add("vs-leave", Leave, timeout: 40f);
        }

        private static GameObject Find(string name) =>
            Object.FindObjectsOfType<Transform>(true).Where(t => t != null && t.name == name && t.gameObject.scene.IsValid())
                .Select(t => t.gameObject).FirstOrDefault();

        private static GameEvents WorldEvent()
        {
            var ev = Singleton<Events>.Instance;
            if (ev != null && ev.worldEvents.TryGetValue(Event, out var ge))
            {
                return ge;
            }
            return null;
        }

        /// <summary>Whether the object is shown: by its SaveableObject's Active when it has one (the location
        /// may not be entered in this game, so the object itself can be off while the save says it's on).</summary>
        private static bool Shown(GameObject go)
        {
            if (go == null)
            {
                return false;
            }
            var so = go.GetComponent<SaveableObject>();
            if (so != null)
            {
                return so.Active;
            }
            return go.activeSelf;
        }

        private static bool IsOpen() => Shown(Find(In)) && !Shown(Find(Locked));

        private static string State()
        {
            var ge = WorldEvent();
            var loc = Find(In)?.transform.GetLocation();
            string text = "not fired";
            if (ge == null)
            {
                text = "not registered";
            }
            else if (ge.fired)
            {
                text = "fired";
            }
            return $"event {text}, {In}: {Desc(Find(In))}, "
                + $"{Locked}: {Desc(Find(Locked))}, {Out}: {Desc(Find(Out))}, location {(loc == null ? "none" : $"{loc.name} entered={loc.entered}")}";
        }

        private static string Desc(GameObject go)
        {
            if (go == null)
            {
                return "missing";
            }
            return $"shown={Shown(go)} active={go.activeInHierarchy} at {Fmt(go.transform.position)}";
        }

        private static IEnumerator Check(string[] a, Result r)
        {
            bool want = a.Length == 0 || a[0] == "open";
            float until = Time.realtimeSinceStartup + 15f;
            while (IsOpen() != want && Time.realtimeSinceStartup < until)
            {
                yield return new WaitForSeconds(0.25f);
            }
            var ge = WorldEvent();
            if (IsOpen() != want)
            {
                string text = "closed";
                if (want)
                {
                    text = "open";
                }
                r.Fail($"not {text}: {State()}");
            }
            else if (want && (ge == null || !ge.fired))
            {
                r.Fail($"open, but the world event isn't marked fired: {State()}");
            }
            else
            {
                r.Info = State();
            }
        }

        /// <summary>The village, a separate location: the hole on its side (holeBig_destFarm2) and teleportPoint_village
        /// are in its scene, loaded only when a player goes there.</summary>
        private const string Village = "outside_village_ch1_01";

        private static bool InVillage()
        {
            var o = Singleton<OutsideLocations>.Instance;
            return o != null && o.playerInOutsideLocation && !o.loading && Find(Out) != null && Find(Out).activeInHierarchy;
        }

        /// <summary>Use the hole as our player and see that we come out at the other end. The village side's hole is
        /// reached by going into the village first (as its gate does); the farm side's is in the world.</summary>
        private static IEnumerator Through(string hole, string dest, Result r)
        {
            var p = Player.Instance;
            var o = Singleton<OutsideLocations>.Instance;
            if (hole == Out && !InVillage())
            {
                AsPlayer(() => o.prepareLocation(Village));
                float t0 = Time.realtimeSinceStartup + 60f;
                while (!InVillage() && Time.realtimeSinceStartup < t0)
                {
                    yield return new WaitForSeconds(0.25f);
                }
                yield return new WaitForSeconds(2f);
                Core.forbidInputs = false;
                if (!InVillage())
                {
                    r.Fail($"couldn't get into {Village}: {State()}");
                    yield break;
                }
            }
            var go = Find(hole);
            var to = Find(dest);
            if (go == null || !go.activeInHierarchy)
            {
                r.Fail($"{hole} not here: {State()}");
                yield break;
            }
            if (StateDump.Flat(p.transform.position - go.transform.position) > 200f)
            {
                Place(go.transform.position + new Vector3(60f, 0f, 0f));
            }
            yield return new WaitForSeconds(1f);
            var et = go.GetComponent<EventTriggers>();
            if (et == null)
            {
                r.Fail($"{hole} has no triggers");
                yield break;
            }
            bool wasAway = o.playerInOutsideLocation;
            AsPlayer(() => et.fireEventTrigger(global::EventTrigger.Type.onActivate));
            // Into the village: we're in the separate location, by its end of the trip once its scene is there.
            // Out of it: back in the world, by teleportPoint_farm.
            float until = Time.realtimeSinceStartup + 60f;
            bool Arrived()
            {
                if (o.loading || o.playerInOutsideLocation == wasAway)
                {
                    return false;
                }
                if (to == null)
                {
                    to = Find(dest);
                }
                return to != null && StateDump.Flat(p.transform.position - to.transform.position) < 300f;
            }
            while (!Arrived() && Time.realtimeSinceStartup < until)
            {
                yield return new WaitForSeconds(0.25f);
            }
            yield return new WaitForSeconds(2f);
            Core.forbidInputs = false; // the trip's fade gives the inputs back on its own; a test step doesn't wait for it
            float d = -1f;
            if (to != null)
            {
                d = StateDump.Flat(p.transform.position - to.transform.position);
            }
            if (!Arrived())
            {
                string text;
                if (to == null)
                {
                    text = "not here";
                }
                else
                {
                    text = $"{d:0} from it";
                }
                r.Fail($"not taken to {dest} ({text}, away={o.playerInOutsideLocation}): {State()}");
                yield break;
            }
            r.Info = $"through {hole} to {Fmt(p.transform.position)}; {State()}";
        }

        /// <summary>"vs-leave": back to the world from the village (as its return path does), if we're there.</summary>
        private static IEnumerator Leave(string[] a, Result r)
        {
            var o = Singleton<OutsideLocations>.Instance;
            if (o.playerInOutsideLocation)
            {
                AsPlayer(() => o.returnToWorld());
            }
            float until = Time.realtimeSinceStartup + 30f;
            while ((o.playerInOutsideLocation || o.loading) && Time.realtimeSinceStartup < until)
            {
                yield return new WaitForSeconds(0.25f);
            }
            yield return new WaitForSeconds(2f);
            Core.forbidInputs = false;
            if (o.playerInOutsideLocation)
            {
                r.Fail("still in a separate location");
            }
            else
            {
                r.Info = $"in the world at {Fmt(Player.Instance.transform.position)}";
            }
        }

        private static IEnumerator Reset(string[] a, Result r)
        {
            var ge = WorldEvent();
            if (ge == null)
            {
                r.Fail("no world event " + Event);
                yield break;
            }
            WorldActions.Replaying = true; // put back here only, not shared
            try
            {
                ge.fired = false;
                Find(In).setMeActive(false);
                Find(Locked)?.setMeActive(true);
            }
            finally
            {
                WorldActions.Replaying = false;
            }
            yield return null;
            r.Info = State();
        }
    }
}
