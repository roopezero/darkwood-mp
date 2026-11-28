using DarkwoodMP.World.Talk;
using UnityEngine;

namespace DarkwoodMP.World.Dreaming
{
    /// <summary>
    /// DreamSync's replay of another game's end of the shared dream (a level
    /// dream, the village cellar's, a part of the epilogue) while our player
    /// has a dialogue, the map or the journal open.
    ///
    /// The game never ends a dream with a dialogue open. A dialogue turns
    /// the main camera off and puts the black screen up under its window
    /// (DialogueWindow.SetDialogue, onTweenOpen); the part's transition then
    /// ran under it, and the next part's start took the dialogue's hold off
    /// the player (startDreaming's resetState: inDialogue false) but left
    /// the window up and the main camera off (seen in map:epidialog before
    /// the fix). Escape reaches the dialogue only while inDialogue is set
    /// (InputScript), and the NPC of a dialogue in the part went with the
    /// old part, so the player was left with nothing of the world drawn.
    ///
    /// So our dialogue is closed first, at once (DialogueGuard.CloseNow: the
    /// game's onTweenClose, without the fades), and the map and the journal
    /// too; a dialogue still opening or closing is waited for a moment
    /// (DreamRules.Replay). Once the next part has started, the camera and
    /// the black screen are checked once more.
    /// </summary>
    internal sealed partial class DreamSync
    {
        /// The outcome of the replayed end still to run (null: none).
        private string _replay;
        private Seconds _replayBy;

        /// Something of ours was closed for a replayed end: checked once the
        /// next part starts.
        private bool _closedForEnd;

        private static DialogueWindow Window
        {
            get
            {
                if (Singleton<UI>.Instance == null)
                {
                    return null;
                }

                return Singleton<UI>.Instance.dialogueWindow;
            }
        }

        private ReplayFacts ReplayOf()
        {
            var facts = new ReplayFacts { Now = Now, WaitBy = _replayBy };
            DialogueWindow w = Window;
            Player p = Player.Instance;
            if (w != null && p != null)
            {
                bool shown = w.gameObject.activeInHierarchy;
                facts = facts with
                {
                    DialogueOpen = shown || p.inDialogue,
                    DialogueTweening = shown && (w.tweening || !w.opened),
                };
            }

            var map = Singleton<Map>.Instance;
            bool mapOpen = map != null && map.opened;
            bool journalOpen = Singleton<UI>.Instance != null
                && Singleton<UI>.Instance.journal != null
                && Singleton<UI>.Instance.journal.opened;
            return facts with { MenuOpen = mapOpen || journalOpen };
        }

        /// <summary>
        /// The replayed end: now, once our dialogue and menus are closed.
        /// </summary>
        private void Replay(Dreams d)
        {
            ReplayStep step = DreamRules.Replay(ReplayOf());
            if (step == ReplayStep.Wait)
            {
                return;
            }

            string outcome = _replay;
            _replay = null;
            if (step == ReplayStep.CloseFirst)
            {
                CloseOurs();
            }

            if (d == null)
            {
                return;
            }

            d.outcome = outcome;
            _coop.Guards.Replaying = true;
            try
            {
                d.initiateEndDreaming();
            }
            finally
            {
                _coop.Guards.Replaying = false;
            }
        }

        private void CloseOurs()
        {
            _closedForEnd = true;
            DialogueWindow w = Window;
            Player p = Player.Instance;
            bool talking = w != null && p != null
                && (w.gameObject.activeInHierarchy || p.inDialogue);
            DialogueGuard guard = DialogueGuard.Running;
            if (talking && guard != null)
            {
                guard.CloseNow(w, "the shared dream's part ends");
            }

            var map = Singleton<Map>.Instance;
            if (map != null && map.opened)
            {
                map.close(true);
            }

            var journal = Singleton<UI>.Instance.journal;
            if (journal != null && journal.opened)
            {
                journal.close(true);
            }
        }

        /// <summary>
        /// The next part started (Dreams.startDreaming): after a replayed end
        /// that closed our dialogue, the main camera is on and the black
        /// screen under the dialogue is gone, unless a dialogue is open
        /// again.
        /// </summary>
        private void AfterReplay()
        {
            if (!_closedForEnd)
            {
                return;
            }

            _closedForEnd = false;
            DialogueWindow w = Window;
            if (w == null || w.gameObject.activeInHierarchy)
            {
                return;
            }

            var cam = Singleton<CamMain>.Instance;
            if (cam != null && !cam.gameObject.activeSelf)
            {
                Plugin.Log.LogInfo("[dream] the main camera was off: on");
                cam.gameObject.SetActive(true);
            }

            GameObject black = Singleton<UI>.Instance.blackScreen;
            if (black == null || !black.activeInHierarchy)
            {
                return;
            }

            var sprite = black.GetComponent<tk2dBaseSprite>();
            if (sprite != null && sprite.color.a > 0.1f)
            {
                Plugin.Log.LogInfo("[dream] the black screen was up: faded");
                Singleton<UI>.Instance.tweenBlackScreen(
                    new Color(0f, 0f, 0f, 0f), 0.5f);
            }
        }
    }
}
