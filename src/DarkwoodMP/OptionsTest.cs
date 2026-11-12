using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using DarkwoodMP.World;
using UnityEngine;

namespace DarkwoodMP
{
    /// <summary>
    /// "--dwmp-test-options DIR" (Plugin): the Options window opened from the menu's list, in the main menu or (with
    /// --dwmp-continue) the pause menu. It changes the music volume and applies it, and checks the game's stored
    /// value (GameSettings, which is Unity's PlayerPrefs, and the prefs file on disk); it listens for a key and
    /// cancels with Escape, then puts F9 (or another free key) on an action and checks it was stored by reading the
    /// key maps back from the store. Then screenshots of every tab, an open list, a key being listened for, the
    /// "Keep these settings?" question and the "Apply the changes?" question, at 1920x1080 and 1280x720.
    ///
    /// These are the player's real game settings, so everything is put back: the key on the action, and every
    /// setting in the prefs file as it was before the test (read from the file, written back through PlayerPrefs,
    /// with keys the test added deleted), then compared with the file from before. On Linux the file is
    /// ~/.config/unity3d/Acid Wizard Studio/Darkwood/prefs; on Windows PlayerPrefs are in the registry
    /// (HKCU\Software\Acid Wizard Studio\Darkwood), which this test can't read, so it doesn't run there.
    /// </summary>
    internal static class OptionsTest
    {
        private static string PrefsPath => Path.Combine(Application.persistentDataPath, "prefs");
        private static void Log(string s) => Plugin.Log.LogInfo("[options] " + s);
        private static int _fails;

        private static void Check(bool ok, string what)
        {
            if (!ok) _fails++;
            Log($"{(ok ? "PASS" : "FAIL")} {what}");
        }

        public static IEnumerator Run(string dir, bool inWorld)
        {
            Directory.CreateDirectory(dir);
            _fails = 0;
            string where = inWorld ? "pause" : "main";
            if (!File.Exists(PrefsPath)) { Log($"FAIL no prefs file at {PrefsPath} (only Linux keeps them in a file): not run"); Log("done"); yield break; }
            if (!inWorld)
            {
                while (!WorldShare.MainMenuReady()) yield return null;
                yield return new WaitForSecondsRealtime(3f);
            }
            else
            {
                while (!WorldShare.WorldReady) yield return null;
                yield return new WaitForSecondsRealtime(4f);
                Core.showHidePauseMenu();
                yield return new WaitForSecondsRealtime(2f);
            }
            var before = ReadPrefs();
            string beforeText = File.ReadAllText(PrefsPath);
            File.WriteAllText(Path.Combine(dir, $"prefs-before-{where}.xml"), beforeText);
            var sizeBefore = (w: Screen.width, h: Screen.height, mode: Screen.fullScreenMode);
            Log($"{where} menu, screen {Screen.width}x{Screen.height}; prefs file {PrefsPath} with {before.Count} settings");

            // Through the menu's entry, as a click on it.
            bool took = MenuList.Click("Options");
            yield return null; yield return null;
            Check(took && OptionsWindow.IsOpen, $"the menu's Options opens the window (entry taken {took}, open {OptionsWindow.IsOpen})");
            if (!OptionsWindow.IsOpen) OptionsWindow.Open();

            // A volume: changed, applied, and stored where the game keeps it.
            OptionsWindow.TestTab(1);
            yield return null;
            float m0 = GameSettings.GetFloat("MusicVolume");
            float m1 = m0 > 0.5f ? m0 - 0.3f : m0 + 0.3f;
            OptionsWindow.SetVolume(music: true, m1);
            bool pending = OptionsWindow.Pending;
            var slider = Singleton<MainMenu>.Instance.AudioMenu.musicSlider;
            bool heard = slider == null || Mathf.Approximately(slider.Magnitude, m1);
            bool applied = OptionsWindow.Apply();
            yield return null;
            var after = ReadPrefs();
            float onDisk = after.TryGetValue("MusicVolume", out var mv) && float.TryParse(mv.value, NumberStyles.Float, CultureInfo.InvariantCulture, out float d) ? d : -1f;
            Check(pending && heard && applied && Mathf.Abs(GameSettings.GetFloat("MusicVolume") - m1) < 0.001f && Mathf.Abs(onDisk - m1) < 0.001f,
                $"music volume {m0:0.00} -> {m1:0.00}: waited for Apply {pending}, the game's slider at it {heard}, stored {GameSettings.GetFloat("MusicVolume"):0.00}, in the file {onDisk:0.00}");
            // Back at once, so nothing after this can leave the player's volume changed.
            OptionsWindow.SetVolume(music: true, m0);
            OptionsWindow.Apply();
            Check(Mathf.Approximately(GameSettings.GetFloat("MusicVolume"), m0), $"music volume back at {GameSettings.GetFloat("MusicVolume"):0.00}");

            // A key: listened for and cancelled with Escape, then put on an action and stored.
            OptionsWindow.TestTab(3);
            yield return null; yield return null;
            var rows = OptionsWindow.TestRows();
            string label = null;
            foreach (var r in rows) if (r.label == "Run" && r.key != null) label = r.label;
            if (label == null) foreach (var r in rows) if (r.key != null) { label = r.label; break; }
            KeyCode free = KeyCode.None;
            foreach (var k in new[] { KeyCode.F9, KeyCode.F10, KeyCode.F11, KeyCode.F8, KeyCode.Insert, KeyCode.Home })
                if (!OptionsWindow.TestKeyUsed(k)) { free = k; break; }
            Log($"{rows.Count} keyboard rows; trying {label} with {free}");
            if (label != null && free != KeyCode.None)
            {
                KeyCode orig = OptionsWindow.TestKeyOf(label);
                OptionsWindow.TestBeginMapping(label);
                float until = Time.realtimeSinceStartup + 3f;
                while (OptionsWindow.TestMapperStatus != "Listening" && Time.realtimeSinceStartup < until) yield return null;
                bool listened = OptionsWindow.TestMapperStatus == "Listening";
                bool gameHeld = Singleton<MainMenu>.Instance.ControlsMenu.assigningMapping;
                OptionsWindow.TestEscape();
                yield return null;
                Check(listened && gameHeld && !OptionsWindow.TestMapping && OptionsWindow.TestMapperStatus == "Idle" && OptionsWindow.TestKeyOf(label) == orig,
                    $"click on {label}'s key listens (Rewired's mapper {(listened ? "listening" : "not listening")}, the game's keys held {gameHeld}); Escape cancels, {label} still {orig}");

                bool set = OptionsWindow.TestAssign(label, free);
                var afterKey = ReadPrefs();
                bool keyStored = false;
                foreach (var kv in afterKey)
                    if (kv.Key.Contains("dataType=ControllerMap|") && kv.Key.Contains("KeyboardMap") && (!before.TryGetValue(kv.Key, out var old) || old.value != kv.Value.value)) keyStored = true;
                OptionsWindow.TestReloadControls();
                KeyCode loaded = OptionsWindow.TestKeyOf(label);
                Check(set && keyStored && loaded == free, $"{label} put on {free}: the keyboard map in the prefs file changed {keyStored}, read back from the store as {loaded}");

                // Back as it was, in the player's map too (the store is put back below).
                OptionsWindow.TestAssign(label, orig);
                Check(OptionsWindow.TestKeyOf(label) == orig, $"{label} back on {orig}");
            }
            else Check(false, "no keyboard row with a key, or no free key to try");

            // Screenshots of every tab at both sizes.
            foreach (var (w, h) in new[] { (1920, 1080), (1280, 720) })
            {
                Screen.SetResolution(w, h, FullScreenMode.Windowed);
                yield return new WaitForSecondsRealtime(2.5f);
                Log($"screen asked {w}x{h}, got {Screen.width}x{Screen.height}");
                string P(string n) => Path.Combine(dir, $"{where}-{w}x{h}-{n}.png");
                var names = OptionsWindow.TestTabNames;
                for (int t = 0; t < names.Length; t++)
                {
                    OptionsWindow.TestTab(t);
                    yield return Shot(P($"{t + 1}-{names[t].ToLowerInvariant()}"));
                }
                OptionsWindow.TestTab(0);
                OptionsWindow.TestOpenDrop("res");
                yield return Shot(P("video-resolution-list"));
                OptionsWindow.TestTab(2);
                OptionsWindow.TestOpenDrop("lang");
                yield return Shot(P("game-language-list"));
                OptionsWindow.TestTab(3);
                yield return null;
                if (label != null)
                {
                    OptionsWindow.TestBeginMapping(label);
                    yield return Shot(P("controls-listening"));
                    OptionsWindow.TestEscape();
                }
                OptionsWindow.TestTab(0);
                OptionsWindow.AskKeep($"The screen is now {Screen.width}x{Screen.height}, in a window.", () => { }, () => { });
                yield return Shot(P("video-keep"));
                ConfirmDialog.Cancel();
                OptionsWindow.TestTab(1);
                OptionsWindow.SetVolume(music: false, GameSettings.GetFloat("SoundVolume") > 0.5f ? 0.3f : 0.8f);
                OptionsWindow.RequestClose();
                yield return Shot(P("audio-apply-question"));
                ConfirmDialog.Choose(false); // Discard: the sound volume back, the window closed
                yield return null; yield return null;
                Check(!OptionsWindow.IsOpen && !OptionsWindow.Pending, $"Discard closes the window with nothing left to apply (open {OptionsWindow.IsOpen})");
                OptionsWindow.Open();
                yield return null;
            }

            // Everything back as it was.
            OptionsWindow.RequestClose();
            Screen.SetResolution(sizeBefore.w, sizeBefore.h, sizeBefore.mode);
            yield return new WaitForSecondsRealtime(1f);
            RestorePrefs(before);
            OptionsWindow.Revert();       // the volumes heard as stored
            OptionsWindow.TestReloadControls();
            var diffs = Compare(before, ReadPrefs());
            Check(diffs.Count == 0, diffs.Count == 0 ? "the prefs file has every setting as before the test" : $"the prefs file differs from before: {string.Join("; ", diffs)}");
            if (inWorld) Core.showHidePauseMenu();
            Log($"done: {(_fails == 0 ? "all passed" : _fails + " failed")}");
        }

        private static IEnumerator Shot(string path)
        {
            yield return new WaitForSecondsRealtime(0.8f);
            ScreenCapture.CaptureScreenshot(path);
            yield return new WaitForSecondsRealtime(0.5f);
            Log($"shot {Path.GetFileName(path)}");
        }

        // ---- the prefs file ------------------------------------------------------------------------

        private static readonly Regex PrefRx = new Regex("<pref name=\"([^\"]*)\" type=\"([a-z]+)\">([^<]*)</pref>");

        /// <summary>Unity's own entries, which it changes itself (the window's size and place, sessions).</summary>
        private static bool Unity(string name) => name.StartsWith("unity.") || name.StartsWith("Screenmanager") || name == "UnitySelectMonitor";

        private static Dictionary<string, (string type, string value)> ReadPrefs()
        {
            PlayerPrefs.Save();
            var d = new Dictionary<string, (string, string)>();
            foreach (Match m in PrefRx.Matches(File.ReadAllText(PrefsPath)))
                d[System.Net.WebUtility.HtmlDecode(m.Groups[1].Value)] = (m.Groups[2].Value, m.Groups[3].Value);
            return d;
        }

        private static void RestorePrefs(Dictionary<string, (string type, string value)> before)
        {
            var now = ReadPrefs();
            foreach (var kv in before)
            {
                if (Unity(kv.Key) || (now.TryGetValue(kv.Key, out var cur) && cur == kv.Value)) continue;
                try
                {
                    switch (kv.Value.type)
                    {
                        case "int": PlayerPrefs.SetInt(kv.Key, int.Parse(kv.Value.value, CultureInfo.InvariantCulture)); break;
                        case "float": PlayerPrefs.SetFloat(kv.Key, float.Parse(kv.Value.value, CultureInfo.InvariantCulture)); break;
                        default: PlayerPrefs.SetString(kv.Key, Encoding.UTF8.GetString(Convert.FromBase64String(kv.Value.value))); break;
                    }
                    Log($"put back {Short(kv.Key)}");
                }
                catch (Exception e) { Log($"couldn't put back {Short(kv.Key)}: {e.Message}"); }
            }
            foreach (var kv in now)
                if (!Unity(kv.Key) && !before.ContainsKey(kv.Key)) { PlayerPrefs.DeleteKey(kv.Key); Log($"deleted the test's {Short(kv.Key)}"); }
            PlayerPrefs.Save();
        }

        private static List<string> Compare(Dictionary<string, (string type, string value)> a, Dictionary<string, (string type, string value)> b)
        {
            var diffs = new List<string>();
            foreach (var kv in a)
                if (!Unity(kv.Key) && (!b.TryGetValue(kv.Key, out var v) || v != kv.Value)) diffs.Add($"{Short(kv.Key)} {kv.Value.value} -> {(b.ContainsKey(kv.Key) ? v.value : "gone")}");
            foreach (var kv in b)
                if (!Unity(kv.Key) && !a.ContainsKey(kv.Key)) diffs.Add($"{Short(kv.Key)} added");
            return diffs;
        }

        private static string Short(string key) => key.Length > 70 ? key.Substring(0, 70) + "..." : key;
    }
}
