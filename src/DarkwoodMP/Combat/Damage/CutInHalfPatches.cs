using DarkwoodMP.Combat.Damage;
using HarmonyLib;
using UnityEngine;

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
    static class CutInHalfUpdatePatch
    {
        static System.Exception Finalizer(
            Character __instance, System.Exception __exception)
        {
            if (__exception == null)
            {
                return null;
            }

            if (CutInHalfRules.Drops(FactsOf(__instance, __exception)))
            {
                return null;
            }

            return __exception;
        }

        static CutFacts FactsOf(Character c, System.Exception thrown)
        {
            bool cutting = c != null && c.cuttingInHalf;
            bool body = false;
            if (cutting)
            {
                body = c.GetComponent<Rigidbody>() != null;
            }

            return new CutFacts
            {
                NullReference = thrown is System.NullReferenceException,
                Character = c != null,
                Cutting = cutting,
                Body = body,
            };
        }
    }
}
