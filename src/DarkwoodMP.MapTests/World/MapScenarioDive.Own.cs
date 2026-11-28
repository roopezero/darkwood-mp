using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;

namespace DarkwoodMP.World
{
    /// <summary>Triggers whose steps run on the player.</summary>
    public static partial class MapScenarioDive
    {
        private sealed class Made
        {
            public EventTriggers Et;
            public global::EventTrigger T;
            public LaterTestCounter C;
        }

        private static readonly Dictionary<string, Made> _made =
            new Dictionary<string, Made>();

        private static PlayerStepCounter Counter()
        {
            var p = Player.Instance;
            PlayerStepCounter steps = p.GetComponent<PlayerStepCounter>();
            if (steps == null)
            {
                steps = p.gameObject.AddComponent<PlayerStepCounter>();
            }

            return steps;
        }

        /// <summary>
        /// "own-setup ID DX DZ once|multi|eventsonce" (both games): an area
        /// trigger ID at the centre plus (DX, DZ) with three runFunction steps:
        /// one on the unique object "player", one on our player's own object
        /// (each counts on our player, by ID), and a shared one on a counter
        /// beside it; and the dive's sound, played at the listener.
        /// </summary>
        private static IEnumerator OwnSetup(string[] a, Result r)
        {
            if (a.Length < 4)
            {
                r.Fail("own-setup ID DX DZ once|multi|eventsonce");
                yield break;
            }

            string id = a[0];
            var pos = Centre + new Vector3(F(a[1]), 0f, F(a[2]));
            // eventsonce: the trigger fires each time, its GameEvents run once
            // (the cellar's first dive)
            bool multi = a[3] != "once";
            var m = new Made();
            var go = Root("dwmp_own_" + id, pos);
            var ge = go.AddComponent<GameEvents>();
            ge.multipleFire = a[3] == "multi";
            var counter = Counter();
            counter.N.Remove(id);
            m.C = Root("dwmp_own_count_"
                    + id, pos).AddComponent<LaterTestCounter>();
            ge.events.Add(new GameEvent
            {
                type = GameEvent.Type.runFunction,
                Value = "DwmpOwnCount",
                activeModifier2 = true,
                stringValue = id,
                targetUniqueObjects = new List<string> { "player" },
                eventAmount = 1
            });

            ge.events.Add(new GameEvent
            {
                type = GameEvent.Type.runFunction,
                Value = "DwmpOwnCount",
                activeModifier2 = true,
                stringValue = id,
                targetGameObjects = new List<GameObject>
                {
                    Player.Instance.gameObject
                },
                eventAmount = 1
            });

            ge.events.Add(new GameEvent
            {
                type = GameEvent.Type.runFunction,
                Value = "DwmpLaterCount",
                targetGameObjects = new List<GameObject> { m.C.gameObject },
                eventAmount = 1
            });

            // the dive's sound, at the listener (MapScenarioDive.Sound)
            ge.events.Add(ListenerSound());

            m.Et = go.AddComponent<EventTriggers>();
            m.T = new global::EventTrigger
            {
                type = global::EventTrigger.Type.area,
                multipleFire = multi,
                gameEvents = ge
            };

            m.Et.eventTriggers.Add(m.T);
            m.T.thisEventTriggers = m.Et;
            _made[id] = m;
            yield return null;
            bool ours =
                Singleton<UniqueObjects>.Instance.getObject("player")
                == Player.Instance.gameObject;
            r.Info =
                $"trigger {id} "
                    + $"{ObjRef.Of(go)} at {Fmt(pos)}, {a[3]}; "
                    + $"the unique object player is ours: {ours}";
        }

        /// <summary>
        /// "own-fire ID OWN": our player walks to trigger ID and sets it off;
        /// OWN steps must act on our player (2, or 0).
        /// </summary>
        private static IEnumerator OwnFire(string[] a, Result r)
        {
            if (a.Length < 2 || !_made.TryGetValue(a[0], out var m)
                || m.Et == null)
            {
                r.Fail("own-fire ID OWN (own-setup first)");
                yield break;
            }

            int want = int.Parse(a[1]);
            yield return Walk(m.Et.transform.position, 100f);
            yield return new WaitForSeconds(0.5f);
            var c = Counter();
            int had = c.Of(a[0]);
            AsPlayer(
                () => m.Et.fireEventTrigger(global::EventTrigger.Type.area));
            m.Et.entered++;
            float until = Time.realtimeSinceStartup + 6f;
            while (Time.realtimeSinceStartup < until && c.Of(a[0]) - had < want)
            {
                yield return new WaitForSeconds(0.1f);
            }

            yield return new WaitForSeconds(1f);
            int got = c.Of(a[0]) - had;
            r.Info =
                $"{got} step(s) acted on our player (want {want}), the shared "
                    + $"step ran {m.C.N} time(s) here";
            if (got != want)
            {
                r.Fail(r.Info);
            }

            MapScenario.Place(Centre);
        }

        /// <summary>
        /// "own-count ID OWN SHARED": in this game, OWN steps of ID acted on
        /// our player and its shared step ran SHARED times.
        /// </summary>
        private static IEnumerator OwnCount(string[] a, Result r)
        {
            if (a.Length < 3 || !_made.TryGetValue(a[0], out var m)
                || m.C == null)
            {
                r.Fail("own-count ID OWN SHARED (own-setup first)");
                yield break;
            }

            int own = int.Parse(a[1]), shared = int.Parse(a[2]);
            float until = Time.realtimeSinceStartup + 4f;
            while (m.C.N < shared && Time.realtimeSinceStartup < until)
            {
                yield return new WaitForSeconds(0.2f);
            }

            yield return new WaitForSeconds(1f);
            int got = Counter().Of(a[0]);
            r.Info =
                $"{got} step(s) of {a[0]} acted on our player (want {own}), "
                    + $"its shared step ran {m.C.N} time(s) (want {shared})";
            if (got != own || m.C.N != shared)
            {
                r.Fail(r.Info);
            }
        }
    }
}
