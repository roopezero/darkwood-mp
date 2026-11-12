using System.Collections;
using System.Collections.Generic;

using DarkwoodMP.Combat;
using DarkwoodMP.Sync;
using UnityEngine;

namespace DarkwoodMP.World
{
    /// <summary>
    /// "--dwmp-test-away" (host, with --dwmp-dummy): once the dummy is in the world, the host's own player is away for
    /// a few seconds, as in a separate location (the world's grid is left and an empty stand-in grid is current), and
    /// comes back. Logs how many enemies near the dummy are active before and
    /// while the host is away (the world's grid is kept entered around other players, SharedCullingPatch), and whether
    /// the dummy's puppet is hidden while the host is away (a player in the main world isn't shown in a location).
    /// Before that, once another player has joined, the draggable object nearest the host's player is knocked away
    /// (its rigidbody given a speed, as a kick or a blast would), and the push sent when it comes to rest is logged.
    /// At the end it waits for the location the observer asked about (LocationAsk) to be loaded here, where it was
    /// promised (World.Locations logs the spot and turn it got next to the ones promised).
    /// </summary>
    public static class AwayTest
    {
        private static int ActiveNear(Vector3 at)
        {
            int n = 0;
            foreach (var c in Object.FindObjectsOfType<Character>())
                if (NetEnemies.Eligible(c) && c.alive && c.gameObject.activeInHierarchy && (c.transform.position - at).sqrMagnitude < 1500f * 1500f) n++;
            return n;
        }

        private static IEnumerator Push()
        {
            var me = Player.Instance.transform.position;
            Item best = null; float bestD = 500f * 500f;
            foreach (var it in Object.FindObjectsOfType<Item>())
            {
                if (!it.draggable || it.isDroppedItem || !it.gameObject.activeInHierarchy) continue;
                var b = it.GetComponent<Rigidbody>();
                if (b == null || b.isKinematic) continue;
                float d = (it.transform.position - me).sqrMagnitude;
                if (d < bestD) { bestD = d; best = it; }
            }
            if (best == null) { Plugin.Log.LogInfo("--dwmp-test-away: no draggable object near the player to push"); yield break; }
            int sent = DragSync.PushesSent;
            var from = best.transform.position;
            var body = best.GetComponent<Rigidbody>();
            var dir = from - me; dir.y = 0f;
            body.WakeUp();
            body.velocity = (dir.sqrMagnitude > 1f ? dir.normalized : Vector3.right) * 150f;
            Plugin.Log.LogInfo($"--dwmp-test-away: knocked {best.name} at {from.x:0},{from.z:0} (sharing {WorldActions.Sharing})");
            yield return new WaitForSeconds(4f);
            var to = best != null ? best.transform.position : from;
            Plugin.Log.LogInfo($"--dwmp-test-away: {(best != null ? best.name : "it")} moved {(to - from).magnitude:0} units to {to.x:0},{to.z:0}; pushes sent: {DragSync.PushesSent - sent}");
        }

        public static IEnumerator Run()
        {
            while (!WorldShare.InWorld || PlayerSync.Instance == null || !PlayerSync.Instance.HasDummy) yield return null;
            yield return new WaitForSeconds(6f);
            float wait = Time.realtimeSinceStartup + 30f;
            while (!WorldActions.Sharing && Time.realtimeSinceStartup < wait) yield return null;
            yield return Push();
            var dummy = PlayerSync.Instance.Remotes[PlayerSync.DummyId];
            var o = Singleton<OutsideLocations>.Instance;
            var grid = Singleton<WorldGrid>.Instance;
            if (o == null || grid == null || grid.currentGrid == null) { Plugin.Log.LogInfo("--dwmp-test-away: no world grid"); yield break; }
            Vector3 at = dummy.Root != null ? dummy.Root.transform.position : Vector3.zero;
            int before = ActiveNear(at);
            // What OutsideLocations.transportToLocation does to the world (leave every node of the world's grid,
            // switch to the location's grid, refresh) and to the player's state, on an empty stand-in grid: a real
            // location loads a scene and may start its story.
            var world = grid.currentGrid;
            var stand = new WorldGrid.Grid("dwmp-test-away", WorldGrid.Grid.Type.outsideLocation);
            grid.grids.Add(stand);
            Plugin.Log.LogInfo($"--dwmp-test-away: {before} enemies active near the dummy; the host goes away");
            world.leave();
            grid.setGrid(stand.name);
            o.playerInOutsideLocation = true;
            grid.refreshPosition(Player.Instance.transform.position, instant: true, force: true);
            yield return new WaitForSeconds(4f);
            int during = ActiveNear(at);
            bool shown = dummy.Root != null && dummy.Root.activeSelf;
            Plugin.Log.LogInfo($"--dwmp-test-away: in {grid.currentGrid?.name} (away {PlayerCapture.LocalAway()}): " +
                               $"{during} enemies active near the dummy; its puppet shown: {shown}");
            stand.leave();
            o.playerInOutsideLocation = false;
            grid.setGrid(world.name);
            grid.grids.Remove(stand);
            grid.refreshPosition(Player.Instance.transform.position, instant: true, force: true);
            yield return new WaitForSeconds(1f);
            // The observer (worldobs.py --ask-location) asked where a location stands: it's loaded here too, without going there.
            wait = Time.realtimeSinceStartup + 60f;
            while ((Locations.Busy || Locations.LoadedForOthers == 0) && Time.realtimeSinceStartup < wait) yield return null;
            Plugin.Log.LogInfo($"--dwmp-test-away: locations loaded for the others: {Locations.LoadedForOthers}; our player is in {(PlayerCapture.LocalAway() ? Locations.Current() : "the world")}");
            Plugin.Log.LogInfo($"--dwmp-test-away: back in {grid.currentGrid?.name} (away {PlayerCapture.LocalAway()}); {ActiveNear(at)} enemies active near the dummy; " +
                               $"its puppet shown: {dummy.Root != null && dummy.Root.activeSelf}; done");
        }
    }
}
