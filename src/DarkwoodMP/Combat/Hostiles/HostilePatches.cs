using HarmonyLib;

namespace DarkwoodMP.Combat
{
    /// <summary>
    /// Client: a scripted attack by one of our NPCs is the host's to make
    /// (NpcHostility.ClientAttack). Alone, the game's own runs.
    /// </summary>
    [HarmonyPatch(typeof(Character), nameof(Character.attackPlayer))]
    static class CharacterAttackPlayer
    {
        [HarmonyPriority(Priority.First)]
        static bool Prefix(Character __instance) => !HostileNpcs.ClientAttack(
            __instance);
    }
}
