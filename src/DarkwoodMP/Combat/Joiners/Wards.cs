using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using DarkwoodMP.Combat.Beams;
using DarkwoodMP.Combat.Joiners;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMP.Combat
{
    /// <summary>
    /// The host's enemies check a few things about "the player" that belong to
    /// one player: the shadow ward and the forest spirit ward
    /// (Character.canSeeEnemy: an enemy afraid of them runs away), and the
    /// Friend / Enemy of the Forest skills (processAnims, onSeeEnemyNear: how
    /// an aggressive animal treats the player it's after). The game reads them
    /// from Player.Instance, the host's player. These transpilers make them
    /// read the player the enemy is after: its target's proxy, with the
    /// joiner's own state from their PlayerState (NetPlayerProxy), or the
    /// host's player when the target is the host or not a player at all. With
    /// nobody else in the world that is the host's player, as in the game.
    /// </summary>
    public static class Wards
    {
        /// <summary>
        /// The joiner whose proxy the enemy is after, or null (then the host's
        /// player counts, as in the game).
        /// </summary>
        private static NetPlayerProxy TargetProxy(Character c)
        {
            int standIns = NetPlayerProxy.All.Count;
            bool target = c != null && c.target != null;
            NetPlayerProxy proxy = null;
            if (standIns != 0 && target)
            {
                proxy = c.target.GetComponent<NetPlayerProxy>();
            }

            if (!WardRules.TargetsStandIn(standIns, target, proxy != null))
            {
                return null;
            }

            return proxy;
        }

        /// <summary>
        /// For Player.Instance.effects.hasEffectType(type) in canSeeEnemy.
        /// </summary>
        public static bool HasEffect(Character c, CharacterEffectType type)
        {
            return WardRules.HasEffect(FactsOf(TargetProxy(c), type));
        }

        /// <summary>
        /// For "target == Player.Instance._transform" before a forest skill
        /// check: the target, when it is a joiner.
        /// </summary>
        public static Transform PlayerTarget(Character c)
        {
            var p = TargetProxy(c);
            if (p != null)
            {
                return p.transform;
            }

            return Player.Instance._transform;
        }

        public static bool FriendOfTheForest(Character c)
        {
            var p = TargetProxy(c);
            if (p != null)
            {
                return WardRules.SkillOf(true, p.FriendOfTheForest, false);
            }

            return WardRules.SkillOf(
                false, false, Player.Instance.skills.FriendOfTheForest);
        }

        public static bool EnemyOfTheForest(Character c)
        {
            var p = TargetProxy(c);
            if (p != null)
            {
                return WardRules.SkillOf(true, p.EnemyOfTheForest, false);
            }

            return WardRules.SkillOf(
                false, false, Player.Instance.skills.EnemyOfTheForest);
        }

        private static WardFacts FactsOf(
            NetPlayerProxy proxy, CharacterEffectType type)
        {
            if (proxy != null)
            {
                return new WardFacts
                {
                    StandIn = true,
                    Kind = KindOf(type),
                    ShadowWard = proxy.ShadowWard,
                    ForestSpiritWard = proxy.ForestSpiritWard,
                };
            }

            var host = Player.Instance;
            return new WardFacts
            {
                Kind = KindOf(type),
                HostHas = host != null && host.effects != null
                    && host.effects.hasEffectType(type),
            };
        }

        private static WardKind KindOf(CharacterEffectType type)
        {
            if (type == CharacterEffectType.shadowWard)
            {
                return WardKind.ShadowWard;
            }

            if (type == CharacterEffectType.forestSpiritWard)
            {
                return WardKind.ForestSpiritWard;
            }

            return WardKind.Other;
        }

        private static MethodInfo GetInstance =>
            AccessTools.PropertyGetter(typeof(Player), nameof(Player.Instance));

        /// <summary>
        /// Player.Instance.effects.hasEffectType(X) becomes
        /// Wards.HasEffect(this, X).
        /// </summary>
        public static IEnumerable<CodeInstruction> SwapEffects(
            IEnumerable<CodeInstruction> instructions)
        {
            var effects = AccessTools.Field(typeof(CharBase), "effects");
            var has = AccessTools.Method(
                typeof(CharacterEffects),
                nameof(CharacterEffects.hasEffectType));
            var mine = AccessTools.Method(typeof(Wards), nameof(HasEffect));
            var list = new List<CodeInstruction>(instructions);
            int swapped = 0;
            for (int i = 0; i < list.Count - 3; i++)
            {
                if (!list[i].Calls(GetInstance)
                    || !list[i + 1].LoadsField(effects)
                    || !list[i + 3].Calls(has))
                {
                    continue;
                }

                // keeps the instruction's labels
                list[i].opcode = OpCodes.Ldarg_0;
                list[i].operand = null;
                list[i + 1].opcode = OpCodes.Nop;
                list[i + 1].operand = null;
                list[i + 3].opcode = OpCodes.Call;
                list[i + 3].operand = mine;
                swapped++;
            }

            BepInEx.Logging.LogLevel level;
            if (swapped == 2)
            {
                level = BepInEx.Logging.LogLevel.Info;
            }
            else
            {
                level = BepInEx.Logging.LogLevel.Warning;
            }

            Plugin.Log.Log(level,
                $"Wards: {swapped} ward checks in Character.canSeeEnemy read "
                    + "the enemy's target (2 expected)");
            return list;
        }

        /// <summary>
        /// Player.Instance.skills.FriendOfTheForest / EnemyOfTheForest becomes
        /// Wards.X(this), and the "target == Player.Instance._transform" just
        /// before it (within the same condition) becomes
        /// "target == Wards.PlayerTarget(this)". Other comparisons with the
        /// host's player are left alone.
        /// </summary>
        public static IEnumerable<CodeInstruction> SwapForestSkills(
            IEnumerable<CodeInstruction> instructions, string where)
        {
            var skills =
                AccessTools.Field(typeof(Player), nameof(Player.skills));
            var friend = AccessTools.Field(
                typeof(PlayerSkills), nameof(PlayerSkills.FriendOfTheForest));
            var enemy = AccessTools.Field(
                typeof(PlayerSkills), nameof(PlayerSkills.EnemyOfTheForest));
            var playerTransform =
                AccessTools.Field(typeof(Player), "_transform");
            var list = new List<CodeInstruction>(instructions);
            int swapped = 0, targets = 0;
            for (int i = 0; i < list.Count - 2; i++)
            {
                if (!list[i].Calls(GetInstance)
                    || !list[i + 1].LoadsField(skills))
                {
                    continue;
                }

                bool isFriend = list[i + 2].LoadsField(friend);
                if (!isFriend && !list[i + 2].LoadsField(enemy))
                {
                    continue;
                }

                list[i].opcode = OpCodes.Ldarg_0;
                list[i].operand = null;
                list[i + 1].opcode = OpCodes.Call;
                string name = nameof(EnemyOfTheForest);
                if (isFriend)
                {
                    name = nameof(FriendOfTheForest);
                }

                list[i + 1].operand = AccessTools.Method(typeof(Wards), name);
                list[i + 2].opcode = OpCodes.Nop;
                list[i + 2].operand = null;
                swapped++;
                for (int j = i - 1; j >= 0 && j >= i - 16; j--)
                {
                    if (!list[j].Calls(GetInstance)
                        || !list[j + 1].LoadsField(playerTransform))
                    {
                        continue;
                    }

                    list[j].opcode = OpCodes.Ldarg_0;
                    list[j].operand = null;
                    list[j + 1].opcode = OpCodes.Call;
                    list[j + 1].operand =
                        AccessTools.Method(typeof(Wards), nameof(PlayerTarget));
                    targets++;
                    break;
                }
            }

            BepInEx.Logging.LogLevel level;
            if (swapped > 0 && swapped == targets)
            {
                level = BepInEx.Logging.LogLevel.Info;
            }
            else
            {
                level = BepInEx.Logging.LogLevel.Warning;
            }

            Plugin.Log.Log(level,
                $"Wards: {swapped} forest skill checks ({targets} with their "
                    + $"target) in {where} read the enemy's target");
            return list;
        }
    }
}
