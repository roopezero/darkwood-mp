using System.Collections;
using HarmonyLib;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;

namespace DarkwoodMP.World
{
    /// <summary>
    /// The dive's sound: the cellar's dives play water_teleport_01B with no
    /// target, at the listener, as loud wherever the player is. As a shared
    /// step every game played it, so every player heard one player's dive.
    /// Each own-setup trigger plays it the same way; "dive-sounds N" checks
    /// this game played it N times since the last "dive-sounds-zero".
    /// </summary>
    public static partial class MapScenarioDive
    {
        private const string DiveSound = "water_teleport_01B";

        private static int _diveSounds;

        private static GameEvent ListenerSound()
        {
            return new GameEvent
            {
                type = GameEvent.Type.playSound_NIEUZYWAJ,
                Value = DiveSound,
                eventAmount = 1
            };
        }

        private static IEnumerator DiveSoundsZero(string[] a, Result r)
        {
            _diveSounds = 0;
            yield break;
        }

        /// <summary>"dive-sounds N": this game played the dive's sound N
        /// times since dive-sounds-zero.</summary>
        private static IEnumerator DiveSounds(string[] a, Result r)
        {
            int want = int.Parse(a[0]);
            // the cellar's step waits 2 seconds; ours doesn't, but its event
            // may still be on its way from the other game
            float until = Time.realtimeSinceStartup + 3f;
            while (_diveSounds < want && Time.realtimeSinceStartup < until)
            {
                yield return new WaitForSeconds(0.2f);
            }

            yield return new WaitForSeconds(1f);
            r.Info = $"{DiveSound} played {_diveSounds} time(s) here "
                + $"(want {want})";
            if (_diveSounds != want)
            {
                r.Fail(r.Info);
            }
        }

        [HarmonyPatch(typeof(AudioController), "_PlayAsSound")]
        static class DiveSoundPlayed
        {
            static void Postfix(string audioID)
            {
                if (audioID == DiveSound)
                {
                    _diveSounds++;
                }
            }
        }
    }
}
