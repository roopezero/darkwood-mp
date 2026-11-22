using System;
using System.Collections;
using System.Linq;
using DarkwoodMP.Combat;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;
using Object = UnityEngine.Object;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Steps of the two-game map test for players far apart
    /// (tools/tests/map_scenario_far.py): things done where the other game has
    /// the area switched off by its own culling.
    ///
    /// Spots are offsets from the centre, given as "off:DX,DZ" (or "centre"),
    /// so both games pick the same door or object: the one nearest that spot,
    /// inactive ones too (FindObjectsOfType(true)).
    /// </summary>
    public static partial class MapScenarioFarWorld
    {
        private static Player P => Player.Instance;

        public static void Register()
        {
            // Walking far: 600/s, so 7000 takes about 12 s, and 90 s is plenty.
            MapScenario.Add("far-goto", FarGoto, timeout: 90f);
            // Item 3: a door opened where the other game has it switched off.
            MapScenario.Add("far-door", FarDoor, timeout: 60f);
            MapScenario.Add("far-door-check", FarDoorCheck, timeout: 60f);
            MapScenario.Add("far-doors-info", FarDoorsInfo);
            // Item 5: an object dragged where the other game has it switched
            // off.
            MapScenario.Add("far-drag", FarDrag, timeout: 60f);
            MapScenario.Add("far-obj-shown", FarObjShown, timeout: 60f);
            // Item 1: the host's enemy whose copy the client's culling switched
            // off.
            MapScenario.Add("far-dog", FarDog);
            MapScenario.Add("far-dog-active", FarDogActive, timeout: 30f);
            MapScenario.Add("far-dog-to-client", FarDogToClient);
            MapScenario.Add("far-dog-shown", FarDogShown, timeout: 30f);
            MapScenario.Add("far-dog-gone", FarDogGone);
            MapScenario.Add("far-dog-hidden", FarDogHidden, timeout: 30f);
        }

        /// <summary>"off:DX,DZ" (from the centre) or "centre".</summary>
        private static Vector3 Spot(string s)
        {
            if (s == null || s == "centre")
            {
                return Centre;
            }

            if (s.StartsWith("off:"))
            {
                var p = s.Substring(4).Split(',');
                return Centre + new Vector3(F(p[0]), 0f, F(p[1]));
            }

            throw new ArgumentException(
                $"not a spot: {s} (off:DX,DZ or centre)");
        }

        private static bool _locationsLogged;

        /// <summary>"far-goto SPOT": walk there.</summary>
        private static IEnumerator FarGoto(string[] a, Result r)
        {
            if (!_locationsLogged)
            {
                _locationsLogged = true;
                foreach (var l in Object.FindObjectsOfType<Location>(true)
                    .Where(l => l != null && !l.isSubLocation))
                {
                    Plugin.Log.LogInfo(
                        $"[map] location {Core.getTrueLocationName(l.name)} at "
                            + $"{Fmt(l.transform.position)} "
                            + $"({Fmt(l.transform.position - Centre)} from the "
                            + "centre)");
                }
            }

            string s = "centre";
            if (a.Length > 0)
            {
                s = a[0];
            }

            var to = Spot(s);
            yield return Walk(to, 60f);
            r.Info = $"at {Fmt(P.transform.position)}";
        }
    }
}
