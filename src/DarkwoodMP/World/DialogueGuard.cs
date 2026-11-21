using System;
using DarkwoodMP.World.Talk;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Keeps a dialogue (and a trade) closable when its NPC goes away while it
    /// is open.
    ///
    /// The game never expects that: DialogueWindow.close() does nothing once
    /// its NPC is destroyed (`npc != null` is false), and closeTrade() throws
    /// on `npc.GetComponent`, which is what Escape and the trade window's close
    /// button call. The player then has the window on screen, the game paused
    /// and the controls taken (Player.inDialogue, inShop) until they quit the
    /// game. AfterNight closes the dialogue before it ends a joiner's morning
    /// (endAfterNight destroys the trader); this is the safety for anything
    /// else that takes an NPC away mid-dialogue:
    ///
    /// - With the NPC destroyed, closeTrade, close and escPress do the game's
    ///   own closing steps without the NPC (ForceClose): what the player put on
    ///   the table comes back, the trader's window goes, the screen fades back
    ///   and onTweenClose gives the controls back. A frame watch does the same
    ///   if nobody presses anything.
    /// - With the NPC switched off while playing together, the dialogue is
    ///   closed the game's way.
    /// - A dialogue whose NPC is gone and that hasn't closed in StuckAfter
    ///   seconds is closed by force.
    /// </summary>
    internal sealed class DialogueGuard
    {
        /// <summary>
        /// Since when the open dialogue's NPC has been gone, and switched off.
        /// </summary>
        private DialogueWatch _watch;
        /// <summary>
        /// The trader's own inventory window (parented to the UI, not to him,
        /// so it outlives him).
        /// </summary>
        private GameObject _shopUI;

        private readonly CoopSession _coop;

        private static readonly Action<DialogueWindow> TweenClose =
            AccessTools.MethodDelegate<Action<DialogueWindow>>(
                AccessTools.Method(typeof(DialogueWindow), "onTweenClose"));

        /// <summary>For one session (CoopSession.Dialogues).</summary>
        public DialogueGuard(CoopSession coop)
        {
            _coop = coop;
        }

        private static DialogueWindow Window
        {
            get
            {
                if (Singleton<UI>.Instance != null)
                {
                    return Singleton<UI>.Instance.dialogueWindow;
                }

                return null;
            }
        }

        /// <summary>
        /// The window's NPC was destroyed (Unity's null, not a cleared field).
        /// </summary>
        internal static bool NpcDestroyed(DialogueWindow w) =>
            w != null && !ReferenceEquals(w.npc, null) && w.npc == null;

        internal void OpenedTrade(DialogueWindow w)
        {
            Inventory inv = null;
            if (w != null && w.npc != null)
            {
                inv = w.npc.GetComponent<Inventory>();
            }

            if (inv != null)
            {
                _shopUI = inv.thisUI;
            }
            else
            {
                _shopUI = null;
            }
        }

        /// <summary>
        /// Every frame: close a dialogue whose NPC is gone or switched off.
        /// </summary>
        public void Update()
        {
            var w = Window;
            DialogueTick tick = DialogueRules.Watch(FactsOf(w), _watch);
            _watch = tick.Watch;
            switch (tick.Action)
            {
                case DialogueAction.ForceDestroyed:
                    ForceClose(w, "destroyed");
                    break;
                case DialogueAction.ForceGone:
                    ForceClose(
                        w, "gone for " + DialogueRules.StuckAfter + " s");
                    break;
                case DialogueAction.Close:
                    // tried; tried again in a second if it didn't close
                    Plugin.Log.LogInfo(
                        $"[world] {w.npc.name} was switched off during our "
                            + "dialogue: closing it");
                    Close(w);
                    break;
            }
        }

        /// <summary>
        /// The open dialogue and its NPC, this frame. w may be null.
        /// </summary>
        private DialogueFacts FactsOf(DialogueWindow w)
        {
            var p = Player.Instance;
            if (p == null || w == null || !p.inDialogue)
            {
                return default;
            }

            // destroyed, or cleared by a close that is on its way
            bool gone = w.npc == null;
            bool active = false;
            bool together = false;
            if (!gone)
            {
                active = w.npc.gameObject.activeInHierarchy;
                together = Hideouts.Together;
            }

            return new DialogueFacts
            {
                Open = true,
                Gone = gone,
                Destroyed = NpcDestroyed(w),
                Active = active,
                Together = together,
                Tweening = w.tweening,
                Now = new Seconds(Time.unscaledTime),
            };
        }

        /// <summary>Close the dialogue (and its trade) the game's way, without
        /// the NPC's goodbye line. False: it is still opening or closing
        /// (tweening) and nothing was done.</summary>
        internal bool Close(DialogueWindow w)
        {
            DialogueClose way = DialogueRules.CloseWay(CloseFactsOf(w));
            if (way == DialogueClose.Force)
            {
                ForceClose(w, "destroyed");
            }

            if (way != DialogueClose.CloseTrade && way != DialogueClose.Close)
            {
                return DialogueRules.Closed(way);
            }

            try
            {
                if (way == DialogueClose.CloseTrade)
                {
                    w.closeTrade();
                }

                // From the main menu close() shows the goodbye line instead of
                // closing (see TalkLock.CloseWhenReady).
                w.currentMenu = DialogueWindow.CurrentMenu.gossip;
                w.close();
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Closing the dialogue: {e.Message}");
                ForceClose(w, "close failed");
            }

            return true;
        }

        /// <summary>
        /// What closing the dialogue looks at. w may be null.
        /// </summary>
        private DialogueCloseFacts CloseFactsOf(DialogueWindow w)
        {
            if (w == null)
            {
                return default;
            }

            bool shop = Player.Instance != null && Player.Instance.inShop;
            return new DialogueCloseFacts
            {
                HasWindow = true,
                Tweening = w.tweening,
                Destroyed = NpcDestroyed(w),
                Cleared = w.npc == null,
                InShop = shop,
            };
        }

        /// <summary>
        /// closeTrade's prefix: false (skip the game's method) after closing
        /// the dialogue if the NPC was destroyed, even while tweening.
        /// </summary>
        internal bool RunCloseTrade(DialogueWindow w)
        {
            if (DialogueRules.RunsCloseTrade(NpcDestroyed(w)))
            {
                return true;
            }

            ForceClose(w, "destroyed");
            return false;
        }

        /// <summary>
        /// close's and escPress's prefix: false (skip the game's method) after
        /// closing the dialogue if the NPC was destroyed and the window isn't
        /// opening or closing.
        /// </summary>
        internal bool RunClose(DialogueWindow w)
        {
            if (DialogueRules.RunsClose(NpcDestroyed(w), w.tweening))
            {
                return true;
            }

            ForceClose(w, "destroyed");
            return false;
        }

        /// <summary>
        /// finishedElement's prefix: whether the game's runs.
        /// </summary>
        internal bool RunFinishedLine(DialogueWindow w)
        {
            var line = new LineFacts
            {
                Elements = w.currentBoardElements.Count,
                HasDialogue = w.currentDialogue != null,
                HasNpc = w.npc != null,
                Active = w.gameObject.activeSelf,
            };

            return DialogueRules.RunsFinishedLine(line);
        }

        /// <summary>
        /// DialogueWindow.closeTrade and close without the NPC (which is gone).
        /// </summary>
        internal void ForceClose(DialogueWindow w, string why)
        {
            var p = Player.Instance;
            if (w == null || p == null)
            {
                return;
            }

            Plugin.Log.LogInfo(
                $"[world] the dialogue's NPC is {why}: closing the dialogue "
                    + "without them");
            _watch = default;
            try
            {
                bool trading = w.trading != null
                    && w.trading.gameObject.activeSelf;
                if (DialogueRules.UndoesTrade(p.inShop, trading))
                {
                    InventoryController instance =
                        Singleton<InventoryController>.Instance;
                    if (instance != null)
                    {
                        ItemPopup itemPopup = instance.itemPopup;
                        if (itemPopup != null)
                        {
                            itemPopup.hide();
                        }
                    }

                    if (w.trading != null)
                    {
                        w.trading.gameObject.SetActive(false);
                    }

                    if (w.options != null)
                    {
                        w.options.gameObject.SetActive(true);
                    }

                    p.inShop = false;
                    p.talkedToNPC = null;
                    p.Hotbar.refresh();
                    p.Inventory.hide();
                    if (_shopUI != null)
                    {
                        UnityEngine.Object.Destroy(_shopUI);
                    }

                    // what our player put on the table
                    w.exchangePlayer.transferAllItemsToPlayer();
                    w.exchangePlayer.hide();
                    // his things went with him
                    w.exchangeTrader.clear();
                    w.exchangeTrader.hide();
                    p.Hotbar.thisUI.transform.localPosition = p.Hotbar.position;
                    var pos = p.Hotbar.thisUI.GetComponent<PositionMe>();
                    if (pos != null)
                    {
                        pos.offset = new Vector2(
                            p.Hotbar.position.x, p.Hotbar.position.z);
                        pos.init();
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning(
                    $"Closing the trade without the trader: {e.Message}");
            }

            _shopUI = null;
            p.inShop = false;
            w.tweening = true;
            w.wantToCook = false;
            w.currentMenu = DialogueWindow.CurrentMenu.main;
            w.displayingDialogue = false;
            w.currentDialogue = null;
            w.currentBoardElements.Clear();
            w.npc = null;
            try
            {
                p.halt();
                Core.forbidInputsFor(1.2f);
                Singleton<UI>.Instance.tweenBlackScreenTop(
                    new Color(0f, 0f, 0f, 1f), 0.5f);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Closing the dialogue: {e.Message}");
            }

            // onTweenClose gives the controls back (inDialogue,
            // performingAction, the pause, the camera).
            Singleton<Controller>.Instance.Invoke(() =>
            {
                try
                {
                    TweenClose(w);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning($"Closing the dialogue: {e.Message}");
                    w.tweening = false;
                    w.gameObject.SetActive(false);
                    if (Singleton<CamMain>.Instance != null)
                    {
                        Singleton<CamMain>.Instance.gameObject.SetActive(true);
                    }

                    p.inDialogue = false;
                    p.performingAction = false;
                    p.talkedToNPC = null;
                    Core.unpause(1f);
                }
            }, 0.5f, timeScaleDependent: false);

            _coop.Talk.Closed();
        }
    }

    [HarmonyPatch(typeof(DialogueWindow), nameof(DialogueWindow.openTrade))]
    static class DialogueGuardOpenTradePatch
    {
        static void Postfix(DialogueWindow __instance)
        {
            CoopSession coop = Coop.Current;
            if (coop != null)
            {
                coop.Dialogues.OpenedTrade(__instance);
            }
        }
    }

    /// <summary>
    /// The trade window's close button and Escape (escPress): closeTrade throws
    /// without the trader.
    /// </summary>
    [HarmonyPatch(typeof(DialogueWindow), nameof(DialogueWindow.closeTrade))]
    static class DialogueGuardCloseTradePatch
    {
        static bool Prefix(DialogueWindow __instance)
        {
            CoopSession coop = Coop.Current;
            if (coop == null)
            {
                return true;
            }

            return coop.Dialogues.RunCloseTrade(__instance);
        }
    }

    /// <summary>close() does nothing once the NPC is destroyed.</summary>
    [HarmonyPatch(typeof(DialogueWindow), nameof(DialogueWindow.close))]
    static class DialogueGuardClosePatch
    {
        static bool Prefix(DialogueWindow __instance)
        {
            CoopSession coop = Coop.Current;
            if (coop == null)
            {
                return true;
            }

            return coop.Dialogues.RunClose(__instance);
        }
    }

    /// <summary>
    /// Escape: a line being written (onInstantClick) and the other menus use
    /// the NPC too.
    /// </summary>
    [HarmonyPatch(typeof(DialogueWindow), nameof(DialogueWindow.escPress))]
    static class DialogueGuardEscPatch
    {
        static bool Prefix(DialogueWindow __instance)
        {
            CoopSession coop = Coop.Current;
            if (coop == null)
            {
                return true;
            }

            return coop.Dialogues.RunClose(__instance);
        }
    }

    /// <summary>A line still being written (WritingText.Update) when the
    /// dialogue closes calls finishedElement on the closed window, which has no
    /// board elements and no dialogue left and throws on currentDialogue.name.
    /// That only happens when a dialogue is closed while it writes, as the mod
    /// does (a refused talk, the host's morning ending, a gone NPC): skip it
    /// then.</summary>
    [HarmonyPatch(
        typeof(DialogueWindow), nameof(DialogueWindow.finishedElement))]
    static class DialogueGuardFinishedElementPatch
    {
        static bool Prefix(DialogueWindow __instance)
        {
            CoopSession coop = Coop.Current;
            if (coop == null)
            {
                return true;
            }

            return coop.Dialogues.RunFinishedLine(__instance);
        }
    }
}
