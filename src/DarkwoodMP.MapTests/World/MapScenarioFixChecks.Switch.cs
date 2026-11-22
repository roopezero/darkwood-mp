using System;
using System.Collections;
using System.Linq;
using DarkwoodMP.Combat;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;
using Object = UnityEngine.Object;

namespace DarkwoodMP.World
{
    /// <summary>Checks of an InteractiveItem's switch.</summary>
    public static partial class MapScenarioFixChecks
    {
        // ---- 28: an InteractiveItem's switch --------------------------------

        private static InteractiveItem _ii;
        private static global::EventTrigger _onT, _offT;

        private static readonly string[] SwitchHosts =
            { "candles_01", "Radio", "Stove" };

        /// <summary>"ii-setup" (both games): make the first of SwitchHosts
        /// found near the centre (a saved object with nothing else to do) a
        /// switch (an InteractiveItem with an on and an off trigger, whose
        /// firing we can see). The test world has none of the game's
        /// own.</summary>
        private static IEnumerator IiSetup(string[] a, Result r)
        {
            SaveableObject host = null;
            foreach (var n in SwitchHosts)
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
                r.Fail($"none of {string.Join(", ", SwitchHosts)} here");
                yield break;
            }

            var tex = Resources.LoadAll<Texture2D>("InteractiveItems/Textures");
            if (tex == null || tex.Length == 0)
            {
                r.Fail("no InteractiveItems textures");
                yield break;
            }

            InteractiveItem ii = host.GetComponent<InteractiveItem>();
            if (ii == null)
            {
                ii = host.gameObject.AddComponent<InteractiveItem>();
            }

            ii.textureOn = tex[0].name;
            int offIndex = 0;
            if (tex.Length > 1)
            {
                offIndex = 1;
            }

            ii.textureOff = tex[offIndex].name;
            ii.switchSound = "";
            ii.doesNotPauseGame = true;
            ii.isOn = false;
            ii.onTrigger =
                MakeTrigger(host.transform, "mapOnTrigger", out _onT);
            ii.offTrigger =
                MakeTrigger(host.transform, "mapOffTrigger", out _offT);
            _ii = ii;
            yield return null;
            r.Info =
                $"{host.name} "
                    + $"{ObjRef.Of(host.gameObject)} "
                    + $"at {Fmt(host.transform.position)} "
                    + $"(textures {ii.textureOn}/{ii.textureOff})";
        }

        private static EventTriggers MakeTrigger(
            Transform parent, string name, out global::EventTrigger t)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var ets = go.AddComponent<EventTriggers>();
            t = new global::EventTrigger
            {
                type = global::EventTrigger.Type.area,
                multipleFire = true
            };

            ets.eventTriggers.Add(t);
            return ets;
        }

        private static void HideSwitchWindow()
        {
            var w = Singleton<UI>.Instance.transform.Find("InteractiveItem");
            if (w != null)
            {
                w.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// "ii-switch on|off": our player switches it (InteractiveItem.switchMe
        /// from its window); its trigger fires here.
        /// </summary>
        private static IEnumerator IiSwitch(string[] a, Result r)
        {
            if (_ii == null)
            {
                r.Fail("no switch (ii-setup first)");
                yield break;
            }

            bool on = a.Length == 0 || a[0] == "on";
            yield return Walk(_ii.transform.position);
            if (_ii.isOn == on)
            {
                string text = "off";
                if (on)
                {
                    text = "on";
                }

                r.Fail($"it's already {text} here");
                yield break;
            }

            _onT.fired = false;
            _offT.fired = false;
            AsPlayer(() => _ii.switchMe());
            HideSwitchWindow();
            yield return new WaitForSeconds(1f);
            global::EventTrigger mine;
            if (on)
            {
                mine = _onT;
            }
            else
            {
                mine = _offT;
            }

            string side = "off";
            if (on)
            {
                side = "on";
            }

            r.Info =
                $"{_ii.name}: on {_ii.isOn}, its {side} trigger fired "
                    + $"{mine.fired}";
            if (_ii.isOn != on)
            {
                r.Fail($"it didn't switch (refused?): {r.Info}");
            }
            else if (!mine.fired)
            {
                r.Fail($"its trigger didn't fire here: {r.Info}");
            }

            yield return Walk(Centre, 60f);
        }

        /// <summary>
        /// "ii-check on|off [--fired|--not-fired]": the other player's switch
        /// reached us; whether its trigger fired here.
        /// </summary>
        private static IEnumerator IiCheck(string[] a, Result r)
        {
            if (_ii == null)
            {
                r.Fail("no switch (ii-setup first)");
                yield break;
            }

            bool on = a.Length == 0 || a[0] == "on";
            float until = Time.realtimeSinceStartup + 4f;
            while (_ii.isOn != on && Time.realtimeSinceStartup < until)
            {
                yield return new WaitForSeconds(0.2f);
            }

            yield return new WaitForSeconds(0.5f);
            global::EventTrigger t;
            if (on)
            {
                t = _onT;
            }
            else
            {
                t = _offT;
            }

            string text = "off";
            if (on)
            {
                text = "on";
            }

            r.Info =
                $"{_ii.name}: on {_ii.isOn}, its {text} trigger fired here "
                    + $"{t.fired}";
            if (_ii.isOn != on)
            {
                r.Fail($"the other's switch didn't reach us: {r.Info}");
            }
            else if (a.Contains("--fired") && !t.fired)
            {
                r.Fail($"its events didn't run here: {r.Info}");
            }
            else if (a.Contains("--not-fired") && t.fired)
            {
                r.Fail($"its events ran here too: {r.Info}");
            }

            _onT.fired = false;
            _offT.fired = false;
        }
    }
}
