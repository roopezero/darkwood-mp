using HarmonyLib;

namespace DarkwoodMP.World
{
    // The NPC death patches (NpcDeaths). Each does nothing with no
    // session: playing alone is the game's own.

    /// <summary>A character died: an NPC's death is shared.</summary>
    [HarmonyPatch(typeof(Character), nameof(Character.die))]
    static class Die
    {
        static void Prefix(Character __instance, out bool __state)
        {
            __state = false;
            var coop = Coop.Current;
            if (coop == null)
            {
                return;
            }

            __state = coop.NpcDeaths.BeforeDie(__instance);
        }

        static void Postfix(Character __instance, bool __state)
        {
            var coop = Coop.Current;
            if (coop == null)
            {
                return;
            }

            coop.NpcDeaths.AfterDie(__instance, __state);
        }
    }

    /// <summary>
    /// Killing the night trader sends the killer into a dream
    /// (`Character.die`'s isNightTrader branch calls
    /// `Player.special_onKillNightTrader`). When the death is replayed from
    /// another game, that dream belongs to the killer, not to us: skip it.
    /// The branch's flag (nightTrader_killedYesterday) and `endAfterNight`
    /// still run.
    /// </summary>
    [HarmonyPatch(typeof(Player), nameof(Player.special_onKillNightTrader))]
    static class SpecialOnKillNightTrader
    {
        static bool Prefix()
        {
            var coop = Coop.Current;
            if (coop == null)
            {
                return true;
            }

            return coop.NpcDeaths.RunsNightTraderDream();
        }
    }
}
