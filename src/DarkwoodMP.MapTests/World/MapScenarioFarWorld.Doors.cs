using System;
using System.Collections;
using System.Linq;
using DarkwoodMP.Combat;
using DarkwoodMP.World.Doors;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;
using Object = UnityEngine.Object;

namespace DarkwoodMP.World
{
    /// <summary>Steps for a door opened where it is switched off.</summary>
    public static partial class MapScenarioFarWorld
    {
        private static Door DoorAt(Vector3 at) =>
            Nearest(
                Object.FindObjectsOfType<Door>(true).Where(
                    d => !d.destroyed && ObjRef.Of(d.gameObject).Valid),
                at);

        /// <summary>
        /// "far-door SPOT open|close": the door nearest the spot, walked to and
        /// opened or closed as our player.
        /// </summary>
        private static IEnumerator FarDoor(string[] a, Result r)
        {
            var door = DoorAt(Spot(a[0]));
            if (door == null)
            {
                r.Fail("no door");
                yield break;
            }

            yield return Walk(door.transform.position);
            bool open = a.Length < 2 || a[1] == "open";
            if (open != door.opened)
            {
                AsPlayer(() =>
                {
                    if (open)
                    {
                        door.open(P.transform.position, P.transform, 0f);
                    }
                    else
                    {
                        door.close(P.transform);
                    }
                });
            }

            yield return new WaitForSeconds(1f);
            r.Info =
                $"{door.name} "
                    + $"{ObjRef.Of(door.gameObject)} "
                    + $"at {Fmt(door.transform.position)} open={door.opened} "
                    + $"leaf {DoorLeaf.Angle(door):0} degrees";
            if (door.opened != open)
            {
                string text = "shut";
                if (open)
                {
                    text = "open";
                }

                r.Fail($"{r.Info}: not {text}");
            }
        }

        /// <summary>
        /// "far-door-check SPOT open|close": walk to the door nearest the spot;
        /// once it's switched on here, its leaf must be drawn as it is (open:
        /// turned from shut).
        /// </summary>
        private static IEnumerator FarDoorCheck(string[] a, Result r)
        {
            var door = DoorAt(Spot(a[0]));
            if (door == null)
            {
                r.Fail("no door");
                yield break;
            }

            bool open = a.Length < 2 || a[1] == "open";
            yield return Walk(door.transform.position);
            for (float t = 0f; t < 5f
                && (!door.gameObject.activeInHierarchy
                    || DoorLeaf.Turned(door) != open); t += 0.25f)
            {
                yield return new WaitForSeconds(0.25f);
            }

            yield return new WaitForSeconds(1f); // let the swing end
            r.Info =
                $"{door.name} "
                    + $"{ObjRef.Of(door.gameObject)} open={door.opened} "
                    + $"on={door.gameObject.activeInHierarchy} "
                    + $"leaf {DoorLeaf.Angle(door):0} degrees";
            if (door.opened != open || DoorLeaf.Turned(door) != open)
            {
                string text = "shut";
                if (open)
                {
                    text = "open and drawn open";
                }

                r.Fail($"{r.Info}: should be {text}");
            }
        }

        /// <summary>
        /// "far-doors-info SPOT": each door within 1000 of the spot: open,
        /// drawn open, its leaf's angle, switched on.
        /// </summary>
        private static IEnumerator FarDoorsInfo(string[] a, Result r)
        {
            string s = "centre";
            if (a.Length > 0)
            {
                s = a[0];
            }

            var at = Spot(s);
            r.Info = string.Join("; ",
                Object.FindObjectsOfType<Door>(true).Where(
                    d => StateDump.Flat(d.transform.position - at) < 1000f)
                    .Select(d =>
                    {
                        ObjRef id = ObjRef.Of(d.gameObject);
                        string spot = Fmt(d.transform.position);
                        bool opened = d.opened;
                        float angle = DoorLeaf.Angle(d);
                        Transform body = d.body;
                        float? turn = null;
                        if (body != null)
                        {
                            turn = body.localEulerAngles.z;
                        }

                        return $"{id} at {spot} open={opened} leaf {angle:0} "
                            + $"(z {turn:0}) "
                            + $"on={d.gameObject.activeInHierarchy}";
                    }));

            yield break;
        }
    }
}
