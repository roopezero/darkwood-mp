using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DarkwoodMP.Net;
using DarkwoodMP.World;
using UnityEngine;

namespace DarkwoodMP.Menus
{
    public sealed partial class StartGameWindow
    {
        // ---- starting -------------------------------------------------------

        internal void Start()
        {
            if (CantStart() != null)
            {
                return;
            }

            CommitName();
            var plugin = Plugin.Instance;
            bool inGame = plugin.Host.Session.Active;
            bool host = StartGameRules.HostsAfterStart(_host, inGame);
            bool steam = HostingRules.HostsWithSteam(
                plugin.SteamAvailable, Plugin.Cfg.HostWith.Value);
            string name = _name;
            Core.profiles = _profiles;
            var mm = Singleton<MainMenu>.Instance;
            if (_choice > 0)
            {
                var p = Chosen.Profile;
                string text = "";
                if (host)
                {
                    string how = "Direct";
                    if (steam)
                    {
                        how = "Steam";
                    }

                    text = $", then host with {how}";
                }

                Plugin.Log.LogInfo(
                    $"[start game] continue slot {p.id} (day {p.day}){text}");
                mm.creatingProfile = false;
                Core.currentProfile = p;
                Close();
                Singleton<UI>.Instance.StartCoroutine(
                    Singleton<UI>.Instance.initLoadGame());
            }
            else
            {
                int id = NewSlot;
                string hosting = "";
                if (host)
                {
                    string via = "Direct";
                    if (steam)
                    {
                        via = "Steam";
                    }

                    hosting = $", then host with {via}";
                }

                Plugin.Log.LogInfo(
                    $"[start game] new game in slot {id} "
                        + $"({Difficulty(_difficulty)}, skip prologue "
                        + $"{_skipPrologue}, backer content "
                        + $"{_backer}){hosting}");
                // As the profiles screen does for a free slot
                // (MainMenu.displayProfile), then its Play
                // (Button.playProfile).
                Core.currentProfile = _profiles[id - 1];
                Singleton<SaveManager>.Instance.deleteCurrentProfile(
                    creatingNewProfile: true);
                mm.creatingProfile = true;
                var p = new GameProfile(id, _Active: false, 0)
                {
                    difficulty = _difficulty,
                    backerOnlyContent = _backer
                };

                Core.currentProfile = p;
                Singleton<Controller>.Instance.skipTutorial = _skipPrologue;
                Close();
                Singleton<UI>.Instance.StartCoroutine(
                    Singleton<UI>.Instance.initNewGame());
                if (StartGameRules.StoresNewName(name))
                {
                    SaveBook.SetName(id, name);
                }
            }

            if (host)
            {
                plugin.HostWhenReady(steam);
            }
        }

        // ---- the test -------------------------------------------------------

        /// <summary>
        /// "--dwmp-test-start-window": Start Game in the main menu's list opens
        /// this window. With --dwmp-sandbox DIR the saves are the copy in DIR
        /// (the record too: DIR/darkwoodmp-saves.json); --dwmp-screen WxH sizes
        /// the game's window first; --dwmp-shots DIR takes screenshots of it (a
        /// save, the list open, New Game, the host fields, Delete asked).
        /// "--dwmp-start N|new" then picks slot N or New Game and presses Start
        /// ("--dwmp-start-host direct|steam" ticks the host box,
        /// "--dwmp-start-name NAME" names the save). Logged "[start window]".
        /// </summary>
        internal static IEnumerator Test(
            string sandbox, string screen, string shots, string start,
            string hostWith, string name)
        {
            while (!WorldShare.MainMenuReady())
            {
                yield return null;
            }

            yield return new WaitForSecondsRealtime(2f);
            if (screen != null)
            {
                var wh = screen.Split('x');
                Screen.SetResolution(
                    int.Parse(wh[0]), int.Parse(wh[1]),
                    FullScreenMode.Windowed);
                yield return new WaitForSecondsRealtime(3f);
            }

            if (sandbox != null)
            {
                SaveRedirect.Begin(sandbox);
                if (!WorldShare.RedirectWorks(sandbox))
                {
                    SaveRedirect.End();
                    yield break;
                }
            }

            var w = Plugin.Instance.Host.StartGame;
            MenuList.Click("Start Game");
            yield return new WaitForSecondsRealtime(1f);
            Plugin.Log.LogInfo(
                $"[start window] clicked Start Game, window open: {w.Open}");
            foreach (var s in w._slots)
            {
                string about;
                if (s.Free)
                {
                    about = "free";
                }
                else
                {
                    about = Line(s);
                }

                Plugin.Log.LogInfo($"[start window] slot {s.Id}: " + about);
            }

            if (!w.Open)
            {
                if (sandbox != null)
                {
                    SaveRedirect.End();
                }

                yield break;
            }

            string prefix = $"{Screen.width}x{Screen.height}-";
            IEnumerator Shot(string what)
            {
                if (shots == null)
                {
                    yield break;
                }

                System.IO.Directory.CreateDirectory(shots);
                yield return new WaitForSecondsRealtime(0.7f);
                ScreenCapture.CaptureScreenshot(
                    System.IO.Path.Combine(shots, prefix + what + ".png"));
                yield return new WaitForSecondsRealtime(0.5f);
            }

            Slot firstUsed = w._slots.FirstOrDefault(s => !s.Free);
            int firstSave = 0;
            if (firstUsed != null)
            {
                firstSave = firstUsed.Id;
            }

            if (shots != null)
            {
                yield return Shot("start-save");
                w._listOpen = true;
                yield return Shot("start-list");
                w.Choose(0);
                yield return Shot("start-new");
                w._host = true;
                Plugin.Cfg.HostWith.Value = "direct";
                yield return Shot("start-new-host");
                if (firstSave > 0)
                {
                    w.Choose(firstSave);
                    yield return Shot("start-save-host");
                    var s = w.Chosen;
                    Confirm.Ask(
                        "Delete save",
                        $"Delete the save in slot {s.Id} "
                            + $"({Summary(s.Profile)})? It can't be undone.",
                        "Delete",
                        () => Plugin.Log.LogInfo(
                            "[start window] (not deleted)"));
                    yield return Shot("start-delete");
                    Confirm.Cancel();
                }

                w._host = false;
            }

            if (start == null)
            {
                w.Close();
                if (sandbox != null)
                {
                    SaveRedirect.End();
                }

                Plugin.Log.LogInfo("[start window] done");
                yield break;
            }

            int slot;
            if (start == "new")
            {
                slot = 0;
            }
            else
            {
                slot = int.Parse(start);
            }

            w.Choose(slot);
            if (name != null)
            {
                w._name = name;
            }

            w._host = hostWith != null;
            if (hostWith != null)
            {
                Plugin.Cfg.HostWith.Value = hostWith;
            }

            yield return Shot("start-go");
            string target;
            if (slot == 0)
            {
                target = $"new game in slot {w.NewSlot}";
            }
            else
            {
                target = $"slot {slot}";
            }

            string hosting = hostWith;
            if (hosting == null)
            {
                hosting = "no";
            }

            string answer = w.CantStart();
            if (answer == null)
            {
                answer = "yes";
            }

            Plugin.Log.LogInfo(
                $"[start window] pressing Start: {target}, host {hosting}; can "
                    + $"start: {answer}");
            w.Start();
        }

        private void Delete(int id)
        {
            var slot = _profiles.FirstOrDefault(p => p.id == id);
            if (slot == null)
            {
                return;
            }

            Plugin.Log.LogInfo($"[start game] delete slot {id}");
            // As the profiles screen's Delete (MainMenu.confirmBoxYes,
            // deleteProfile).
            Core.profiles = _profiles;
            Core.currentProfile = slot;
            Singleton<SaveManager>.Instance.deleteCurrentProfile();
            if (_choice == id)
            {
                _choice = -1;
            }

            _nameFor = -1;
            Refresh();
        }
    }
}
