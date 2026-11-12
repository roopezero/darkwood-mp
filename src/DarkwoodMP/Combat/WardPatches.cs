using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMP.Combat
{
    /// <summary>
    /// The host's enemies check a few things about "the player" that belong to one player: the shadow ward and the
    /// forest spirit ward (Character.canSeeEnemy: an enemy afraid of them runs away), and the Friend / Enemy of the
    /// Forest skills (processAnims, onSeeEnemyNear: how an aggressive animal treats the player it's after). The game
    /// reads them from Player.Instance, the host's player. These transpilers make them read the player the enemy is
    /// after: its target's proxy, with the joiner's own state from their PlayerState (NetPlayerProxy), or the host's
    /// player when the target is the host or not a player at all. With nobody else in the world that is the host's
    /// player, as in the game.
    /// </summary>
    public static class Wards
    {
        /// <summary>The joiner whose proxy the enemy is after, or null (then the host's player counts, as in the game).</summary>
        private static NetPlayerProxy TargetProxy(Character c)
        {
            if (NetPlayerProxy.All.Count == 0 || c == null || c.target == null) return null;
            return c.target.GetComponent<NetPlayerProxy>();
        }

        /// <summary>For Player.Instance.effects.hasEffectType(type) in canSeeEnemy.</summary>
        public static bool HasEffect(Character c, CharacterEffectType type)
        {
            var p = TargetProxy(c);
            if (p != null) return type == CharacterEffectType.shadowWard ? p.ShadowWard : type == CharacterEffectType.forestSpiritWard && p.ForestSpiritWard;
            var host = Player.Instance;
            return host != null && host.effects != null && host.effects.hasEffectType(type);
        }

        /// <summary>For "target == Player.Instance._transform" before a forest skill check: the target, when it is a joiner.</summary>
        public static Transform PlayerTarget(Character c)
        {
            var p = TargetProxy(c);
            return p != null ? p.transform : Player.Instance._transform;
        }

        public static bool FriendOfTheForest(Character c)
        {
            var p = TargetProxy(c);
            return p != null ? p.FriendOfTheForest : Player.Instance.skills.FriendOfTheForest;
        }

        public static bool EnemyOfTheForest(Character c)
        {
            var p = TargetProxy(c);
            return p != null ? p.EnemyOfTheForest : Player.Instance.skills.EnemyOfTheForest;
        }

        private static MethodInfo GetInstance => AccessTools.PropertyGetter(typeof(Player), nameof(Player.Instance));

        /// <summary>Player.Instance.effects.hasEffectType(X) becomes Wards.HasEffect(this, X).</summary>
        public static IEnumerable<CodeInstruction> SwapEffects(IEnumerable<CodeInstruction> instructions)
        {
            var effects = AccessTools.Field(typeof(CharBase), "effects");
            var has = AccessTools.Method(typeof(CharacterEffects), nameof(CharacterEffects.hasEffectType));
            var mine = AccessTools.Method(typeof(Wards), nameof(HasEffect));
            var list = new List<CodeInstruction>(instructions);
            int swapped = 0;
            for (int i = 0; i < list.Count - 3; i++)
            {
                if (!list[i].Calls(GetInstance) || !list[i + 1].LoadsField(effects) || !list[i + 3].Calls(has)) continue;
                list[i].opcode = OpCodes.Ldarg_0; list[i].operand = null; // keeps the instruction's labels
                list[i + 1].opcode = OpCodes.Nop; list[i + 1].operand = null;
                list[i + 3].opcode = OpCodes.Call; list[i + 3].operand = mine;
                swapped++;
            }
            Plugin.Log.Log(swapped == 2 ? BepInEx.Logging.LogLevel.Info : BepInEx.Logging.LogLevel.Warning,
                $"Wards: {swapped} ward checks in Character.canSeeEnemy read the enemy's target (2 expected)");
            return list;
        }

        /// <summary>
        /// Player.Instance.skills.FriendOfTheForest / EnemyOfTheForest becomes Wards.X(this), and the
        /// "target == Player.Instance._transform" just before it (within the same condition) becomes
        /// "target == Wards.PlayerTarget(this)". Other comparisons with the host's player are left alone.
        /// </summary>
        public static IEnumerable<CodeInstruction> SwapForestSkills(IEnumerable<CodeInstruction> instructions, string where)
        {
            var skills = AccessTools.Field(typeof(Player), nameof(Player.skills));
            var friend = AccessTools.Field(typeof(PlayerSkills), nameof(PlayerSkills.FriendOfTheForest));
            var enemy = AccessTools.Field(typeof(PlayerSkills), nameof(PlayerSkills.EnemyOfTheForest));
            var playerTransform = AccessTools.Field(typeof(Player), "_transform");
            var list = new List<CodeInstruction>(instructions);
            int swapped = 0, targets = 0;
            for (int i = 0; i < list.Count - 2; i++)
            {
                if (!list[i].Calls(GetInstance) || !list[i + 1].LoadsField(skills)) continue;
                bool isFriend = list[i + 2].LoadsField(friend);
                if (!isFriend && !list[i + 2].LoadsField(enemy)) continue;
                list[i].opcode = OpCodes.Ldarg_0; list[i].operand = null;
                list[i + 1].opcode = OpCodes.Call; list[i + 1].operand = AccessTools.Method(typeof(Wards), isFriend ? nameof(FriendOfTheForest) : nameof(EnemyOfTheForest));
                list[i + 2].opcode = OpCodes.Nop; list[i + 2].operand = null;
                swapped++;
                for (int j = i - 1; j >= 0 && j >= i - 16; j--)
                {
                    if (!list[j].Calls(GetInstance) || !list[j + 1].LoadsField(playerTransform)) continue;
                    list[j].opcode = OpCodes.Ldarg_0; list[j].operand = null;
                    list[j + 1].opcode = OpCodes.Call; list[j + 1].operand = AccessTools.Method(typeof(Wards), nameof(PlayerTarget));
                    targets++;
                    break;
                }
            }
            Plugin.Log.Log(swapped > 0 && swapped == targets ? BepInEx.Logging.LogLevel.Info : BepInEx.Logging.LogLevel.Warning,
                $"Wards: {swapped} forest skill checks ({targets} with their target) in {where} read the enemy's target");
            return list;
        }
    }

    [HarmonyPatch(typeof(Character), "canSeeEnemy")]
    static class WardSightPatch
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) => Wards.SwapEffects(instructions);
    }

    /// <summary>An enemy afraid of a ward runs from the player it was after (who has it), not from the host's.</summary>
    [HarmonyPatch(typeof(Character), "canSeeEnemy")]
    static class WardFleePatch
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
            PlayerPositionTranspiler.Swap(instructions, PlayerPositionTranspiler.Flee, "Character.canSeeEnemy");
    }

    [HarmonyPatch(typeof(Character), "processAnims")]
    static class ForestSkillsAnimsPatch
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) => Wards.SwapForestSkills(instructions, "Character.processAnims");
    }

    [HarmonyPatch(typeof(Character), "onSeeEnemyNear")]
    static class ForestSkillsNearPatch
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) => Wards.SwapForestSkills(instructions, "Character.onSeeEnemyNear");
    }

    /// <summary>
    /// The game lights the path-finding nodes under each player light every 0.3 s (Controller.updateLogicLights), and
    /// shadow creatures and shadow armour check those nodes (Core.isInLight). A puppet's torch or lantern is the
    /// item's own light prefab, which lights nodes as the game's do. A puppet's flashlight beam is a copy of the host's
    /// "Flashlight", which the game clips to the host's player's flashlight collider, so it would light nothing where
    /// the puppet is. This moves the clip box to the puppet: the host's collider's place relative to the host's player,
    /// put at the same place relative to the puppet.
    /// </summary>
    [HarmonyPatch(typeof(Light2D), nameof(Light2D.lightGraphNodes), new[] { typeof(Bounds) })]
    static class PuppetBeamLightPatch
    {
        static void Prefix(Light2D __instance, ref Bounds _bounds)
        {
            var parent = __instance.transform.parent;
            if (parent == null || !parent.name.StartsWith("DarkwoodMP_Remote_")) return;
            var host = Player.Instance;
            var box = host != null ? host.flashlightCollider : null;
            if (box == null) return;
            Vector3 local = host.transform.InverseTransformPoint(box.transform.TransformPoint(box.center));
            Vector3 scale = box.transform.lossyScale;
            float side = Mathf.Max(Mathf.Abs(box.size.x * scale.x), Mathf.Abs(box.size.y * scale.y)) * 1.42f; // covers the box turned any way
            _bounds = new Bounds(parent.TransformPoint(local), new Vector3(side, 10000f, side));
        }
    }
}
