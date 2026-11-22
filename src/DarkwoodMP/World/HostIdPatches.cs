using HarmonyLib;

namespace DarkwoodMP.World
{
    /// <summary>
    /// A saveable object started: the host may give it an id (HostIds).
    /// </summary>
    [HarmonyPatch(typeof(SaveableObject), nameof(SaveableObject.init))]
    static class SaveableObjectInit
    {
        static void Prefix(SaveableObject __instance, ref bool __state) =>
            __state = HostIds.BeforeInit(__instance);

        static void Postfix(SaveableObject __instance, bool __state) =>
            HostIds.AfterInit(__instance, __state);
    }
}
