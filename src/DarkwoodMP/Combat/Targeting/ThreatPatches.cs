using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMP.Combat
{
    /// <summary>
    /// Damage a player deals to an enemy is threat for that player.
    /// </summary>
    [HarmonyPatch(typeof(Character), nameof(Character.getHit),
        new[]
        {
            typeof(float),
            typeof(Transform),
            typeof(bool),
            typeof(bool),
            typeof(bool),
            typeof(bool),
            typeof(bool),
            typeof(bool),
            typeof(bool)
        })]
    public static class CharacterGetHitThreatPatch
    {
        private static void Prefix(Character __instance, out float __state) =>
            __state = __instance.health;

        private static void Postfix(
            Character __instance, Transform attackerTransform, bool normalHit,
            float __state)
        {
            float dealt = __state - __instance.health;
            // The game splats blood for a normal hit that took health (not the
            // night trader); show it to the clients too.
            if (normalHit && dealt > 0f && !__instance.isNightTrader
                && Net.Session.Instance != null && Net.Session.Instance.IsHost)
            {
                float y;
                if (attackerTransform != null)
                {
                    y = (attackerTransform.rotation * Quaternion.Euler(
                        90f, 180f, 0f)).eulerAngles.y;
                }
                else
                {
                    y = __instance.transform.eulerAngles.y;
                }

                NetEnemies instance = NetEnemies.Instance;
                if (instance != null)
                {
                    instance.HostBlood(__instance, y);
                }
            }

            if (!Threat.Enabled || attackerTransform == null)
            {
                return;
            }

            if (dealt > 0f)
            {
                Threat.OnDamaged(
                    __instance, attackerTransform.GetComponent<CharBase>(),
                    dealt);
            }
        }
    }

    /// <summary>
    /// Around each sight check: order the candidates by threat before it,
    /// credit being seen after it.
    /// </summary>
    [HarmonyPatch(typeof(Character), "canSeeEnemy")]
    public static class CharacterSightThreatPatch
    {
        private static void Prefix(Character __instance) =>
            Threat.BeforeSightCheck(__instance);
        private static void Postfix(Character __instance) =>
            Threat.AfterSightCheck(__instance);
    }

    /// <summary>Noise is threat for the player who made it.</summary>
    [HarmonyPatch(typeof(Character), nameof(Character.heardSound))]
    public static class CharacterHearingThreatPatch
    {
        private static void Prefix(
            Character __instance, Vector3 Position, float volume)
        {
            if (Threat.Enabled && !__instance.deaf && __instance.alive)
            {
                Threat.OnHeard(__instance, Position, volume);
            }
        }
    }

    /// <summary>
    /// "Attack the player" (scripted attacks, spawns that come for the player)
    /// means the player with the most threat.
    /// </summary>
    [HarmonyPatch(typeof(Character), nameof(Character.attackPlayer))]
    static class AttackPlayerThreatPatch
    {
        static bool Prefix(Character __instance)
        {
            if (!Threat.Enabled || __instance.dummy)
            {
                return true;
            }

            // An NPC a joiner's event sends after "the player" (a dialogue's
            // outcome, run here for them) goes for them.
            NetPlayerProxy requester = null;
            CoopSession coop = Coop.Current;
            if (__instance.npc != null && coop != null)
            {
                requester = coop.Authority.Requester;
            }

            if (requester != null)
            {
                HostileNpcs.SendAfter(__instance, requester);
                return false;
            }

            var best = Threat.BestPlayerFor(__instance);
            if (best == null || best is Player)
            {
                return true; // the host's player: as the game does it
            }

            JoinerAi.GoFor(__instance, best.transform);
            if (Threat.Debug)
            {
                Plugin.Log.LogInfo(
                    $"[threat] {__instance.name} sent after the player: goes "
                        + $"for {((NetPlayerProxy)best).OwnerName}");
            }

            return false;
        }
    }

    /// <summary>
    /// Enemies that pursue the player constantly aim at
    /// Player.Instance.transform in their sight check. With others in the
    /// world, that's the player they have the most threat for: the transpiler
    /// swaps exactly that (Player.Instance followed by .transform) for
    /// Threat.PursuitTarget(this). The check's other uses of the host's player
    /// (its wards) are about the host's player and stay.
    /// </summary>
    [HarmonyPatch(typeof(Character), "canSeeEnemy")]
    static class ThreatPursuitTranspiler
    {
        static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions) =>
            JoinerAi.Swap(
                instructions, JoinerAi.HostThen(
                    AccessTools.PropertyGetter(
                        typeof(Component), nameof(Component.transform))),
                AccessTools.Method(
                    typeof(Threat), nameof(Threat.PursuitTarget)),
                "Character.canSeeEnemy",
                none: "ThreatPursuitTranspiler: no Player.Instance.transform "
                    + "in canSeeEnemy (game changed?)");
    }

    /// <summary>
    /// The game's enemy code measures "the player" as
    /// Player.Instance._transform.position: on the host that's only the host's
    /// own player. These transpilers swap exactly that read (Player.Instance,
    /// ._transform, .position) for a helper that knows the other players, in
    /// the methods listed below. With nobody else in the world the helpers
    /// return the host's player, so single player is unchanged.
    /// </summary>
    static class PlayerPositionTranspiler
    {
        /// <param name="characterArg">The argument that holds the Character: 0
        /// (`this`) in Character's own methods.</param>
        public static IEnumerable<CodeInstruction> Swap(
            IEnumerable<CodeInstruction> instructions,
            System.Reflection.MethodInfo helper, string where,
            int characterArg = 0) =>
            JoinerAi.Swap(
                instructions, HostPosition, helper, where, arg: characterArg,
                none: "PlayerPositionTranspiler: no "
                    + $"Player.Instance._transform.position in {where} (game "
                    + "changed?); it still uses the host's player");

        private static readonly System.Reflection.FieldInfo PlayerTransform =
            AccessTools.Field(typeof(Player), "_transform");
        private static readonly System.Reflection.MethodInfo GetPosition =
            AccessTools.PropertyGetter(
                typeof(Transform), nameof(Transform.position));

        /// <summary>Player.Instance._transform.position</summary>
        private static int HostPosition(List<CodeInstruction> l, int i)
        {
            if (i + 2 < l.Count && l[i].Calls(JoinerAi.GetInstance)
                && l[i + 1].LoadsField(PlayerTransform)
                && l[i + 2].Calls(GetPosition))
            {
                return 3;
            }

            return 0;
        }

        public static System.Reflection.MethodInfo Nearest =>
            AccessTools.Method(typeof(Threat), nameof(Threat.NearestPlayerPos));
        public static System.Reflection.MethodInfo Flee =>
            AccessTools.Method(typeof(Threat), nameof(Threat.FleeFrom));
    }

    /// <summary>
    /// Host: a spawned enemy (temporarySpawned, beyond 3500) or one on its way
    /// out (wantToDespawn, beyond 1500) is removed by its distance to the
    /// nearest player, not to the host's, so one chasing a far-off joiner
    /// stays.
    /// </summary>
    [HarmonyPatch(typeof(Character), "checkStuff")]
    static class DespawnByNearestPlayerPatch
    {
        static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions) =>
            PlayerPositionTranspiler.Swap(
                instructions, PlayerPositionTranspiler.Nearest,
                "Character.checkStuff");
    }
}
