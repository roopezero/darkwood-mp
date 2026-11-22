using DarkwoodMP.Combat.Damage;
using UnityEngine;

namespace DarkwoodMP.Combat
{
    /// <summary>
    /// What Character.Update threw, for a character being cut in half: the
    /// NullReferenceException from its destroyed Rigidbody is dropped (see
    /// CharacterUpdateCutInHalf). It holds no state and runs with no session
    /// too: the game throws it without the mod as well.
    /// </summary>
    internal static class CutsInHalf
    {
        public static System.Exception Thrown(
            Character c,
            System.Exception thrown)
        {
            if (thrown == null)
            {
                return null;
            }

            if (CutInHalfRules.Drops(FactsOf(c, thrown)))
            {
                return null;
            }

            return thrown;
        }

        private static CutFacts FactsOf(Character c, System.Exception thrown)
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
