using System;
using System.Collections;
using DarkwoodMP.Net;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using DarkwoodMP.World.Scripted;

namespace DarkwoodMP.World.Story
{
    /// <summary>
    /// The game's end seen by a joiner alone, out of the session, and the
    /// way to the credits and the main menu.
    /// </summary>
    internal sealed partial class Chapters
    {
        /// <summary>
        /// Joiner: leave the session and see the ending alone in our copy of
        /// the host's world.
        /// </summary>
        internal static void SoloEnding(EndKind kind)
        {
            var s = S;
            if (s != null)
            {
                INetBackend backend = s.Backend;
                if (backend != null)
                {
                    backend.Leave();
                }
            }

            WorldShare instance = Plugin.Instance.Host.Share;
            if (instance != null)
            {
                instance.SetLoadStatus("The host reached the end of the game.");
            }

            if (!EndingRules.ShowsEnding(
                WorldShare.InWorld, Plugin.Instance.Host.SaveFolders.Active))
            {
                return;
            }

            Plugin.Notice("The host has reached the end of the game");
            Plugin.Instance.StartCoroutine(ShowEnding(kind));
        }

        /// <summary>
        /// Joiner, out of the session: the ending in our copy of the host's
        /// world. Saves still go to the mod's cache (SaveRedirect lasts until
        /// the main menu).
        /// </summary>
        private static IEnumerator ShowEnding(EndKind kind)
        {
            yield return new WaitForSecondsRealtime(2f); // the notice
            if (kind == EndKind.Epilogue)
            {
                bool started = false;
                try
                {
                    started = StartEpilogue();
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning(
                        $"Couldn't start the epilogue: {e.Message}");
                }

                if (started)
                {
                    float until = Time.realtimeSinceStartup
                        + EndingRules.AloneEnterFor;
                    while (Time.realtimeSinceStartup < until
                        && !(Player.Instance != null
                            && Player.Instance.inEpilogue))
                    {
                        yield return null;
                    }
                }

                if (started && Player.Instance != null
                    && Player.Instance.inEpilogue)
                {
                    Plugin.Log.LogInfo(
                        "Playing the epilogue (the credits follow, then the "
                            + "main menu)");
                    if (!Plugin.Instance.Host.TestQuickEnd)
                    {
                        yield return ToMenuAfterEnding();
                        yield break;
                    }

                    yield return new WaitForSecondsRealtime(5f);
                    Plugin.Log.LogInfo(
                        "Test: skipping the rest of the epilogue");
                }
                else
                {
                    Plugin.Log.LogInfo(
                        "The epilogue didn't start here: showing the end "
                            + "screen");
                    yield return EndScreen();
                }
            }
            else
            {
                yield return EndScreen();
            }

            ToCredits();
            yield return ToMenuAfterEnding();
        }

        /// <summary>
        /// As the epilogue's startDream step does it: inputs off, a black
        /// screen, then the epilogue dream.
        /// </summary>
        private static bool StartEpilogue()
        {
            var dreams = Singleton<Dreams>.Instance;
            var p = Player.Instance;
            var o = Singleton<OutsideLocations>.Instance;
            var facts = new StartFacts
            {
                Dreams = Prologue.DreamsOf(dreams),
                HasPlayer = p != null,
                InOutside = o != null && o.playerInOutsideLocation,
            };

            if (!EndingRules.MayStartAlone(facts))
            {
                return false;
            }

            Core.forbidInputs = true;
            p.halt();
            dreams.wantToDream = true;
            Singleton<UI>.Instance.tweenBlackScreenTop(Color.black, 2f);
            Plugin.Instance.StartCoroutine(PrepareSoon(dreams));
            return true;
        }

        private static IEnumerator PrepareSoon(Dreams dreams)
        {
            yield return new WaitForSecondsRealtime(2.1f);
            dreams.StartCoroutine(dreams.prepareDream(EpilogueDream));
        }

        private static IEnumerator EndScreen()
        {
            try
            {
                Singleton<UI>.Instance.showEndGame();
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning(
                    $"Couldn't show the end screen: {e.Message}");
            }

            Plugin.Log.LogInfo("Showing the end screen");
            float time = 12f;
            if (Plugin.Instance.Host.TestQuickEnd)
            {
                time = 4f;
            }

            yield return new WaitForSecondsRealtime(time);
        }

        /// <summary>
        /// As EpilogueOutcomes.goToCredits does: fade out, then the credits
        /// scene, which ends at the main menu.
        /// </summary>
        internal static void ToCredits()
        {
            Plugin.Log.LogInfo("To the credits");
            try
            {
                Core.unpause();
                Singleton<Controller>.Instance.fadeAudio(
                    fadeOut: true, 3f, musicToo: true);
                Singleton<UI>.Instance.tweenBlackScreenTop(Color.black, 3f);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning(
                    $"Couldn't fade out for the credits: {e.Message}");
            }

            Core.forbidInputs = true;
            Core.coreStarted = false;
            Core.loadingGame = false;
            Core.loadedGame = false;
            Plugin.Instance.StartCoroutine(CreditsSoon());
        }

        private static IEnumerator CreditsSoon()
        {
            yield return new WaitForSecondsRealtime(3.5f);
            Core.cantChangeForbidInputs = false;
            SceneManager.LoadScene("credits");
        }

        internal static IEnumerator ToMenuAfterEnding()
        {
            bool credits = false;
            while (!WorldShare.MainMenuReady())
            {
                if (!credits && SceneManager.GetActiveScene().name == "credits")
                {
                    credits = true;
                    Plugin.Log.LogInfo("The credits are rolling");
                    if (Plugin.Instance.Host.TestQuickEnd)
                    {
                        Plugin.Instance.StartCoroutine(SkipCredits());
                    }
                }

                yield return null;
            }

            Plugin.Log.LogInfo("Back at the main menu after the ending");
            Plugin.Instance.OpenPanel(); // it says why (LoadStatus)
        }

        private static IEnumerator SkipCredits()
        {
            yield return new WaitForSecondsRealtime(3f);
            var c = UnityEngine.Object.FindObjectOfType<Credits>();
            if (c != null)
            {
                AccessTools.Method(typeof(Credits), "exitToMainMenu").Invoke(
                    c, null);
            }
        }
    }
}
