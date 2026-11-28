using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;

namespace DarkwoodMP.World
{
    /// <summary>
    /// For MapScenarioDive: put on the player; a runFunction step on the player
    /// calls this (SendMessage with the trigger's ID).
    /// </summary>
    public sealed class PlayerStepCounter : MonoBehaviour
    {
        public readonly Dictionary<string, int> N =
            new Dictionary<string, int>();
        private void DwmpOwnCount(string id) => N[id] = Of(id) + 1;
        public int Of(string id)
        {
            if (N.TryGetValue(id, out var n))
            {
                return n;
            }

            return 0;
        }
    }

    /// <summary>
    /// Steps of the two-game map test for diving in the village cellar and
    /// steps that act on the player (tools/tests/map_scenario_dive.py).
    ///
    /// A step whose target is the player (the unique object "player", or the
    /// player's own object) was run as a shared step, so each game ran it on
    /// its own player: one player diving in the cellar (runFunction diveIn on
    /// "player") made every player dive. Now it's the player's own
    /// (ScriptedEvents.TargetsPlayer), and a later player of a once-only
    /// trigger gets it too.
    /// </summary>
    public static partial class MapScenarioDive
    {
        public static void Register()
        {
            Add("dive-scan", Scan, timeout: 30f);
            Add("own-setup", OwnSetup);
            Add("own-fire", OwnFire, timeout: 30f);
            Add("own-count", OwnCount, timeout: 15f);
            Add("dive-sounds-zero", DiveSoundsZero);
            Add("dive-sounds", DiveSounds, timeout: 15f);
            Add("trip-setup", TripSetup);
            Add("trip-go", TripGo, timeout: 40f);
            Add("tank-setup", TankSetup);
            Add("tank-give", TankGive, timeout: 15f);
            Add("tank-take", TankTake);
            Add("tank-flag", TankFlag, timeout: 15f);
            Add("tank-fire", TankFire, timeout: 30f);
        }

        // ---- a scripted trip the host's movement check must let through -----
        // A short one, which the host's movement check must let through.

        private sealed class Trip
        {
            public EventTriggers Et;
            public Transform To;
        }

        private static readonly Dictionary<string, Trip> _trips =
            new Dictionary<string, Trip>();

        /// <summary>
        /// "trip-setup ID DX DZ DX2 DZ2" (both games): an area trigger ID at
        /// the centre plus (DX, DZ), each time, whose one step takes the player
        /// to a marker at the centre plus (DX2, DZ2) (transportPlayerToObject,
        /// as the cellar's dive does).
        /// </summary>
        private static IEnumerator TripSetup(string[] a, Result r)
        {
            if (a.Length < 5)
            {
                r.Fail("trip-setup ID DX DZ DX2 DZ2");
                yield break;
            }

            var pos = Centre + new Vector3(F(a[1]), 0f, F(a[2]));
            var go = Root("dwmp_trip_" + a[0], pos);
            var to = Root(
                "dwmp_trip_to_" + a[0],
                Centre + new Vector3(F(a[3]), 0f, F(a[4]))).transform;
            var ge = go.AddComponent<GameEvents>();
            ge.multipleFire = true;
            ge.events.Add(new GameEvent
            {
                type = GameEvent.Type.transportPlayerToObject,
                targetGameObjects = new List<GameObject> { to.gameObject },
                floatValue = 0.1f,
                magnitude = 0.1f,
                eventAmount = 1
            });

            var et = go.AddComponent<EventTriggers>();
            var t = new global::EventTrigger
            {
                type = global::EventTrigger.Type.area,
                multipleFire = true,
                gameEvents = ge
            };

            et.eventTriggers.Add(t);
            t.thisEventTriggers = et;
            _trips[a[0]] = new Trip { Et = et, To = to };
            yield return null;
            r.Info =
                $"trip {a[0]} "
                    + $"{ObjRef.Of(go)} from {Fmt(pos)} to {Fmt(to.position)}, "
                    + $"{StateDump.Flat(to.position - pos):0} away";
        }

        /// <summary>
        /// Corrections this session (MoveCheck.Corrections), 0 without one.
        /// </summary>
        private static int Corrections()
        {
            var coop = Coop.Current;
            if (coop == null)
            {
                return 0;
            }

            return coop.Moves.Corrections;
        }

        /// <summary>
        /// "trip-go ID": our player sets off trip ID, arrives at its marker and
        /// must stay there: the host may not put it back (PlayerCorrection) as
        /// if it had moved too fast.
        /// </summary>
        private static IEnumerator TripGo(string[] a, Result r)
        {
            if (a.Length < 1 || !_trips.TryGetValue(a[0], out var m)
                || m.Et == null)
            {
                r.Fail("trip-go ID (trip-setup first)");
                yield break;
            }

            yield return Walk(m.Et.transform.position, 100f);
            yield return new WaitForSeconds(1f);
            int corrections = Corrections();
            var p = Player.Instance;
            bool There() =>
                StateDump.Flat(p.transform.position - m.To.position) < 150f;
            AsPlayer(
                () => m.Et.fireEventTrigger(global::EventTrigger.Type.area));
            m.Et.entered++;
            float until = Time.realtimeSinceStartup + 10f;
            while (!There() && Time.realtimeSinceStartup < until)
            {
                yield return null;
            }

            bool arrived = There();
            // a correction comes within a few State updates
            yield return new WaitForSeconds(3f);
            float off = StateDump.Flat(p.transform.position - m.To.position);
            r.Info =
                $"arrived {arrived}, now at {Fmt(p.transform.position)} "
                    + $"({off:0} from the marker), "
                    + $"put back {Corrections() - corrections} "
                    + "time(s)";
            if (!arrived)
            {
                r.Fail($"the trip didn't take us there: {r.Info}");
            }
            else if (!There() || Corrections() != corrections)
            {
                r.Fail($"the host put us back: {r.Info}");
            }

            Core.forbidInputs = false;
            // a jump back would be a speed hack to the host's movement check
            yield return Walk(Centre);
            yield return new WaitForSeconds(1f);
        }

        private static GameObject Root(string name, Vector3 pos)
        {
            var old = GameObject.Find(name);
            if (old != null)
            {
                Object.Destroy(old);
            }

            var go = new GameObject(name);
            go.transform.position = pos;
            return go;
        }
    }
}
