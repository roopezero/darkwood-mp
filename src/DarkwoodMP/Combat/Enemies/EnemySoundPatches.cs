using DarkwoodMP.Combat.Enemies;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMP.Combat
{
    /// <summary>
    /// Host: sounds the host's enemies make are played on the clients that see
    /// them.
    /// </summary>
    [HarmonyPatch(typeof(AudioController), "_PlayAsSound")]
    public static class HostEnemySoundPatch
    {
        private static void Postfix(
            string audioID, float volume, Transform parentObj)
        {
            var s = Net.Session.Instance;
            if (parentObj == null || s == null || !s.IsHost
                || s.Peers.Count == 0 || NetEnemies.Instance == null)
            {
                return;
            }

            NetEnemies.Instance.HostSound(parentObj, audioID, volume);
        }
    }

    /// <summary>
    /// Host: an enemy's loop that stops without another taking its place
    /// (CharacterSounds.playIdleLoop with no loop: a dog's aggressive loop is
    /// none, so its defensive one stops as it starts chasing) plays nothing, so
    /// HostEnemySoundPatch doesn't see it: the clients are sent an empty
    /// EnemySound, and their copy's loop stops too.
    /// </summary>
    [HarmonyPatch(
        typeof(CharacterSounds), nameof(CharacterSounds.playIdleLoop))]
    public static class HostLoopStopPatch
    {
        private static void Prefix(
            CharacterSounds __instance, out bool __state) =>
            __state = __instance.idleAudioObject != null;

        private static void Postfix(
            CharacterSounds __instance, string loopName, bool __state)
        {
            bool after = __instance.idleAudioObject != null;
            if (!EnemyLoopRules.Stopped(__state, loopName, after))
            {
                return;
            }

            var s = Net.Session.Instance;
            if (s == null || !s.IsHost || s.Peers.Count == 0
                || NetEnemies.Instance == null)
            {
                return;
            }

            NetEnemies.Instance.HostSound(__instance.transform, "", 0f);
        }
    }
}
