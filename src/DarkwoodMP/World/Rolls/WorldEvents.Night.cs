using System.Collections;
using System.Collections.Generic;
using DarkwoodMP.Net;
using UnityEngine;

namespace DarkwoodMP.World.Rolls
{
    internal sealed partial class WorldEvents
    {
        // ---- night events ---------------------------------------------------

        /// <summary>
        /// Host: a night event fired (CustomEvent.fire); the joiners play it
        /// too.
        /// </summary>
        internal void SendNightEvent(CustomEvent ev)
        {
            if (!HostSharing || _coop.Guards.Replaying || ev == null)
            {
                return;
            }

            var ns = Singleton<NightScenarios>.Instance;
            Location big = null;
            if (Player.Instance != null)
            {
                big = Player.Instance.whereAmI.bigLocation;
            }

            string s = "";
            if (ns != null && ns.currentScenario != null)
            {
                s = ns.currentScenario.name;
            }

            string sentName = "";
            if (big != null)
            {
                sentName = big.name;
            }

            var w = Net.Begin(MsgType.NightEvent);
            var fired = new NightFired
            {
                Scenario = s,
                Event = ev.gameObject.name,
                Where = sentName,
            };

            NightEventMessage.WriteFired(w, fired);
            Net.Broadcast(w, Channel.Control, reliable: true);
            if (WorldActions.Debug)
            {
                string shownName = "nowhere";
                if (big != null)
                {
                    shownName = big.name;
                }

                Plugin.Log.LogInfo(
                    $"[world] sent night event {ev.gameObject.name} at "
                        + $"{shownName}");
            }
        }

        private void SendShadowLights(Generator gen, Item keep)
        {
            var w = Net.Begin(MsgType.NightEvent);
            NightEventMessage.WriteShadowLights(
                w,
                ObjRef.NameOf(ObjRef.Of(gen.gameObject)),
                ObjRef.NameOf(ObjRef.Of(keep.gameObject)));
            Net.Broadcast(w, Channel.Control, reliable: true);
            if (WorldActions.Debug)
            {
                Plugin.Log.LogInfo(
                    $"[world] sent shadow lights: {keep.name} stays on "
                        + $"({gen.name})");
            }
        }

        internal static CustomEvent FindEvent(NightScenario sc, string name)
        {
            foreach (var e in sc.customEventAndInts)
            {
                if (e != null && e.customEvent != null
                    && e.customEvent.gameObject.name == name)
                {
                    return e.customEvent;
                }
            }

            foreach (var e in sc.customEvents)
            {
                if (e != null && e.gameObject.name == name)
                {
                    return e;
                }
            }

            return null;
        }

        private void OnNightEvent(ulong from, PacketReader r)
        {
            if (!FromHost(from))
            {
                return;
            }

            NightEventBody body = NightEventMessage.Read(r);
            if (body.Kind == NightEventMessage.Fired)
            {
                if (Follows)
                {
                    PlayNightEvent(body.Fired);
                }
            }
            else if (body.Kind == NightEventMessage.ShadowLights)
            {
                if (Follows)
                {
                    PlayShadowLights(body);
                }
            }
        }

        /// <summary>
        /// A joiner plays the host's night event at its own big location,
        /// presentation only.
        /// </summary>
        private void PlayNightEvent(NightFired sent)
        {
            string scenario = sent.Scenario;
            string name = sent.Event;
            string where = sent.Where;
            var ns = Singleton<NightScenarios>.Instance;
            NightScenario sc = null;
            if (ns != null)
            {
                sc = ns.getScenario(scenario);
            }

            CustomEvent ev = null;
            if (sc != null)
            {
                ev = FindEvent(sc, name);
            }

            if (ev == null)
            {
                Plugin.Log.LogWarning(
                    $"No night event {scenario}/{name} here");
                return;
            }

            var big = Player.Instance.whereAmI.bigLocation;
            if (big == null)
            {
                if (WorldActions.Debug)
                {
                    Plugin.Log.LogInfo(
                        $"[world] night event {name} (the host's, at "
                            + $"{where}): not at a location here, not "
                            + "played");
                }

                return;
            }

            ns.currentScenario = sc;
            Presentation++;
            _coop.Guards.Replaying = true;
            try
            {
                ev.fire(force: true);
            }
            finally
            {
                _coop.Guards.Replaying = false;
                Presentation--;
            }

            if (WorldActions.Debug)
            {
                Plugin.Log.LogInfo(
                    $"[world] night event {name} (the host's, at {where}) "
                        + $"played here at {big.name}");
            }
        }

        /// <summary>
        /// A joiner's shadows' lights, with the lamp the host chose.
        /// </summary>
        private void PlayShadowLights(NightEventBody sent)
        {
            var g = ObjRef.RefOf(sent.Generator);
            var k = ObjRef.RefOf(sent.Lamp);
            var gen = ObjRef.Resolve<Generator>(g);
            var keep = ObjRef.Resolve<Item>(k);
            if (gen == null || keep == null)
            {
                Plugin.Log.LogWarning(
                    $"Shadow lights: no generator {g} or lamp {k} here");
                return;
            }

            _coop.Guards.Replaying = true;
            try
            {
                ShadowLights(gen, keep);
            }
            finally
            {
                _coop.Guards.Replaying = false;
            }

            if (WorldActions.Debug)
            {
                Plugin.Log.LogInfo(
                    $"[world] shadow lights: {keep.name} stays on (the "
                        + "host's choice)");
            }
        }

        /// <summary>
        /// Player.shadowLightsTurnOff on the host, at night while the shadows
        /// are there (ShadowRules.Rolls).
        /// </summary>
        internal bool HostShadowLights(Player p)
        {
            bool removed = Singleton<CharacterSpawner>.Instance.shadowsRemove;
            if (!ShadowRules.Rolls(false, removed, Core.isDay()))
            {
                return false;
            }

            return TurnShadowLightsOff(p);
        }

        /// <summary>
        /// The shadows' lights at any time, for the world test.
        /// </summary>
        internal bool ShadowLightsAnyTime(Player p)
        {
            return TurnShadowLightsOff(p);
        }

        /// <summary>
        /// The shadows' lights with their lamp chosen once and sent: the
        /// chosen lamp comes on, all of the generator's lamps flicker, and
        /// after ShadowRules.FlickerFor the others go out.
        /// </summary>
        private bool TurnShadowLightsOff(Player p)
        {
            Generator gen = null;
            if (p.whereAmI.bigLocation != null)
            {
                gen = p.whereAmI.bigLocation.generator;
            }

            if (gen == null)
            {
                return false;
            }

            var lamps = gen.powerItems;
            var choices = ShadowRules.Choices(FactsOf(gen));
            if (choices.Count == 0)
            {
                return false;
            }

            int roll = UnityEngine.Random.Range(0, choices.Count);
            var keep = lamps[choices[roll]];
            ShadowLights(gen, keep);
            SendShadowLights(gen, keep);
            return true;
        }

        /// <summary>The generator's power items, as lamps.</summary>
        private static List<LampFacts> FactsOf(Generator gen)
        {
            var own = gen.GetComponent<Item>();
            var lamps = new List<LampFacts>(gen.powerItems.Count);
            foreach (var l in gen.powerItems)
            {
                bool missing = l == null;
                bool dark = false;
                if (!missing)
                {
                    dark = l.dontTurnOnByShadowsEvent;
                }

                lamps.Add(new LampFacts
                {
                    Own = own != null && l == own,
                    Missing = missing,
                    KeptDark = dark,
                });
            }

            return lamps;
        }

        private static List<Item> Lights(Generator gen)
        {
            var lights = new List<Item>(gen.powerItems);
            lights.Remove(gen.GetComponent<Item>());
            return lights;
        }

        /// <summary>
        /// The game's shadow lights effect, with the lamp that stays on given.
        /// </summary>
        internal static void ShadowLights(Generator gen, Item keep)
        {
            var lights = Lights(gen);
            if (lights.Count == 0)
            {
                return;
            }

            keep.turnOn();
            foreach (var l in lights)
            {
                if (l == null)
                {
                    continue;
                }

                l.enable();
                if (l.itemLight != null)
                {
                    l.itemLight.flicker_short(ShadowRules.FlickerFor);
                }
            }

            lights.Remove(keep);
            Singleton<Controller>.Instance.Invoke(delegate
            {
                foreach (var l in lights)
                {
                    if (l != null)
                    {
                        l.empDisable(ShadowRules.DarkFor);
                    }
                }

                if (keep != null)
                {
                    keep.enable();
                }
            }, ShadowRules.FlickerFor, timeScaleDependent: true);
        }

        /// <summary>
        /// The GameEvent steps a joiner runs when it plays the host's night
        /// event: what's seen and heard (RollRules.Presents).
        /// </summary>
        internal static bool PresentationOnly(GameEvent e)
        {
            return RollRules.Presents(StepOf(e.type), e.activeModifier);
        }

        private static NightStep StepOf(GameEvent.Type type)
        {
            switch (type)
            {
                case GameEvent.Type.sound:
                    return NightStep.Sound;
                case GameEvent.Type.changeIdleSound:
                    return NightStep.ChangeIdleSound;
                case GameEvent.Type.playSound_NIEUZYWAJ:
                    return NightStep.PlaySoundUnused;
                case GameEvent.Type.particle:
                    return NightStep.Particle;
                case GameEvent.Type.cameraEffect:
                    return NightStep.CameraEffect;
                case GameEvent.Type.tweenColor:
                    return NightStep.TweenColor;
                case GameEvent.Type.playAnim:
                    return NightStep.PlayAnim;
                case GameEvent.Type.displayMessage:
                    return NightStep.DisplayMessage;
                case GameEvent.Type.debug:
                    return NightStep.Debug;
                case GameEvent.Type.fireEvent:
                    return NightStep.FireEvent;
                default:
                    return NightStep.Other;
            }
        }

        internal static IEnumerator Nothing()
        {
            yield break;
        }

        /// <summary>
        /// Runs a played event's step with Presentation and Replaying set, so
        /// what it fires later is filtered too.
        /// </summary>
        internal IEnumerator AsPresentation(IEnumerator inner)
        {
            while (true)
            {
                // the first step runs at once, inside OnNightEvent's own replay
                bool more, was = _coop.Guards.Replaying;
                Presentation++;
                _coop.Guards.Replaying = true;
                try
                {
                    more = inner.MoveNext();
                }
                finally
                {
                    _coop.Guards.Replaying = was;
                    Presentation--;
                }

                if (!more)
                {
                    yield break;
                }

                yield return inner.Current;
            }
        }
    }
}
