using System.Collections;
using System.Collections.Generic;
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
            var doors = Doors();
            for (int i = 0; i < doors.Count; i++)
            {
                sb.Append($"\n  door {i}: {DoorInfo(doors[i])}");
            }

            var ets = InDream<EventTriggers>();
            int n = 0;
            for (int i = 0; i < ets.Count; i++)
            {
                var et = ets[i];
                _listed.Add(et.name);
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

        /// The dream's doors a player can use (each room also has an
        /// inactive copy of its door).
        private static List<Door> Doors()
        {
            return InDream<Door>()
                .Where(d => d.gameObject.activeInHierarchy)
                .ToList();
        }

        private static Door DoorAt(string[] a, Result r)
        {
            var doors = Doors();
            int i = int.Parse(a[0]);
            if (i >= doors.Count)
            {
                r.Fail($"no door {i} ({doors.Count} in the dream)");
                return null;
            }

            return doors[i];
        }

        /// <summary>
        /// "basement-door N" (either): our player opens the dream's door N (of
        /// the active ones),
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

        /// The trigger names basement-scan listed.
        private static readonly HashSet<string> _listed =
            new HashSet<string>();

        /// "NAME" or "NAME#K": the K-th trigger of that name in the dream
        /// (null: none).
        private static EventTriggers TriggerAt(string at)
        {
            string[] parts = at.Split('#');
            int k = 0;
            if (parts.Length > 1)
            {
                k = int.Parse(parts[1]);
            }

            var ets = InDream<EventTriggers>()
                .Where(x => x.name == parts[0])
                .ToList();
            if (k >= ets.Count)
            {
                return null;
            }

            return ets[k];
        }

        private static global::EventTrigger FirstOf(EventTriggers et)
        {
            return et.eventTriggers.FirstOrDefault(x => x != null);
        }

        /// <summary>
        /// "basement-fire NAME[#K]" (either): our player sets off the
        /// dream's trigger NAME (the K-th of that name; its first trigger)
        /// beside it. A once-only area trigger may go as our player lands
        /// beside it, and remove itself once fired.
        /// </summary>
        private static IEnumerator Fire(string[] a, Result r)
        {
            var et = TriggerAt(a[0]);
            if (et == null || FirstOf(et) == null)
            {
                r.Fail($"no trigger {a[0]} in the dream");
                yield break;
            }

            var t = FirstOf(et);
            Place(et.transform.position + new Vector3(40f, 0f, 0f));
            yield return new WaitForSeconds(1f);
            var type = t.type;
            if (et != null && !t.fired)
            {
                AsPlayer(() => et.fireEventTrigger(type));
            }

            float until = Time.realtimeSinceStartup + 10f;
            while (!t.fired && Time.realtimeSinceStartup < until)
            {
                yield return new WaitForSeconds(0.3f);
            }

            r.Info = $"{a[0]} ({type}): fired {t.fired}, still there "
                + $"{et != null}";
            if (!t.fired)
            {
                r.Fail($"it didn't fire: {r.Info}");
            }
        }

        /// <summary>
        /// "basement-fired NAME[#K]" (either): that trigger fired here too
        /// (another player set it off), or it is gone: it was listed and
        /// removed itself once fired.
        /// </summary>
        private static IEnumerator Fired(string[] a, Result r)
        {
            float until = Time.realtimeSinceStartup + 15f;
            var et = TriggerAt(a[0]);
            while (et != null && !FirstOf(et).fired
                && Time.realtimeSinceStartup < until)
            {
                yield return new WaitForSeconds(0.3f);
                et = TriggerAt(a[0]);
            }

            string name = a[0].Split('#')[0];
            if (et == null)
            {
                r.Info = $"{a[0]}: gone (removed once fired)";
                if (!_listed.Contains(name))
                {
                    r.Fail($"no trigger {a[0]}, and the scan didn't list it");
                }

                yield break;
            }

            bool fired = FirstOf(et).fired;
            r.Info = $"{a[0]}: fired {fired}";
            if (!fired)
            {
                r.Fail($"it didn't fire here: {r.Info}");
            }
        }
    }
}
