using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Map test steps for a part of the shared epilogue ended by the other
    /// player while ours is in a dialogue
    /// (tools/tests/map_scenario_epidialog.py): our game goes on to the next
    /// part, and once the dialogue is closed the screen isn't left black.
    /// </summary>
    public static partial class MapScenarioEpilogue
    {
        /// The part we were in when "epidialog-open" opened the dialogue.
        private static string _dialogPart;

        private static void RegisterDialog()
        {
            Add("epidialog-open", OpenDialog, timeout: 40f);
            Add("epidialog-after", AfterDialog, timeout: 150f);
        }

        private static DialogueWindow Window =>
            Singleton<UI>.Instance.dialogueWindow;

        /// An NPC with a dialogue: one switched on in the scene if there is
        /// one (the part's own first), else any loaded.
        private static NPC Talker()
        {
            var on = Object.FindObjectsOfType<NPC>()
                .Where(n => n.characterDialogue != null)
                .ToList();
            Location place = null;
            if (D.dreamLocation != null)
            {
                place = D.dreamLocation;
            }

            if (place != null)
            {
                var own = on.FirstOrDefault(
                    n => n.transform.IsChildOf(place.transform));
                if (own != null)
                {
                    return own;
                }
            }

            if (on.Count > 0)
            {
                return on[0];
            }

            return Resources.FindObjectsOfTypeAll<NPC>().FirstOrDefault(
                n => n.characterDialogue != null
                    && n.gameObject.scene.IsValid());
        }

        /// <summary>
        /// "epidialog-open" (any): open a dialogue with an NPC (as the
        /// story's openDialogue step does) and wait until it's on screen.
        /// </summary>
        private static IEnumerator OpenDialog(string[] a, Result r)
        {
            _dialogPart = "";
            if (D.preset != null)
            {
                _dialogPart = D.preset.name;
            }

            NPC npc = Talker();
            if (npc == null)
            {
                r.Fail($"no NPC with a dialogue: {Where()}");
                yield break;
            }

            string where = "loaded, switched off";
            if (npc.gameObject.activeInHierarchy)
            {
                where = "in the scene";
            }

            if (D.dreamLocation != null
                && npc.transform.IsChildOf(D.dreamLocation.transform))
            {
                where = "in the part";
            }

            Window.initiateDialogue(npc);
            float until = Time.realtimeSinceStartup + 15f;
            while (Time.realtimeSinceStartup < until
                && !(Window.opened && !Window.tweening))
            {
                yield return new WaitForSecondsRealtime(0.25f);
            }

            r.Info = $"talking to {npc.name} ({where}) in {_dialogPart}; "
                + Screen();
            if (!Window.opened || !P.inDialogue)
            {
                r.Fail($"the dialogue didn't open: {r.Info}");
            }
        }

        /// <summary>
        /// "epidialog-after [DIR]" (any): the other player's end of the part
        /// took us on to the next one while our dialogue was open; close it
        /// if it still is (its leave button: close()) and check that the
        /// screen and the controls came back. With DIR, a screenshot
        /// DIR/epidialog-ROLE.png.
        /// </summary>
        private static IEnumerator AfterDialog(string[] a, Result r)
        {
            float until = Time.realtimeSinceStartup + 100f;
            while (Time.realtimeSinceStartup < until
                && !(D.dreaming && !D.switchingDream && D.preset != null
                    && D.preset.name != _dialogPart))
            {
                yield return new WaitForSecondsRealtime(0.5f);
            }

            yield return new WaitForSecondsRealtime(5f);
            string before = Screen();
            bool open = Window.gameObject.activeInHierarchy;
            if (open)
            {
                Window.close();
            }

            yield return new WaitForSecondsRealtime(5f);
            string after = Screen();
            if (a.Length > 0)
            {
                Directory.CreateDirectory(a[0]);
                string role = "client";
                if (Plugin.Instance.Host.Session.IsHost)
                {
                    role = "host";
                }

                string path = Path.Combine(a[0], $"epidialog-{role}.png");
                ScreenCapture.CaptureScreenshot(path);
                yield return new WaitForSecondsRealtime(1f);
                Plugin.Log.LogInfo($"[shots] {path}");
            }

            r.Info = $"from {_dialogPart}: {Where()}; after the move "
                + $"(dialogue still open {open}): {before}; then: {after}";
            if (D.preset == null || D.preset.name == _dialogPart)
            {
                r.Fail($"we didn't go on: {r.Info}");
            }
            else if (!ScreenBack())
            {
                r.Fail($"the screen or the controls didn't come back: "
                    + r.Info);
            }
        }

        private static float Alpha(GameObject g)
        {
            if (g == null || !g.activeInHierarchy)
            {
                return 0f;
            }

            var sprite = g.GetComponent<tk2dBaseSprite>();
            if (sprite == null)
            {
                return 0f;
            }

            return sprite.color.a;
        }

        private static bool CamOn()
        {
            var cam = Singleton<CamMain>.Instance;
            return cam != null && cam.gameObject.activeInHierarchy;
        }

        private static bool VideoOn()
        {
            GameObject video = Singleton<UI>.Instance.videoOverlay;
            return video != null && video.activeInHierarchy;
        }

        /// What covers the screen and holds the controls.
        private static string Screen()
        {
            var ui = Singleton<UI>.Instance;
            return $"blackScreen {Alpha(ui.blackScreen):0.00} top "
                + $"{Alpha(ui.blackScreenTop):0.00} camera {CamOn()} video "
                + $"{VideoOn()} window "
                + $"{Window.gameObject.activeInHierarchy} inDialogue "
                + $"{P.inDialogue} forbidInputs {Core.forbidInputs} "
                + $"timeScale {Time.timeScale:0.00} performingAction "
                + $"{P.performingAction}";
        }

        /// The screen shows the world and the player has the controls.
        private static bool ScreenBack()
        {
            var ui = Singleton<UI>.Instance;
            return Alpha(ui.blackScreen) < 0.1f
                && Alpha(ui.blackScreenTop) < 0.1f
                && CamOn()
                && !VideoOn()
                && !Window.gameObject.activeInHierarchy
                && !P.inDialogue
                && !Core.forbidInputs
                && Time.timeScale > 0f;
        }
    }
}
