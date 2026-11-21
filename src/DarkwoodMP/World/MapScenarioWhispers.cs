using System.Collections;
using System.Linq;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;
using Object = UnityEngine.Object;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Steps of the two-game map test for an area's enter and exit events
    /// (tools/tests/map_scenario_whispers.py). Players got stuck hearing
    /// whispers: an area trigger's enter events are shared (a looping whisper
    /// sound starts in every game), but its exit events
    /// (EventTrigger.gameEventsExit, the sound's stop) ran only in the game of
    /// the player who walked out. The test area gets one such trigger, the same
    /// in both games, and each player walks in and out of it in turn.
    /// </summary>
    public static class MapScenarioWhispers
    {
        private static readonly string[] Hosts =
            { "candles_01", "Radio", "Stove" };
        private const string Fallback = "whispers_loop_01_2d";

        private static EventTriggers _et;
        private static global::EventTrigger _t;
        private static string _sound;

        public static void Register()
        {
            MapScenario.Add("whisper-setup", Setup);
            MapScenario.Add("whisper-enter", Enter, timeout: 25f);
            MapScenario.Add("whisper-leave", Leave, timeout: 25f);
            MapScenario.Add("whisper-check", Check, timeout: 15f);
            MapScenario.Add("whisper-stand", Stand, timeout: 30f);
            MapScenario.Add("whisper-out", Out, timeout: 30f);
        }

        /// <summary>
        /// "whisper-setup" (both games): an area trigger on the first of Hosts
        /// near the centre (a saved object, so it has the same ObjRef in both
        /// games), firing each time: entering plays a looping whisper (a sound
        /// step with no target, as the game's 2D whispers are played), leaving
        /// stops it (a sound step's stop).
        /// </summary>
        private static IEnumerator Setup(string[] a, Result r)
        {
            SaveableObject host = null;
            foreach (var n in Hosts)
            {
                host = Nearest(Object.FindObjectsOfType<SaveableObject>()
                        .Where(i => i.gameObject.name.Replace("(Clone)", "")
                            .Trim() == n && ObjRef.Of(i.gameObject).Valid
                            && StateDump.Flat(i.transform.position - Centre)
                                < 1500f),
                        Centre);
                if (host != null)
                {
                    break;
                }
            }
            if (host == null)
            {
                r.Fail($"none of {string.Join(", ", Hosts)} here");
                yield break;
            }
            if (a.Length > 0)
            {
                _sound = a[0];
            }
            else
            {
                _sound = Fallback;
            }
            if (AudioController.GetAudioItem(_sound) == null)
            {
                r.Fail($"no audio item {_sound}");
                yield break;
            }

            var go = new GameObject("dwmp_whisper_area");
            go.transform.SetParent(host.transform, false);
            var enter =
                new GameObject("dwmp_whisper_enter").AddComponent<GameEvents>();
            enter.transform.SetParent(go.transform, false);
            enter.multipleFire = true;
            enter.events.Add(new GameEvent
            {
                type = GameEvent.Type.sound,
                soundModifyType = GameEvent.SoundModify.play,
                Value = _sound,
                floatValue = 1f,
                eventAmount = 1
            });
            var exit =
                new GameObject("dwmp_whisper_exit").AddComponent<GameEvents>();
            exit.transform.SetParent(go.transform, false);
            exit.multipleFire = true;
            exit.events.Add(new GameEvent
            {
                type = GameEvent.Type.sound,
                soundModifyType = GameEvent.SoundModify.stop,
                Value = _sound,
                eventAmount = 1
            });
            _et = go.AddComponent<EventTriggers>();
            _t = new global::EventTrigger
            {
                type = global::EventTrigger.Type.area,
                multipleFire = true,
                gameEvents = enter,
                gameEventsExit = exit
            };
            _et.eventTriggers.Add(_t);
            // EventTriggers.Awake ran before the trigger was in its list
            _t.thisEventTriggers = _et;
            yield return null;
            r.Info =
                $"area on {host.name} {ObjRef.Of(go)} at {Fmt(go.transform.position)}, sound {_sound}";
        }

        /// <summary>
        /// Whether the whispers play here (one fading out after its stop, over
        /// the sound's own fade-out, counts as stopped).
        /// </summary>
        private static bool Playing() =>
            AudioController.GetPlayingAudioObjects(
                _sound, includePausedAudio: false).Any(
                    o => o != null && o.IsPlaying()
                        && !o.isFadingOutOrScheduled);

        /// <summary>
        /// "whisper-enter": our player walks into the area (as
        /// EventTriggers.OnTriggerEnter does); the whispers play here.
        /// </summary>
        private static IEnumerator Enter(string[] a, Result r)
        {
            if (_et == null)
            {
                r.Fail("no area (whisper-setup first)");
                yield break;
            }
            yield return Walk(_et.transform.position, 150f);
            AsPlayer(
                () => _et.fireEventTrigger(global::EventTrigger.Type.area));
            _et.entered++;
            float until = Time.realtimeSinceStartup + 4f;
            while (!Playing() && Time.realtimeSinceStartup < until)
            {
                yield return new WaitForSeconds(0.2f);
            }
            r.Info =
                $"entered: {_sound} playing {Playing()}, trigger fired {_t.fired}";
            if (!Playing())
            {
                r.Fail($"the whispers don't play here: {r.Info}");
            }
        }

        /// <summary>
        /// "whisper-leave": our player walks out of the area (as
        /// EventTriggers.OnTriggerExit does); the whispers stop here.
        /// </summary>
        private static IEnumerator Leave(string[] a, Result r)
        {
            if (_et == null)
            {
                r.Fail("no area (whisper-setup first)");
                yield break;
            }
            _et.exited++;
            AsPlayer(
                () => _et.fireEventTriggerExit(global::EventTrigger.Type.area));
            float until = Time.realtimeSinceStartup + 4f;
            while (Playing() && Time.realtimeSinceStartup < until)
            {
                yield return new WaitForSeconds(0.2f);
            }
            r.Info =
                $"left: {_sound} playing {Playing()}, exit ran {_t.firedExit}";
            if (Playing())
            {
                r.Fail($"the whispers go on here: {r.Info}");
            }
        }

        /// <summary>
        /// "whisper-check playing|stopped": what the other player's entering or
        /// leaving did to the whispers here.
        /// </summary>
        private static IEnumerator Check(string[] a, Result r)
        {
            if (_et == null)
            {
                r.Fail("no area (whisper-setup first)");
                yield break;
            }
            bool want = a.Length == 0 || a[0] == "playing";
            float until = Time.realtimeSinceStartup + 5f;
            while (Playing() != want && Time.realtimeSinceStartup < until)
            {
                yield return new WaitForSeconds(0.2f);
            }
            yield return new WaitForSeconds(0.5f);
            string sound = _sound;
            bool playing = Playing();
            string wanted = "stopped";
            if (want)
            {
                wanted = "playing";
            }
            r.Info =
                $"{sound} playing {playing} (want {wanted}); entered {_et.entered}, left {_et.exited}, exit ran {_t.firedExit}";
            if (Playing() != want)
            {
                string why;
                if (want)
                {
                    why = $"the whispers don't play here: {r.Info}";
                }
                else
                {
                    why = $"stuck hearing the whispers here: {r.Info}";
                }
                r.Fail(why);
            }
        }

        /// <summary>
        /// "whisper-stand": give the area a real collider in this game only and
        /// walk our player into it, so the game's own OnTriggerEnter counts
        /// them in (and fires the area for them, as it fires each time).
        /// </summary>
        private static IEnumerator Stand(string[] a, Result r)
        {
            if (_et == null)
            {
                r.Fail("no area (whisper-setup first)");
                yield break;
            }
            // From outside it, as a player walks in (OnTriggerEnter counts a
            // player in only from its edge once it has been entered before).
            yield return Walk(
                _et.transform.position + new Vector3(0f, 0f, -700f), 80f);
            _et.entered = 0;
            _et.exited = 0;
            BoxCollider box = _et.gameObject.GetComponent<BoxCollider>();
            if (box == null)
            {
                box = _et.gameObject.AddComponent<BoxCollider>();
            }
            box.isTrigger = true;
            // A cube, as the object it's on is turned (the game's sprites lie
            // at 90 degrees) and may be scaled.
            float scale =
                Mathf.Max(0.01f, Mathf.Abs(_et.transform.lossyScale.x));
            box.size = Vector3.one * (500f / scale);
            Rigidbody rb = _et.gameObject.GetComponent<Rigidbody>();
            if (rb == null)
            {
                rb = _et.gameObject.AddComponent<Rigidbody>();
            }
            rb.isKinematic = true;
            rb.useGravity = false;
            yield return Walk(_et.transform.position, 100f);
            float until = Time.realtimeSinceStartup + 5f;
            while (!ScriptedEvents.OurPlayerInside(_et)
                && Time.realtimeSinceStartup < until)
            {
                yield return new WaitForSeconds(0.2f);
            }
            yield return new WaitForSeconds(1f);
            r.Info =
                $"in the area: {ScriptedEvents.OurPlayerInside(_et)} (entered {_et.entered}, left {_et.exited}); {_sound} playing {Playing()}";
            if (!ScriptedEvents.OurPlayerInside(_et))
            {
                r.Fail($"the game didn't count our player in: {r.Info}");
            }
        }

        /// <summary>
        /// "whisper-out": walk our player out of the real collider (the game's
        /// own OnTriggerExit); the whispers stop here.
        /// </summary>
        private static IEnumerator Out(string[] a, Result r)
        {
            if (_et == null)
            {
                r.Fail("no area (whisper-setup first)");
                yield break;
            }
            yield return Walk(
                _et.transform.position + new Vector3(0f, 0f, -700f), 80f);
            float until = Time.realtimeSinceStartup + 5f;
            while ((ScriptedEvents.OurPlayerInside(_et) || Playing())
                && Time.realtimeSinceStartup < until)
            {
                yield return new WaitForSeconds(0.2f);
            }
            r.Info =
                $"in the area: {ScriptedEvents.OurPlayerInside(_et)} (entered {_et.entered}, left {_et.exited}); {_sound} playing {Playing()}";
            var box = _et.gameObject.GetComponent<BoxCollider>();
            if (box != null)
            {
                Object.Destroy(box);
            }
            if (ScriptedEvents.OurPlayerInside(_et))
            {
                r.Fail($"still in the area: {r.Info}");
            }
            else if (Playing())
            {
                r.Fail($"the whispers go on here: {r.Info}");
            }
            yield return Walk(Centre, 60f);
        }
    }
}
