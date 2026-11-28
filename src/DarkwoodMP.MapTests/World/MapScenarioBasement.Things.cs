using System.Collections;
using System.Linq;
using System.Text;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;

namespace DarkwoodMP.World
{
    /// <summary>
    /// The cellar dream's doors and triggers: listed in the same order in
    /// every game (by name, then by their spot in the dream), so "door N"
    /// and "trigger N" name the same one in both games.
    /// </summary>
    public static partial class MapScenarioBasement
    {
        private static string DoorInfo(Door d)
        {
            var l = d.GetComponent<Locked>();
            bool locked = l != null && l.locked;
            return $"{d.name} at {Fmt(d.transform.position)} "
                + $"{ObjRef.Of(d.gameObject)} locked {locked} "
                + $"blocked {d.blocked} open {d.opened}";
        }

        private static string Steps(GameEvents g)
        {
            if (g == null || g.events == null)
            {
                return "-";
            }

            var types = g.events
                .Where(e => e != null)
                .Select(e => $"{e.type}'{e.Value}'");
            return $"{g.name}[{string.Join(" ", types)}]";
        }

        /// <summary>
        /// "basement-scan" (either): log the dream's doors and triggers.
        /// </summary>
        private static IEnumerator Scan(string[] a, Result r)
        {
            var sb = new StringBuilder();
            var doors = InDream<Door>();
            for (int i = 0; i < doors.Count; i++)
            {
                sb.Append($"\n  door {i}: {DoorInfo(doors[i])}");
            }

            var ets = InDream<EventTriggers>();
            int n = 0;
            for (int i = 0; i < ets.Count; i++)
            {
                var et = ets[i];
                sb.Append(
                    $"\n  triggers {i}: {et.name} at "
                        + $"{Fmt(et.transform.position)} "
                        + $"active {et.gameObject.activeInHierarchy}");
                foreach (var t in et.eventTriggers)
                {
                    if (t == null)
                    {
                        continue;
                    }

                    n++;
                    sb.Append(
                        $"\n    > {t.type} fired {t.fired} multi "
                            + $"{t.multipleFire} disabled {t.disabled} "
                            + $"reqs {t.eventRequirements.Count}: "
                            + $"{Steps(t.gameEvents)}");
                }
            }

            Plugin.Log.LogInfo($"[basement] in the dream:{sb}");
            r.Info = $"{doors.Count} doors, {ets.Count} trigger objects with "
                + $"{n} triggers (in the log)";
            yield break;
        }

        private static Door DoorAt(string[] a, Result r)
        {
            var doors = InDream<Door>();
            int i = int.Parse(a[0]);
            if (i >= doors.Count)
            {
                r.Fail($"no door {i} ({doors.Count} in the dream)");
                return null;
            }

            return doors[i];
        }

        /// <summary>
        /// "basement-door N" (either): our player opens the dream's door N,
        /// and it stays open (the host's refusal would close it).
        /// </summary>
        private static IEnumerator OpenDoor(string[] a, Result r)
        {
            var door = DoorAt(a, r);
            if (door == null)
            {
                yield break;
            }

            var p = Player.Instance;
            Place(door.transform.position + new Vector3(80f, 0f, 0f));
            yield return new WaitForSeconds(1f);
            if (!door.opened)
            {
                AsPlayer(
                    () => door.open(p.transform.position, p.transform, 0f));
            }

            yield return new WaitForSeconds(2f);
            r.Info = DoorInfo(door);
            if (!door.opened)
            {
                r.Fail($"the door didn't stay open: {r.Info}");
            }
        }

        /// <summary>
        /// "basement-door-is N open|closed" (either): the dream's door N is
        /// so here.
        /// </summary>
        private static IEnumerator DoorIs(string[] a, Result r)
        {
            var door = DoorAt(a, r);
            if (door == null)
            {
                yield break;
            }

            bool open = a[1] == "open";
            float until = Time.realtimeSinceStartup + 15f;
            while (door.opened != open && Time.realtimeSinceStartup < until)
            {
                yield return new WaitForSeconds(0.3f);
            }

            r.Info = DoorInfo(door);
            if (door.opened != open)
            {
                r.Fail($"want {a[1]}: {r.Info}");
            }
        }

        private static (EventTriggers et, global::EventTrigger t) TriggerAt(
            string[] a, Result r)
        {
            var ets = InDream<EventTriggers>();
            int i = int.Parse(a[0]);
            if (i >= ets.Count)
            {
                r.Fail($"no trigger {i} ({ets.Count} in the dream)");
                return (null, null);
            }

            var et = ets[i];
            var t = et.eventTriggers.FirstOrDefault(x => x != null);
            if (t == null)
            {
                r.Fail($"{et.name} has no trigger");
            }

            return (et, t);
        }

        /// <summary>
        /// "basement-fire N" (either): our player sets off the dream's
        /// trigger N (its first trigger) beside it.
        /// </summary>
        private static IEnumerator Fire(string[] a, Result r)
        {
            var (et, t) = TriggerAt(a, r);
            if (t == null)
            {
                yield break;
            }

            Place(et.transform.position + new Vector3(40f, 0f, 0f));
            yield return new WaitForSeconds(1f);
            var type = t.type;
            AsPlayer(() => et.fireEventTrigger(type));
            float until = Time.realtimeSinceStartup + 10f;
            while (!t.fired && Time.realtimeSinceStartup < until)
            {
                yield return new WaitForSeconds(0.3f);
            }

            r.Info = $"{et.name} ({type}): fired {t.fired}; "
                + $"{Steps(t.gameEvents)}";
            if (!t.fired)
            {
                r.Fail($"it didn't fire: {r.Info}");
            }
        }

        /// <summary>
        /// "basement-fired N" (either): the dream's trigger N fired here too
        /// (another player set it off).
        /// </summary>
        private static IEnumerator Fired(string[] a, Result r)
        {
            var (et, t) = TriggerAt(a, r);
            if (t == null)
            {
                yield break;
            }

            float until = Time.realtimeSinceStartup + 15f;
            while (!t.fired && Time.realtimeSinceStartup < until)
            {
                yield return new WaitForSeconds(0.3f);
            }

            r.Info = $"{et.name} ({t.type}): fired {t.fired}";
            if (!t.fired)
            {
                r.Fail($"it didn't fire here: {r.Info}");
            }
        }
    }
}
