using HarmonyLib;
using UnityEngine;

namespace DarkwoodMP.Combat
{
    /// <summary>
    /// Host: sounds the host's enemies make are played on the clients that see
    /// them.
    /// </summary>
    [HarmonyPatch(typeof(AudioController), "_PlayAsSound")]
    public static class AudioControllerPlayAsSound
    {
        private static void Postfix(
            string audioID, float volume, Transform parentObj)
        {
            CoopSession coop = Coop.Current;
            if (coop != null)
            {
                coop.Enemies.SoundPlayed(parentObj, audioID, volume);
            }
        }
    }

    /// <summary>
    /// Host: an enemy's loop that stops without another taking its place
    /// (CharacterSounds.playIdleLoop with no loop: a dog's aggressive loop is
    /// none, so its defensive one stops as it starts chasing) plays nothing, so
    /// AudioControllerPlayAsSound doesn't see it: the clients are sent an empty
    /// EnemySound, and their copy's loop stops too.
    /// </summary>
    [HarmonyPatch(
        typeof(CharacterSounds), nameof(CharacterSounds.playIdleLoop))]
    public static class CharacterSoundsPlayIdleLoopStop
    {
        private static void Prefix(
            CharacterSounds __instance, out bool __state) =>
            __state = NetEnemies.LoopPlaying(__instance);

        private static void Postfix(
            CharacterSounds __instance, string loopName, bool __state)
        {
            CoopSession coop = Coop.Current;
            if (coop != null)
            {
                coop.Enemies.LoopChanged(__instance, loopName, __state);
            }
        }
    }
}
