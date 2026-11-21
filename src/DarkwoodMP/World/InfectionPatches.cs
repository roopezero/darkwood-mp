using HarmonyLib;

namespace DarkwoodMP.World
{
    /// <summary>
    /// A patch told to fade: sent if it's this game's clear (InfectionSync).
    /// </summary>
    [HarmonyPatch(typeof(Infection), nameof(Infection.disappear))]
    static class InfectionDisappear
    {
        static void Prefix(Infection __instance, out bool __state) =>
            __state = InfectionSync.WasFading(__instance);

        static void Postfix(Infection __instance, bool __state)
        {
            CoopSession coop = Coop.Current;
            if (coop == null)
            {
                return;
            }

            coop.Infections.Disappeared(__instance, __state);
        }
    }

    /// <summary>
    /// A patch spreading: the host's, sent; a client's copy never spreads by
    /// itself.
    /// </summary>
    [HarmonyPatch(typeof(Infection), "spawnInfection")]
    static class InfectionSpawnInfection
    {
        static bool Prefix(out bool __state)
        {
            __state = false;
            CoopSession coop = Coop.Current;
            if (coop == null)
            {
                return true;
            }

            __state = coop.Infections.BeginSpread();
            return __state;
        }

        static System.Exception Finalizer(
            System.Exception __exception, bool __state)
        {
            CoopSession coop = Coop.Current;
            if (__state && coop != null)
            {
                coop.Infections.EndGrowing();
            }

            return __exception;
        }
    }

    /// <summary>
    /// A spider laying its eggs: a patch of infection where it stands (the
    /// host's spiders only).
    /// </summary>
    [HarmonyPatch(typeof(Character), "waitToSpawnEgg")]
    static class CharacterWaitToSpawnEgg
    {
        static void Prefix()
        {
            CoopSession coop = Coop.Current;
            if (coop == null)
            {
                return;
            }

            coop.Infections.BeginLaying();
        }

        static System.Exception Finalizer(System.Exception __exception)
        {
            CoopSession coop = Coop.Current;
            if (coop != null)
            {
                coop.Infections.EndGrowing();
            }

            return __exception;
        }
    }

    /// <summary>
    /// A patch started: the host sends the world's new ones.
    /// </summary>
    [HarmonyPatch(typeof(Infection), nameof(Infection.init))]
    static class InfectionInit
    {
        static void Postfix(Infection __instance)
        {
            CoopSession coop = Coop.Current;
            if (coop == null)
            {
                return;
            }

            coop.Infections.Started(__instance);
        }
    }
}
