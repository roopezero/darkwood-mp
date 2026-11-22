using System;
using System.Collections.Generic;
using DarkwoodMP.Combat.Joiners;
using DarkwoodMP.Combat.Targeting;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMP.Combat
{
    [HarmonyPatch(typeof(Character), "waitToTeleport")]
    static class CharacterWaitToTeleport
    {
        static bool Prefix(Character __instance)
        {
            return JoinerSight.WaitToTeleport(__instance);
        }
    }

    [HarmonyPatch(typeof(Character), "checkIfInSightOfPlayer")]
    static class CharacterCheckIfInSightOfPlayer
    {
        static bool Prefix(Character __instance)
        {
            return JoinerSight.CheckIfInSight(__instance);
        }
    }

    [HarmonyPatch(typeof(Character), "onBansheeOutOfSightOfPlayer")]
    static class CharacterOnBansheeOutOfSightOfPlayer
    {
        static void Postfix(Character __instance)
        {
            JoinerSight.OutOfSight(__instance);
        }
    }
}
