using System.Collections;
using System.Linq;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;

namespace DarkwoodMP.World
{
    public static partial class MapScenarioDreams
    {
        // The level dream at home (dream_home): the player wakes in the
        // bedroom, and walking into the knocking area
        // (start_knocking_dreamHome) unlocks the bedroom's door (a modifyDoor
        // unlock step) for the way out.
        private static EventTriggers Knocking()
        {
            if (D.dreamLocation == null)
            {
                return null;
            }

            return D.dreamLocation.GetComponentsInChildren<EventTriggers>(true)
                .FirstOrDefault(
                    x => x.name.StartsWith("start_knocking_dreamHome"));
        }

        private static Door HomeDoorOf(EventTriggers et)
        {
            GameEvents ge;
            if (et == null)
            {
                ge = null;
            }
            else
            {
                ge = et.GetComponent<GameEvents>();
            }

            GameEvent step;
            if (ge == null)
            {
                step = null;
            }
            else
            {
                step = ge.events.FirstOrDefault(
                    e => e != null && e.type == GameEvent.Type.modifyDoor);
            }

            GameObject go = null;
            if (step != null)
            {
                go = step.targetGameObjects.FirstOrDefault(g => g != null);
            }

            if (go == null)
            {
                return null;
            }

            Door door = go.GetComponent<Door>();
            if (door == null)
            {
                door = go.GetComponentInChildren<Door>();
            }

            return door;
        }

        private static string DoorInfo(Door d)
        {
            var l = d.GetComponent<Locked>();
            bool locked = l != null && l.locked;
            return $"{d.name} at {Fmt(d.transform.position)} "
                + $"{ObjRef.Of(d.gameObject)} locked {locked} "
                + $"blocked {d.blocked} open {d.opened}";
        }

        /// <summary>
        /// "home-door locked|unlocked|open" (either): the bedroom door of the
        /// home dream is in that state here.
        /// </summary>
        private static IEnumerator HomeDoor(string[] a, Result r)
        {
            yield return new WaitForSeconds(0.5f);
            var door = HomeDoorOf(Knocking());
            if (door == null)
            {
                Location dreamLocation = D.dreamLocation;
                string name = null;
                if (dreamLocation != null)
                {
                    name = dreamLocation.name;
                }

                r.Fail($"no knocking area or door in {name}");
                yield break;
            }

            var l = door.GetComponent<Locked>();
            bool locked = l != null && l.locked;
            r.Info = DoorInfo(door);
            string want = "";
            if (a.Length > 0)
            {
                want = a[0];
            }

            if ((want == "locked" && !locked)
                || (want == "unlocked" && (locked || door.blocked))
                || (want == "open" && !door.opened))
            {
                r.Fail($"the door isn't {want}: {r.Info}");
            }
        }

        /// <summary>
        /// "home-knock" (either): our player walks into the knocking area,
        /// which unlocks the bedroom door.
        /// </summary>
        private static IEnumerator HomeKnock(string[] a, Result r)
        {
            var et = Knocking();
            if (et == null)
            {
                Location dreamLocation = D.dreamLocation;
                string name = null;
                if (dreamLocation != null)
                {
                    name = dreamLocation.name;
                }

                r.Fail($"no knocking area in {name}");
                yield break;
            }

            var p = P;
            p.transform.position = new Vector3(
                et.transform.position.x, p.transform.position.y,
                et.transform.position.z);
            var rb = p.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.position = p.transform.position;
            }

            yield return new WaitForSeconds(0.3f);
            AsPlayer(() => et.fireEventTrigger(global::EventTrigger.Type.area));
            yield return new WaitForSeconds(3f);
            var door = HomeDoorOf(et);
            string line =
                $"walked into {et.name} at {Fmt(et.transform.position)}; ";
            string state = "no door";
            if (door != null)
            {
                state = DoorInfo(door);
            }

            r.Info = line + state;
        }

        /// <summary>
        /// "home-open" (either): our player opens the bedroom door, as a player
        /// can only when it isn't locked here, and it stays open.
        /// </summary>
        private static IEnumerator HomeOpen(string[] a, Result r)
        {
            var door = HomeDoorOf(Knocking());
            if (door == null)
            {
                Location dreamLocation = D.dreamLocation;
                string name = null;
                if (dreamLocation != null)
                {
                    name = dreamLocation.name;
                }

                r.Fail($"no door in {name}");
                yield break;
            }

            var p = P;
            var to = door.transform.position
                + (p.transform.position - door.transform.position).normalized
                    * 150f;
            p.transform.position = new Vector3(
                to.x, p.transform.position.y, to.z);
            var rb = p.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.position = p.transform.position;
            }

            yield return new WaitForSeconds(0.5f);
            var l = door.GetComponent<Locked>();
            if ((l != null && l.locked) || door.blocked)
            {
                r.Fail($"we're locked in: {DoorInfo(door)}");
                yield break;
            }

            if (!door.opened)
            {
                AsPlayer(
                    () => door.open(p.transform.position, p.transform, 0f));
            }

            // a refusal by the host closes it again
            yield return new WaitForSeconds(2f);
            r.Info = DoorInfo(door);
            if (!door.opened)
            {
                r.Fail($"the door didn't stay open: {r.Info}");
            }
        }
    }
}
