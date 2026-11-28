using System.Collections;
using System.Globalization;
using System.Linq;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;
using Outcome = CharacterDialogue.Dialogue.Board.Outcome;

namespace DarkwoodMP.World
{
    /// The epilogue's flat as two players go through it: the wardrobe's real
    /// dialogue outcome, the kitchen's wall, the bed and the floorboards.
    public static partial class MapScenarioEpilogue
    {
        /// <summary>
        /// "epiapt-wardrobe" (either game): our player changes clothes as the
        /// wardrobe's dialogue does it (DialogueWindow): its fireWorldEvent
        /// outcome runs only if the dialogue isn't already shown (or the
        /// outcome fires every time), then the dialogue is marked shown. Our
        /// player's clothes must change.
        /// </summary>
        private static IEnumerator AptWardrobe(string[] a, Result r)
        {
            CharacterDialogue.Dialogue found = null;
            CharacterDialogue owner = null;
            Outcome outcome = null;
            foreach (var cd in Singleton<Flags>.Instance.dialogues)
            {
                if (cd == null || found != null)
                {
                    continue;
                }

                foreach (var dl in cd.dialogues)
                {
                    var o = ClothesOutcome(dl);
                    if (o != null)
                    {
                        found = dl;
                        owner = cd;
                        outcome = o;
                        break;
                    }
                }
            }

            if (found == null)
            {
                r.Fail($"no dialogue fires {ClothesEvent}");
                yield break;
            }

            string what = $"{owner.name}/{found.name} alreadyShown "
                + $"{found.alreadyShown} multipleFire {outcome.multipleFire}";
            Plugin.Log.LogInfo($"[map] epiapt-wardrobe: {what}");
            bool before = P.changedClothes;
            bool fires = !found.alreadyShown || outcome.multipleFire;
            if (fires)
            {
                AsPlayer(() => Singleton<Events>.Instance.fireWorldEvent(
                    outcome.Value));
            }

            found.alreadyShown = true;
            float until = Time.realtimeSinceStartup + 6f;
            while (Time.realtimeSinceStartup < until
                && P.changedClothes == before)
            {
                yield return new WaitForSeconds(0.2f);
            }

            yield return new WaitForSeconds(1f);
            r.Info = $"{what}; outcome run {fires}; changed clothes "
                + $"{before} -> {P.changedClothes}, body {LibraryOf(P)}";
            if (!P.changedClothes || !LibraryOf(P).Contains("Epilogue"))
            {
                r.Fail($"our clothes didn't change: {r.Info}");
            }
        }

        private static Outcome ClothesOutcome(CharacterDialogue.Dialogue dl)
        {
            if (dl == null)
            {
                return null;
            }

            foreach (var b in dl.boards)
            {
                if (b == null || b.outcomes == null)
                {
                    continue;
                }

                foreach (var o in b.outcomes)
                {
                    if (o != null && o.type == Outcome.Type.fireWorldEvent
                        && o.Value == ClothesEvent)
                    {
                        return o;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// "epiapt-wall-gone" (either game): the kitchen's invisible wall,
        /// which the wardrobe's event removes, is gone here: removed, switched
        /// off, or without a collider that's on. (Walk moves our player
        /// straight through walls, so epiapt-room alone wouldn't notice.)
        /// </summary>
        private static IEnumerator AptWallGone(string[] a, Result r)
        {
            const string name = "ROOM_mask_kuchnia_wall";
            string seen = "";
            float until = Time.realtimeSinceStartup + 5f;
            while (Time.realtimeSinceStartup < until)
            {
                seen = WallState(Named<Transform>(name));
                if (seen == "gone")
                {
                    break;
                }

                yield return new WaitForSeconds(0.2f);
            }

            r.Info = $"{name}: {seen}";
            if (seen != "gone")
            {
                r.Fail($"the kitchen's wall is still up: {r.Info}");
            }
        }

        private static string WallState(Transform wall)
        {
            if (wall == null)
            {
                return "gone";
            }

            if (!wall.gameObject.activeInHierarchy)
            {
                return "gone";
            }

            var on = wall.GetComponentsInChildren<Collider>()
                .Where(c => c.enabled && !c.isTrigger).ToList();
            if (on.Count == 0)
            {
                return "gone";
            }

            return $"active, {on.Count} solid colliders ("
                + string.Join(",", on.Select(c => c.name)) + ")";
        }

        /// The bed the flat's events make draggable.
        private const string BedName = "Bed_epilog_01";

        /// Where the bed stood at epiapt-bed-ready, in this game.
        private static Vector3 _bedStart;

        /// <summary>
        /// "epiapt-bed-ready" (either game): the bed can be dragged, as
        /// makeBedDraggable_epilogue makes it (run here as our player's own
        /// if the bed is still off); its spot is remembered and becomes the
        /// centre the generic drag step finds it around.
        /// </summary>
        private static IEnumerator AptBedReady(string[] a, Result r)
        {
            var bed = Named<Transform>(BedName);
            if (bed == null)
            {
                r.Fail($"no {BedName}: {Where()}");
                yield break;
            }

            string how = "already on";
            if (!bed.gameObject.activeInHierarchy)
            {
                var ge = Named<GameEvents>("makeBedDraggable_epilogue");
                if (ge != null)
                {
                    how = "makeBedDraggable_epilogue run";
                    AsPlayer(() => ge.fire());
                }

                float until = Time.realtimeSinceStartup + 3f;
                while (Time.realtimeSinceStartup < until
                    && !bed.gameObject.activeInHierarchy)
                {
                    yield return new WaitForSeconds(0.2f);
                }
            }

            if (!bed.gameObject.activeInHierarchy)
            {
                r.Fail($"{BedName} is still off ({how})");
                yield break;
            }

            var it = bed.GetComponent<Item>();
            _bedStart = bed.position;
            SetCentre(_bedStart);
            yield return Walk(_bedStart, 120f);
            bool draggable = it != null && it.draggable;
            r.Info = $"{BedName} at {Fmt(_bedStart)} ({how}), draggable "
                + $"{draggable}";
            if (!draggable)
            {
                r.Fail($"can't drag it: {r.Info}");
            }
        }

        /// <summary>
        /// "epiapt-bed-moved DX DZ" (either game): the bed the other player
        /// dragged by DX, DZ moved here at least half as far.
        /// </summary>
        private static IEnumerator AptBedMoved(string[] a, Result r)
        {
            var by = new Vector3(
                float.Parse(a[0], CultureInfo.InvariantCulture),
                0f,
                float.Parse(a[1], CultureInfo.InvariantCulture));
            var bed = Named<Transform>(BedName);
            float moved = 0f;
            float until = Time.realtimeSinceStartup + 8f;
            while (bed != null && Time.realtimeSinceStartup < until)
            {
                moved = StateDump.Flat(bed.position - _bedStart);
                if (moved >= StateDump.Flat(by) * 0.5f)
                {
                    break;
                }

                yield return new WaitForSeconds(0.2f);
            }

            if (bed == null)
            {
                r.Fail($"no {BedName}");
                yield break;
            }

            r.Info = $"{BedName} from {Fmt(_bedStart)} to "
                + $"{Fmt(bed.position)}: moved {moved:0}";
            if (moved < StateDump.Flat(by) * 0.5f)
            {
                r.Fail($"the bed didn't move here: {r.Info}");
            }
        }

        /// The floorboard under the bed and its sensor (a disarmable Item).
        private const string Board = "floor_epilog_16";

        private const string BoardSensor = Board + "_sensor_event";

        /// <summary>
        /// "epiapt-floor-ready" (either game): the floorboard under the bed
        /// can be broken, as the roots' chain leaves it (its sensor switched
        /// on), with the flag the screwdriver sets.
        /// </summary>
        private static IEnumerator AptFloorReady(string[] a, Result r)
        {
            var sensor = Named<Transform>(BoardSensor);
            if (sensor == null)
            {
                r.Fail($"no {BoardSensor}: {Where()}");
                yield break;
            }

            sensor.gameObject.SetActive(true);
            Singleton<Flags>.Instance.setFlag("epilogue_playerHasTool", true);
            yield return new WaitForSeconds(0.5f);
            r.Info = SensorInfo(sensor);
            var item = sensor.GetComponent<Item>();
            var t = sensor.GetComponent<Trigger>();
            if (item == null || t == null || !t.canDisarm)
            {
                r.Fail($"not something to break: {r.Info}");
            }
        }

        private static string SensorInfo(Transform sensor)
        {
            var item = sensor.GetComponent<Item>();
            var t = sensor.GetComponent<Trigger>();
            string text = $"{sensor.name} at {Fmt(sensor.position)} active "
                + $"{sensor.gameObject.activeInHierarchy}";
            if (item != null)
            {
                string inv = "none";
                if (item.invItem != null)
                {
                    inv = item.invItem.type;
                }

                text += $"; item: no item needed {item.doesNotNeedItemToDisarm}"
                    + $", gives {inv}, chance {item.disarmChance}";
            }

            if (t != null)
            {
                text += $"; trigger: canDisarm {t.canDisarm}, on attempt "
                    + $"{t.triggerOnDisarmAttempt}, requirements "
                    + $"{t.disarmRequirements.Count} met "
                    + $"{t.requirementsMet()}, triggered {t.triggered}";
            }

            return text;
        }

        /// <summary>
        /// "epiapt-floor" (either game): our player breaks the floorboard as
        /// the game does: Item.attemptToDisarm (the use key), then the
        /// progress bar ends as our player's action. Its sensor must be gone
        /// here.
        /// </summary>
        private static IEnumerator AptFloor(string[] a, Result r)
        {
            var sensor = Named<Transform>(BoardSensor);
            if (sensor == null || !sensor.gameObject.activeInHierarchy)
            {
                r.Fail($"no {BoardSensor} to break (run epiapt-floor-ready)");
                yield break;
            }

            string info = SensorInfo(sensor);
            var item = sensor.GetComponent<Item>();
            yield return Walk(sensor.position, 90f);
            AsPlayer(() => item.attemptToDisarm());
            yield return null;
            if (!P.disarmingTrap || P.progressBar == null)
            {
                r.Fail($"didn't start breaking it: {info}");
                yield break;
            }

            Plugin.Instance.Host.TestAsPlayer = true;
            P.progressBar.time = 0.01f;
            float until = Time.realtimeSinceStartup + 6f;
            while (sensor != null && Time.realtimeSinceStartup < until)
            {
                yield return null;
            }

            Plugin.Instance.Host.TestAsPlayer = false;
            P.hidingInventory = false;
            yield return new WaitForSeconds(1f);
            r.Info = $"{info}: {BoardState()}";
            if (sensor != null)
            {
                r.Fail($"the floorboard didn't break: {r.Info}");
            }
        }

        /// <summary>
        /// "epiapt-floor-broken" (either game): the floorboard the other
        /// player broke is gone here too, and the hole under it is open.
        /// </summary>
        private static IEnumerator AptFloorBroken(string[] a, Result r)
        {
            string seen = "";
            float until = Time.realtimeSinceStartup + 8f;
            while (Time.realtimeSinceStartup < until)
            {
                seen = BoardState();
                if (BoardBroken())
                {
                    break;
                }

                yield return new WaitForSeconds(0.2f);
            }

            r.Info = seen;
            if (!BoardBroken())
            {
                r.Fail($"the floorboard isn't broken here: {seen}");
            }
        }

        private static bool BoardBroken()
        {
            var board = Named<Transform>(Board);
            var hole = Named<Transform>("HOLE_room_epilog_01");
            bool gone = board == null || !board.gameObject.activeInHierarchy;
            return gone && hole != null && hole.gameObject.activeInHierarchy;
        }

        private static string BoardState()
        {
            string Of(string name)
            {
                var t = Named<Transform>(name);
                if (t == null)
                {
                    return $"{name} gone";
                }

                return $"{name} active {t.gameObject.activeInHierarchy}";
            }

            return $"{Of(Board)}, {Of(BoardSensor)}, "
                + $"{Of("HOLE_room_epilog_01")}";
        }
    }
}
