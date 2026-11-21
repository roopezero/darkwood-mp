using HarmonyLib;

namespace DarkwoodMP.World.Fuel
{
    /// <summary>
    /// The local player pours fuel into a generator (Fuel.Poured).
    /// </summary>
    [HarmonyPatch(typeof(Generator), nameof(Generator.addFuel))]
    static class GeneratorAddFuel
    {
        static void Prefix(Generator __instance, out float __state)
        {
            __state = __instance.fuel;
        }

        static void Postfix(Generator __instance, float __state)
        {
            CoopSession coop = Coop.Current;
            if (coop != null)
            {
                coop.Fuel.Poured(__instance, __instance.fuel - __state);
            }
        }
    }

    /// <summary>
    /// The local player pours fuel into a saw (Fuel.Poured).
    /// </summary>
    [HarmonyPatch(typeof(Saw), nameof(Saw.addFuel))]
    static class SawAddFuel
    {
        static void Prefix(Saw __instance, out float __state)
        {
            __state = __instance.fuel;
        }

        static void Postfix(Saw __instance, float __state)
        {
            CoopSession coop = Coop.Current;
            if (coop != null)
            {
                coop.Fuel.Poured(__instance, __instance.fuel - __state);
            }
        }
    }

    /// <summary>
    /// Cutting a log (Saw.convert) uses fuelToConvert of the saw's fuel; it
    /// goes out like a pour.
    /// </summary>
    [HarmonyPatch(typeof(Saw), nameof(Saw.convert))]
    static class SawConvert
    {
        // A finalizer, not a postfix: the fuel is taken before Saw.refresh,
        // which throws when the saw's window isn't set up (a test calling it
        // directly).
        static void Prefix(Saw __instance, out float __state)
        {
            __state = __instance.fuel;
        }

        static System.Exception Finalizer(
            Saw __instance, System.Exception __exception, float __state)
        {
            CoopSession coop = Coop.Current;
            if (coop != null)
            {
                coop.Fuel.Poured(__instance, __instance.fuel - __state);
            }

            return __exception;
        }
    }

    /// <summary>
    /// Every generator the game starts, also alone (ModHost.Generators).
    /// </summary>
    [HarmonyPatch(typeof(Generator), "Start")]
    static class GeneratorStart
    {
        static void Postfix(Generator __instance)
        {
            Plugin.Instance.Host.Generators.Add(__instance);
        }
    }

    /// <summary>
    /// Fuel just poured goes out before the switch-on does (ItemOnPatch sends
    /// it in its postfix).
    /// </summary>
    [HarmonyPatch(typeof(Item), nameof(Item.turnOn))]
    static class ItemTurnOn
    {
        [HarmonyPriority(Priority.First)]
        static void Prefix()
        {
            CoopSession coop = Coop.Current;
            if (coop != null)
            {
                coop.Fuel.BeforeOn();
            }
        }
    }

    /// <summary>
    /// Whose electricity skill burns a generator's fuel
    /// (GeneratorDrain).
    /// </summary>
    [HarmonyPatch(typeof(Generator), nameof(Generator.drainFuel))]
    static class GeneratorDrainFuel
    {
        static void Prefix(Generator __instance, out float? __state)
        {
            __state = null;
            CoopSession coop = Coop.Current;
            if (coop != null)
            {
                __state = coop.Fuel.Drain.Before(__instance);
            }
        }

        static System.Exception Finalizer(
            System.Exception __exception, float? __state)
        {
            GeneratorDrain.After(__state);
            return __exception;
        }
    }
}
