using System.Collections;
using System.Linq;
using DarkwoodMP.Combat;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Steps of the two-game map test for separate locations (the order they
    /// run in is tools/tests/map_scenario_locations.py): a player goes into a
    /// location and back, the host puts a dog beside the client there, and the
    /// dumps are taken around the client's player ("where-me") to compare the
    /// enemies.
    /// </summary>
    public static partial class MapScenarioLocations
    {
        private static Character _dog; // host: the dog loc-dog made
        // host: the location's own enemy loc-wake woke
        private static Character _woken;

        public static void Register()
        {
            // "where-me": where our player stands (the driver dumps both games
            // around it).
            Add("where-me", (a, r) =>
            {
                Vector3 at;
                if (Player.Instance != null)
                {
                    at = Player.Instance.transform.position;
                }
                else
                {
                    at = Vector3.zero;
                }

                return Info(r, Fmt(at));
            });

            // "loc-go NAME": our player goes into that separate location, as
            // its entrance takes them there.
            Add("loc-go", Go, timeout: 60f);
            // "loc-back": our player goes back to the world, where they came
            // from.
            Add("loc-back", Back, timeout: 40f);
            // "loc-dog" (host): a Dog beside the joiner who's in a separate
            // location, and it goes for them.
            Add("loc-dog", Dog, timeout: 25f);
            // "loc-dog-check alive|dead" (client): the host's dog is shown
            // here, near our player, alive or dead.
            Add("loc-dog-check", DogCheck, timeout: 15f);
            // "loc-hit" (client): our player hits the host's dog;
            // "loc-dog-target" (host): the dog goes for them.
            Add("loc-hit", Hit, timeout: 20f);
            Add("loc-dog-target", DogTarget, timeout: 15f);
            // "loc-kill [woken]" (host): our player kills the dog (or the enemy
            // loc-wake woke).
            Add("loc-kill", Kill);
            // "loc-sight" (host): the dog, turned to face the joiner, sees them
            // (goes for them) before anyone hits it.
            Add("loc-sight", Sight, timeout: 15f);
            // "loc-save" (host): the game saves; a location kept only for the
            // joiner is saved as our player left it.
            Add("loc-save", Save, timeout: 30f);
            // "loc-tame" (host): the enemies of the location the joiner is in
            // bite for 1 (the test mustn't kill them).
            Add("loc-tame", Tame);
            // "loc-approach" (client): our player walks up to the host's
            // nearest sleeping enemy here.
            Add("loc-approach", Approach, timeout: 30f);
            // "loc-noise" (client): our player's running steps (noise enemies
            // hear, sent to the host as ours).
            Add("loc-noise", Noise, timeout: 10f);
            // "loc-wake" (host): the location's enemy nearest the joiner wakes
            // and goes for them.
            Add("loc-wake", Wake, timeout: 20f);
            // "loc-stopped NAME" (host): nobody else is in NAME: it isn't kept
            // running, and its characters are off.
            Add("loc-stopped", Stopped, timeout: 15f);
            // "corpse-out [KIND]" (host): a location's savage dies outside it
            // and the location is left: the corpse stays;
            // "corpse-out-check [KIND]" (client): it's here too, shown and
            // searchable.
            Add("corpse-out", CorpseOut, timeout: 30f);
            Add("corpse-out-check", CorpseOutCheck, timeout: 10f);
            // "view-scan [KIND]" (either): the ground around the nearest dead
            // KIND, around the other players' puppets and around our player,
            // where the game would switch our player's view off
            // (Sync.ViewWatch): nothing that is only there in co-op (a corpse,
            // a character, a puppet, a stand-in) may, and the info names
            // whatever does.
            Add("view-scan", ViewScan, timeout: 20f);
        }

        private static NetPlayerProxy Joiner() =>
            NetPlayerProxy.All.Values.FirstOrDefault(
                p => p != null && p.InLocation != null
                    && p.Owner != Sync.PlayerSync.DummyId);

        private static IEnumerator Info(Result r, string info)
        {
            r.Info = info;
            yield break;
        }

        private static IEnumerator Go(string[] a, Result r)
        {
            var o = Singleton<OutsideLocations>.Instance;
            if (o == null || a.Length < 1)
            {
                r.Fail("no OutsideLocations, or no location named");
                yield break;
            }

            string name = a[0];
            if (o.playerInOutsideLocation)
            {
                r.Fail($"already in {Locations.Current()}");
                yield break;
            }

            AsPlayer(() => o.prepareLocation(name, null));
            while (!(o.playerInOutsideLocation && !o.loading
                    && Locations.Current() == name))
            {
                yield return new WaitForSeconds(0.25f);
            }

            // the black screen fades, the location's objects switch on
            yield return new WaitForSeconds(2f);
            var p = Player.Instance;
            var loc = o.getLocation(name);
            string current = Locations.Current();
            string spot = Fmt(p.transform.position);
            string place = "?";
            if (loc != null)
            {
                place = Fmt(loc.transform.position);
            }

            r.Info = $"in {current} at {spot} (the location at {place}), " +
                     $"host runs it here: {Locations.HostRunsHere()}";
        }

        private static IEnumerator Back(string[] a, Result r)
        {
            var o = Singleton<OutsideLocations>.Instance;
            if (o == null || !o.playerInOutsideLocation)
            {
                r.Fail("not in a separate location");
                yield break;
            }

            string was = Locations.Current();
            AsPlayer(() => o.returnToWorld());
            while (o.playerInOutsideLocation || o.returningToWorld)
            {
                yield return new WaitForSeconds(0.25f);
            }

            yield return new WaitForSeconds(1f);
            string spot = Fmt(Player.Instance.transform.position);
            WorldGrid world = Singleton<WorldGrid>.Instance;
            string area = null;
            if (world != null)
            {
                WorldGrid.Grid grid = world.currentGrid;
                if (grid != null)
                {
                    area = grid.name;
                }
            }

            r.Info = $"back from {was} at {spot}, in {area}";
        }
    }
}
