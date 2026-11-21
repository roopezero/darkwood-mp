using System.Collections;
using System.Collections.Generic;
using DarkwoodMP.Combat;
using DarkwoodMP.Net;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;

namespace DarkwoodMP.World.Rolls
{
    public sealed partial class WorldEvents
    {
        // ---- night events ---------------------------------------------------

        private const byte EventFired = 1, ShadowLightsKind = 2;

        /// <summary>
        /// Host: a night event fired (CustomEvent.fire); the joiners play it
        /// too.
        /// </summary>
        internal void SendNightEvent(CustomEvent ev)
        {
            if (!HostSharing || Coop.Guards.Replaying || ev == null)
            {
                return;
            }

            var ns = Singleton<NightScenarios>.Instance;
            Location big = null;
            if (Player.Instance != null)
            {
                big = Player.Instance.whereAmI.bigLocation;
            }

            var w = _session.Begin(MsgType.NightEvent);
            w.W.Write(EventFired);
            string s = "";
            if (ns != null && ns.currentScenario != null)
            {
                s = ns.currentScenario.name;
            }

            w.WriteStr(s);
            w.WriteStr(ev.gameObject.name);
            string sentName = "";
            if (big != null)
            {
                sentName = big.name;
            }

            w.WriteStr(sentName);
            _session.Broadcast(w, Channel.Control, reliable: true);
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
            var w = _session.Begin(MsgType.NightEvent);
            w.W.Write(ShadowLightsKind);
            ObjRef.Of(gen.gameObject).Write(w);
            ObjRef.Of(keep.gameObject).Write(w);
            _session.Broadcast(w, Channel.Control, reliable: true);
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
            if (from != _session.HostId || !WorldShare.InWorld)
            {
                return;
            }

            byte kind = r.R.ReadByte();
            if (kind == EventFired)
            {
                string scenario = r.ReadStr(),
                    name = r.ReadStr(),
                    where = r.ReadStr();
                if (!Follows)
                {
                    return;
                }

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
                Coop.Guards.Replaying = true;
                try
                {
                    ev.fire(force: true);
                }
                finally
                {
                    Coop.Guards.Replaying = false;
                    Presentation--;
                }

                if (WorldActions.Debug)
                {
                    Plugin.Log.LogInfo(
                        $"[world] night event {name} (the host's, at {where}) "
                            + $"played here at {big.name}");
                }
            }
            else if (kind == ShadowLightsKind)
            {
                var g = ObjRef.Read(r);
                var k = ObjRef.Read(r);
                if (!Follows)
                {
                    return;
                }

                var gen = ObjRef.Resolve<Generator>(g);
                var keep = ObjRef.Resolve<Item>(k);
                if (gen == null || keep == null)
                {
                    Plugin.Log.LogWarning(
                        $"Shadow lights: no generator {g} or lamp {k} here");
                    return;
                }

                Coop.Guards.Replaying = true;
                try
                {
                    ShadowLights(gen, keep);
                }
                finally
                {
                    Coop.Guards.Replaying = false;
                }

                if (WorldActions.Debug)
                {
                    Plugin.Log.LogInfo(
                        $"[world] shadow lights: {keep.name} stays on (the "
                            + "host's choice)");
                }
            }
        }

        /// <summary>
        /// Player.shadowLightsTurnOff on the host, with its lamp chosen once
        /// and sent: the chosen lamp comes on, all of the generator's lamps
        /// flicker, and after 8 seconds the others go out for 30.
        /// </summary>
        internal bool HostShadowLights(Player p, bool anyTime = false)
        {
            if (!anyTime
                && (Singleton<CharacterSpawner>.Instance.shadowsRemove
                    || Core.isDay()))
            {
                return false;
            }

            Generator gen = null;
            if (p.whereAmI.bigLocation != null)
            {
                gen = p.whereAmI.bigLocation.generator;
            }

            if (gen == null)
            {
                return false;
            }

            var choices = Lights(gen);
            choices.RemoveAll(l => l == null || l.dontTurnOnByShadowsEvent);
            if (choices.Count == 0)
            {
                return false;
            }

            var keep = choices[UnityEngine.Random.Range(0, choices.Count)];
            ShadowLights(gen, keep);
            SendShadowLights(gen, keep);
            return true;
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
                    l.itemLight.flicker_short(8f);
                }
            }

            lights.Remove(keep);
            Singleton<Controller>.Instance.Invoke(delegate
            {
                foreach (var l in lights)
                {
                    if (l != null)
                    {
                        l.empDisable(30f);
                    }
                }

                if (keep != null)
                {
                    keep.enable();
                }
            }, 8f, timeScaleDependent: true);
        }

        /// <summary>
        /// The GameEvent steps a joiner runs when it plays the host's night
        /// event: what's seen and heard.
        /// </summary>
        internal static bool PresentationOnly(GameEvent e)
        {
            switch (e.type)
            {
                case GameEvent.Type.sound:
                case GameEvent.Type.changeIdleSound:
                case GameEvent.Type.playSound_NIEUZYWAJ:
                case GameEvent.Type.particle:
                case GameEvent.Type.cameraEffect:
                case GameEvent.Type.tweenColor:
                case GameEvent.Type.playAnim:
                case GameEvent.Type.displayMessage:
                case GameEvent.Type.debug:
                    return true;
                case GameEvent.Type.fireEvent:
                    // other GameEvents (filtered the same way), not a world
                    // event
                    return !e.activeModifier;
                default:
                    return false;
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
        internal static IEnumerator AsPresentation(IEnumerator inner)
        {
            while (true)
            {
                // the first step runs at once, inside OnNightEvent's own replay
                bool more, was = Coop.Guards.Replaying;
                Presentation++;
                Coop.Guards.Replaying = true;
                try
                {
                    more = inner.MoveNext();
                }
                finally
                {
                    Coop.Guards.Replaying = was;
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
