using System;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Keeps a dialogue (and a trade) closable when its NPC goes away while it is open.
    ///
    /// The game never expects that: DialogueWindow.close() does nothing once its NPC is destroyed (`npc != null` is
    /// false), and closeTrade() throws on `npc.GetComponent`, which is what Escape and the trade window's close
    /// button call. The player then has the window on screen, the game paused and the controls taken
    /// (Player.inDialogue, inShop) until they quit the game (issue #15: the host walked out of the hideout, its
    /// AfterNightEnded made the joiner's game end its morning, and endAfterNight destroyed the trader the joiner
    /// was trading with). AfterNight now closes the dialogue before it ends the morning; this is the safety for
    /// anything else that takes an NPC away mid-dialogue:
    ///
    /// - With the NPC destroyed, closeTrade, close and escPress do the game's own closing steps without the NPC
    ///   (ForceClose): what the player put on the table comes back, the trader's window goes, the screen fades back
    ///   and onTweenClose gives the controls back. A frame watch does the same if nobody presses anything.
    /// - With the NPC switched off while playing together, the dialogue is closed the game's way.
    /// - A dialogue whose NPC is gone and that hasn't closed in StuckAfter seconds is closed by force.
    /// </summary>
    public static class DialogueGuard
    {
        /// <summary>Seconds a dialogue may stay open without a live NPC before it's closed by force.</summary>
        private const float StuckAfter = 4f;
        private static float _goneSince = -1f;
        private static float _offSince = -1f;
        /// <summary>The trader's own inventory window (parented to the UI, not to him, so it outlives him).</summary>
        private static GameObject _shopUI;

        private static readonly Action<DialogueWindow> TweenClose =
            AccessTools.MethodDelegate<Action<DialogueWindow>>(AccessTools.Method(typeof(DialogueWindow), "onTweenClose"));

        private static DialogueWindow Window => Singleton<UI>.Instance != null ? Singleton<UI>.Instance.dialogueWindow : null;

        /// <summary>The window's NPC was destroyed (Unity's null, not a cleared field).</summary>
        internal static bool NpcDestroyed(DialogueWindow w) => w != null && !ReferenceEquals(w.npc, null) && w.npc == null;

        internal static void OpenedTrade(DialogueWindow w)
        {
            var inv = w != null && w.npc != null ? w.npc.GetComponent<Inventory>() : null;
            _shopUI = inv != null ? inv.thisUI : null;
        }

        /// <summary>Every frame: close a dialogue whose NPC is gone or switched off.</summary>
        public static void Update()
        {
            var p = Player.Instance;
            var w = Window;
            if (p == null || w == null || !p.inDialogue) { _goneSince = _offSince = -1f; return; }
            float now = Time.unscaledTime;
            bool gone = w.npc == null; // destroyed, or cleared by a close that is on its way
            bool off = !gone && !w.npc.gameObject.activeInHierarchy && Hideouts.Together;
            if (!gone) _goneSince = -1f; else if (_goneSince < 0f) _goneSince = now;
            if (!off) _offSince = -1f; else if (_offSince < 0f) _offSince = now;

            if (NpcDestroyed(w) && !w.tweening) { ForceClose(w, "destroyed"); return; }
            if (gone && now - _goneSince > StuckAfter) { ForceClose(w, "gone for " + StuckAfter + " s"); return; }
            if (off && !w.tweening && now - _offSince > 1f)
            {
                _offSince = now; // tried; try again in a second if it didn't close
                Plugin.Log.LogInfo($"[world] {w.npc.name} was switched off during our dialogue: closing it");
                Close(w);
            }
        }

        /// <summary>Close the dialogue (and its trade) the game's way, without the NPC's goodbye line. False: it is
        /// still opening or closing (tweening) and nothing was done.</summary>
        internal static bool Close(DialogueWindow w)
        {
            if (w == null || w.tweening) return false;
            if (NpcDestroyed(w)) { ForceClose(w, "destroyed"); return true; }
            if (w.npc == null) return false;
            try
            {
                if (Player.Instance != null && Player.Instance.inShop) w.closeTrade();
                // From the main menu close() shows the goodbye line instead of closing (see TalkLock.CloseWhenReady).
                w.currentMenu = DialogueWindow.CurrentMenu.gossip;
                w.close();
            }
            catch (Exception e) { Plugin.Log.LogWarning($"Closing the dialogue: {e.Message}"); ForceClose(w, "close failed"); }
            return true;
        }

        /// <summary>DialogueWindow.closeTrade and close without the NPC (which is gone).</summary>
        internal static void ForceClose(DialogueWindow w, string why)
        {
            var p = Player.Instance;
            if (w == null || p == null) return;
            Plugin.Log.LogInfo($"[world] the dialogue's NPC is {why}: closing the dialogue without them");
            _goneSince = _offSince = -1f;
            try
            {
                if (p.inShop || (w.trading != null && w.trading.gameObject.activeSelf))
                {
                    Singleton<InventoryController>.Instance?.itemPopup?.hide();
                    if (w.trading != null) w.trading.gameObject.SetActive(false);
                    if (w.options != null) w.options.gameObject.SetActive(true);
                    p.inShop = false;
                    p.talkedToNPC = null;
                    p.Hotbar.refresh();
                    p.Inventory.hide();
                    if (_shopUI != null) UnityEngine.Object.Destroy(_shopUI);
                    w.exchangePlayer.transferAllItemsToPlayer(); // what our player put on the table
                    w.exchangePlayer.hide();
                    w.exchangeTrader.clear();                    // his things went with him
                    w.exchangeTrader.hide();
                    p.Hotbar.thisUI.transform.localPosition = p.Hotbar.position;
                    var pos = p.Hotbar.thisUI.GetComponent<PositionMe>();
                    if (pos != null) { pos.offset = new Vector2(p.Hotbar.position.x, p.Hotbar.position.z); pos.init(); }
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning($"Closing the trade without the trader: {e.Message}"); }
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
                Singleton<UI>.Instance.tweenBlackScreenTop(new Color(0f, 0f, 0f, 1f), 0.5f);
            }
            catch (Exception e) { Plugin.Log.LogWarning($"Closing the dialogue: {e.Message}"); }
            // onTweenClose gives the controls back (inDialogue, performingAction, the pause, the camera).
            Singleton<Controller>.Instance.Invoke(() =>
            {
                try { TweenClose(w); }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning($"Closing the dialogue: {e.Message}");
                    w.tweening = false;
                    w.gameObject.SetActive(false);
                    if (Singleton<CamMain>.Instance != null) Singleton<CamMain>.Instance.gameObject.SetActive(true);
                    p.inDialogue = false;
                    p.performingAction = false;
                    p.talkedToNPC = null;
                    Core.unpause(1f);
                }
            }, 0.5f, timeScaleDependent: false);
            TalkLock.Closed();
        }
    }

    [HarmonyPatch(typeof(DialogueWindow), nameof(DialogueWindow.openTrade))]
    static class DialogueGuardOpenTradePatch
    {
        static void Postfix(DialogueWindow __instance) => DialogueGuard.OpenedTrade(__instance);
    }

    /// <summary>The trade window's close button and Escape (escPress): closeTrade throws without the trader.</summary>
    [HarmonyPatch(typeof(DialogueWindow), nameof(DialogueWindow.closeTrade))]
    static class DialogueGuardCloseTradePatch
    {
        static bool Prefix(DialogueWindow __instance)
        {
            if (!DialogueGuard.NpcDestroyed(__instance)) return true;
            DialogueGuard.ForceClose(__instance, "destroyed");
            return false;
        }
    }

    /// <summary>close() does nothing once the NPC is destroyed.</summary>
    [HarmonyPatch(typeof(DialogueWindow), nameof(DialogueWindow.close))]
    static class DialogueGuardClosePatch
    {
        static bool Prefix(DialogueWindow __instance)
        {
            if (!DialogueGuard.NpcDestroyed(__instance) || __instance.tweening) return true;
            DialogueGuard.ForceClose(__instance, "destroyed");
            return false;
        }
    }

    /// <summary>Escape: a line being written (onInstantClick) and the other menus use the NPC too.</summary>
    [HarmonyPatch(typeof(DialogueWindow), nameof(DialogueWindow.escPress))]
    static class DialogueGuardEscPatch
    {
        static bool Prefix(DialogueWindow __instance)
        {
            if (!DialogueGuard.NpcDestroyed(__instance) || __instance.tweening) return true;
            DialogueGuard.ForceClose(__instance, "destroyed");
            return false;
        }
    }

    /// <summary>A line still being written (WritingText.Update) when the dialogue closes calls finishedElement on the
    /// closed window, which has no board elements and no dialogue left and throws on currentDialogue.name. That only
    /// happens when a dialogue is closed while it writes, as the mod does (a refused talk, the host's morning ending, a
    /// gone NPC): skip it then.</summary>
    [HarmonyPatch(typeof(DialogueWindow), nameof(DialogueWindow.finishedElement))]
    static class DialogueGuardFinishedElementPatch
    {
        static bool Prefix(DialogueWindow __instance) =>
            __instance.currentBoardElements.Count > 0 ||
            (__instance.currentDialogue != null && __instance.npc != null && __instance.gameObject.activeSelf);
    }
}
