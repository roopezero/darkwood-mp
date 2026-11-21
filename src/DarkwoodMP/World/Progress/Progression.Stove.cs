using DarkwoodMP.World.Hideout;
using System;
using System.Collections.Generic;
using System.IO;
using DarkwoodMP.Combat;
using DarkwoodMP.Net;
using Newtonsoft.Json;
using UnityEngine;

namespace DarkwoodMP.World.Progress
{
    // The level taken at the stove, and the essence cooked there.
    internal sealed partial class Progression
    {
        /// <summary>
        /// The game's SkillsMenu.showHideTiers: whether it runs as it is
        /// (only showing the tiers, or the game's skill choice open);
        /// otherwise the stove's full bar gives the level here.
        /// </summary>
        public bool ShowHideTiers(SkillsMenu menu, bool onlyShow)
        {
            if (StoveRules.RunsGame(onlyShow, menu.opened))
            {
                return true;
            }

            TakeLevelAtStove();
            return false;
        }

        /// <summary>
        /// Our player's essence (0 with no player), before the game's
        /// consumeExp cooks.
        /// </summary>
        public static int Experience()
        {
            if (Player.Instance != null)
            {
                return Player.Instance.experience;
            }

            return 0;
        }

        /// <summary>
        /// The game's consumeExp is done: what it added to our player's
        /// essence since before (Experience) is our cooking, unless it
        /// was a replay.
        /// </summary>
        public void ConsumedExp(int before)
        {
            Player p = Player.Instance;
            int added = 0;
            if (p != null)
            {
                added = p.experience - before;
            }

            if (ProgressRules.Cooked(p != null, Coop.Guards.Replaying, added))
            {
                Cooked(added);
            }
        }

        /// <summary>
        /// The stove's full bar (LevelingMenu.checkIfWantToLevel ->
        /// SkillsMenu.showHideTiers): take the level and its skill point,
        /// without the game's skill choice (the point is spent on the Skills
        /// panel). The rest is what SkillsMenu.confirmSkills does after a
        /// choice: the bar starts again (and gives the next level if it's still
        /// full), and levels 2, 3, 5, 6 and 7 bring their dream (only the
        /// host's in co-op, World.DreamSync).
        /// </summary>
        internal void TakeLevelAtStove()
        {
            var p = Player.Instance;
            var menu = StoveMenu;
            if (p == null || menu == null)
            {
                return;
            }

            p.currentLevel++;
            p.gainSaturation(StoveRules.Saturation);
            var d = Singleton<Dreams>.Instance;
            bool client = d != null && NetEnemies.ClientMode
                && Hideouts.Together,
                was = d != null && d.disabled;
            if (client)
            {
                // a client's level brings no dream of its own
                d.disabled = true;
            }

            try
            {
                p.gainExp(0);
                menu.resetLiquid();
                try
                {
                    AudioController.Play("UI_placeItem");
                }
                catch
                {
                }

                int l = p.currentLevel;
                if (d != null
                    && StoveRules.BringsDream(l, HadDream(d, l), d.disabled))
                {
                    d.wantToDream = true;
                    MarkDream(d, l);
                    if (l == StoveRules.FirstDreamLevel)
                    {
                        d.startTransition.dreamToTransitionTo =
                            StoveRules.FirstDream;
                    }
                }

                if (d != null && Transitions(d))
                {
                    Core.forbidInputs = true;
                    if (Singleton<CamMain>.Instance != null)
                    {
                        Singleton<CamMain>.Instance.gameObject.SetActive(true);
                    }

                    Singleton<UI>.Instance.InventorySelectionPrompt.Hide();
                    d.startTransition.transition();
                    menu.hide(forceHide: true);
                }
                else if (d != null)
                {
                    d.wantToDream = false;
                }
            }
            finally
            {
                if (client)
                {
                    d.disabled = was;
                }
            }

            CoopSession coop = Coop.Current;
            if (coop != null)
            {
                coop.Dreaming.AfterConfirm(d);
            }

            LevelTaken();
            Plugin.Log.LogInfo(
                $"[progress] took level {p.currentLevel} at the stove: "
                    + $"{SkillTree.Points(p)} skill points to spend");
            p.displayMessage(
                $"A skill point to spend: press {SkillsPanel.KeyName} for the "
                    + "Skills");
        }

        /// <summary>
        /// Whether the level's dream was had (Dreams.hadDreamAtLvlN).
        /// </summary>
        private static bool HadDream(Dreams d, int level)
        {
            switch (level)
            {
                case 2:
                    return d.hadDreamAtLvl2;
                case 3:
                    return d.hadDreamAtLvl3;
                case 5:
                    return d.hadDreamAtLvl5;
                case 6:
                    return d.hadDreamAtLvl6;
                case 7:
                    return d.hadDreamAtLvl7;
                default:
                    return false;
            }
        }

        /// <summary>The level's dream counts as had.</summary>
        private static void MarkDream(Dreams d, int level)
        {
            switch (level)
            {
                case 2:
                    d.hadDreamAtLvl2 = true;
                    break;
                case 3:
                    d.hadDreamAtLvl3 = true;
                    break;
                case 5:
                    d.hadDreamAtLvl5 = true;
                    break;
                case 6:
                    d.hadDreamAtLvl6 = true;
                    break;
                case 7:
                    d.hadDreamAtLvl7 = true;
                    break;
            }
        }

        /// <summary>
        /// Whether the wanted dream's transition starts; the game is asked
        /// for a preset only in a random world.
        /// </summary>
        private static bool Transitions(Dreams d)
        {
            bool random = Core.randomGeneration;
            bool preset = d.wantToDream && random
                && d.hasUnspawnedRandomPresetForChapter(
                    Singleton<WorldGenerator>.Instance.chapterID);
            return StoveRules.Transitions(d.wantToDream, random, preset);
        }
    }
}
