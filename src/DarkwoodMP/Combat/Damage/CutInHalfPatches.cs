using HarmonyLib;

namespace DarkwoodMP.Combat
{
    /// <summary>
    /// The game's own Character.cutMeInHalf (a chomper cut by a blade) destroys
    /// the upper half's Rigidbody but leaves it active until its Cut_half
    /// animation ends, so Character.Update reads the destroyed body's position
    /// every frame of that animation and throws (about 40
    /// NullReferenceExceptions per cut, in the game without the mod too). The
    /// exception only ended that frame's Update early; it's dropped for a
    /// character being cut in half.
    /// </summary>
    [HarmonyPatch(typeof(Character), nameof(Character.Update))]
    static class CharacterUpdateCutInHalf
    {
        static System.Exception Finalizer(
            Character __instance, System.Exception __exception) =>
            CutsInHalf.Thrown(__instance, __exception);
    }
}
