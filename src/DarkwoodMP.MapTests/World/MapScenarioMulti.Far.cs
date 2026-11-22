using System;
using System.Collections;
using System.Linq;
using DarkwoodMP.Combat;
using UnityEngine;
using Object = UnityEngine.Object;
using static DarkwoodMP.World.MapScenario;

namespace DarkwoodMP.World
{
    /// <summary>Steps for things done while a player is far away.</summary>
    public static partial class MapScenarioMulti
    {
        // ---- 4. things done while a player is far away ----------------------

        /// <summary>"mt-door-drawn": the door nearest the centre is drawn as it
        /// is (an open door's body swung away from where it sits closed, a
        /// closed one's not), not only flagged so.</summary>
        private static IEnumerator DoorDrawn(string[] a, Result r)
        {
            var door = Nearest(
                Object.FindObjectsOfType<Door>().Where(
                    d => !d.destroyed && ObjRef.Of(d.gameObject).Valid),
                Centre);
            if (door == null || door.body == null)
            {
                r.Fail("no door");
                yield break;
            }

            // let a body that was just switched on settle
            yield return new WaitForSeconds(1f);
            float swung = DoorSwing(door);
            bool drawnOpen = Mathf.Abs(swung) > 15f;
            bool kinematic =
                door.body.GetComponent<Rigidbody>() is Rigidbody rb
                && rb.isKinematic;
            r.Info =
                $"{door.name} "
                    + $"{ObjRef.Of(door.gameObject)}: open {door.opened}, "
                    + $"swung {swung:0} degrees, kinematic {kinematic}";
            if (drawnOpen != door.opened)
            {
                string text = "closed";
                if (drawnOpen)
                {
                    text = "open";
                }

                r.Fail($"{r.Info}: drawn {text}");
            }
        }

        /// <summary>How far a door's body has swung on its hinge from where it
        /// sits closed (setCloseRotation puts its local z at 90).</summary>
        private static float DoorSwing(Door door) =>
            Mathf.DeltaAngle(90f, door.body.localRotation.eulerAngles.z);
    }
}
