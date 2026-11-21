using HarmonyLib;

namespace DarkwoodMP.World
{
    /// <summary>
    /// "The player has a full oxygen tank" holds for every player once one of
    /// them has filled it (SharedTank). This runs in a game played alone
    /// too: the flag it reads is kept for when others join.
    /// </summary>
    [HarmonyPatch(
        typeof(EventTriggerRequirement),
        nameof(EventTriggerRequirement.requirementsMet))]
    static class TriggerRequirementsMet
    {
        static void Postfix(
            EventTriggerRequirement __instance, ref bool __result)
        {
            Plugin plugin = Plugin.Instance;
            if (plugin != null && plugin.Host != null)
            {
                __result = plugin.Host.Tank.Requirement(__instance, __result);
            }
        }
    }
}
