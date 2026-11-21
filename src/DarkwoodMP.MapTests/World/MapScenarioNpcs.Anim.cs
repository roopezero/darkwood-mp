using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;

namespace DarkwoodMP.World
{
    /// <summary>A world event from a character's animation.</summary>
    public static partial class MapScenarioNpcs
    {
        // ---- a world event from a character's animation ---------------------
        // The musician under the tractor.

        /// <summary>The world event the anim-* steps make: it switches the
        /// NPC's talking back on, as the musician's musician_setUnderTractor
        /// does for the tractor's NPC (tractor_muzyk).</summary>
        private const string AnimEvent = "dwmp_test_animWakesNpc";
        private static (NPC npc, GameEvents ge,
            AnimationTriggerListener added) _anim;

        /// <summary>"anim-setup NAME DX DZ" (host and client): our NPC of that
        /// name nearest centre + (DX, DZ) is switched off for talking
        /// (NPC.disabled), and a world event is made that switches it on again
        /// with the game's own step (GameEvent modifyComponent NPC.disabled),
        /// as the tractor's NPC waits for the musician's hiding animation. The
        /// NPC gets an AnimationTriggerListener if it has none, for anim-fire
        /// to fire the event from its animation.</summary>
        private static IEnumerator AnimSetup(string[] a, Result r)
        {
            AnimEnd();
            var npc = Shared(a[0], Centre + new Vector3(F(a[1]), 0f, F(a[2])));
            if (npc == null)
            {
                r.Fail($"no {a[0]} here that every game has");
                yield break;
            }

            var go = new GameObject(AnimEvent);
            go.transform.position = npc.transform.position;
            var ge = go.AddComponent<GameEvents>();
            ge.isWorldEvent = true;
            ge.worldEventType = AnimEvent;
            ge.events = new List<GameEvent>
            {
                new GameEvent
                {
                    type = GameEvent.Type.modifyComponent, Value = "NPC",
                    Value2 = "disabled",
                    // sets the bool field to activeModifier2
                    relentlessPursuit = true, activeModifier2 = false,
                    targetGameObjects = new List<GameObject> { npc.gameObject },
                },
            };

            Singleton<Events>.Instance.addWorldEvent(AnimEvent, ge);
            var listener = npc.GetComponent<AnimationTriggerListener>();
            AnimationTriggerListener added = null;
            if (listener == null)
            {
                added = npc.gameObject.AddComponent<AnimationTriggerListener>();
            }

            _anim = (npc, ge, added);
            npc.disabled = true;
            yield return null;
            r.Info =
                $"{npc.name} "
                    + $"{ObjRef.Of(npc.gameObject)} "
                    + $"at {Fmt(npc.transform.position)}: "
                    + "switched off for talking";
        }

        /// <summary>"anim-fire left|run": our NPC's animation fires the world
        /// event (the frame trigger's own call,
        /// AnimationTriggerListener.checkFrameTrigger of type fireWorldEvent).
        /// "left" (client): it doesn't run here, it's the host's (the NPC stays
        /// switched off for a second). "run" (host): it runs here (the NPC is
        /// switched on).</summary>
        private static IEnumerator AnimFire(string[] a, Result r)
        {
            var (npc, ge, _) = _anim;
            if (npc == null || ge == null)
            {
                r.Fail("no anim-setup");
                yield break;
            }

            var listener = npc.GetComponent<AnimationTriggerListener>();
            listener.checkFrameTrigger(
                AnimEvent, 0, 0f,
                tk2dSpriteAnimationFrame.Trigger.Type.fireWorldEvent,
                Vector3.zero, Vector3.zero, null, "", false);
            bool run = a.Length > 0 && a[0] == "run";
            float wait = 1.5f;
            if (run)
            {
                wait = 5f;
            }

            float until = Time.realtimeSinceStartup + wait;
            while (Time.realtimeSinceStartup < until && npc.disabled)
            {
                yield return new WaitForSecondsRealtime(0.25f);
            }

            string text = "can be talked to";
            if (npc.disabled)
            {
                text = "switched off";
            }

            string ran = "didn't run";
            if (ge.fired)
            {
                ran = "ran";
            }

            r.Info = $"{npc.name}: {text}, the event {ran} here";
            bool condition;
            if (run)
            {
                condition = npc.disabled || !ge.fired;
            }
            else
            {
                condition = !npc.disabled || ge.fired;
            }

            if (condition)
            {
                r.Fail(r.Info);
            }
        }

        /// <summary>"anim-check" (client): the host's run of the event reached
        /// us: our NPC can be talked to as the game asks it before talking
        /// (wantsToTalk and not disabled; waiting up to 10 s).</summary>
        private static IEnumerator AnimCheck(string[] a, Result r)
        {
            var (npc, ge, _) = _anim;
            if (npc == null || ge == null)
            {
                r.Fail("no anim-setup");
                yield break;
            }

            float until = Time.realtimeSinceStartup + 10f;
            while (Time.realtimeSinceStartup < until && npc.disabled)
            {
                yield return new WaitForSecondsRealtime(0.25f);
            }

            string name = npc.name;
            bool wants = npc.wantsToTalk;
            bool disabled = npc.disabled;
            string ran = "didn't run";
            if (ge.fired)
            {
                ran = "ran";
            }

            r.Info =
                $"{name}: wantsToTalk {wants}, disabled {disabled}, the event "
                    + $"{ran} here";
            if (npc.disabled || !npc.wantsToTalk || !ge.fired)
            {
                r.Fail(r.Info);
            }
        }

        /// <summary>
        /// "anim-end": the test's world event and listener are removed, and the
        /// NPC can be talked to.
        /// </summary>
        private static void AnimEnd()
        {
            var (npc, ge, added) = _anim;
            _anim = default;
            if (ge != null)
            {
                // GameEvents.OnDestroy takes it out of the world events
                UnityEngine.Object.Destroy(ge.gameObject);
            }

            if (added != null)
            {
                UnityEngine.Object.Destroy(added);
            }

            if (npc != null)
            {
                npc.disabled = false;
            }
        }
    }
}
