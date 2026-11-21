using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using DarkwoodMP.Net;
using UnityEngine;

namespace DarkwoodMP.World
{
    /// <summary>
    /// The test world while the game runs: loading it from the main menu
    /// (and what went wrong, for the panel), and, in it, building the test
    /// area once and the labels drawn over its things (TestWorld). ModHost
    /// owns it: the test world is loaded from the main menu, with no session.
    /// </summary>
    internal sealed class TestArea
    {
        public string Status { get; private set; }

        /// <summary>
        /// Load the test world (copying your saves first if there's no copy
        /// yet, or to reset it).
        /// </summary>
        public IEnumerator Start(WorldShare world, bool reset)
        {
            Status = null;
            while (!WorldShare.MainMenuReady())
            {
                yield return null;
            }

            if (reset || !TestWorld.Exists)
            {
                // no redirect in the main menu: your own
                string mine = Singleton<SaveManager>.Instance.baseSaveDirectory;
                if (SaveRedirect.Active || string.IsNullOrEmpty(mine)
                    || !Directory.Exists(mine))
                {
                    Status =
                        "No saves of yours to copy: play a world once first.";
                    yield break;
                }

                try
                {
                    WorldShare.RemoveDir(TestWorld.Dir);
                    Copy(mine, TestWorld.Dir);
                    Characters instance = Characters.Instance;
                    if (instance != null)
                    {
                        instance.Forget("the test world reset");
                    }
                }
                catch (Exception e)
                {
                    Status = $"Couldn't copy your saves: {e.Message}";
                    Plugin.Log.LogError(Status);
                    yield break;
                }

                Plugin.Log.LogInfo(
                    "Test world: a fresh copy of your saves in "
                        + $"{TestWorld.Dir}");
            }

            // A living profile in the copy (the first one).
            SaveRedirect.Begin(TestWorld.Dir);
            var state = Singleton<SaveManager>.Instance.loadGameProfiles();
            GameProfile profile = null;
            if (state != null)
            {
                List<GameProfile> profiles = state.profiles;
                if (profiles != null)
                {
                    profile = profiles.Where(p => p != null && p.Active)
                        .OrderBy(p => p.dead).ThenBy(p => p.id)
                        .FirstOrDefault();
                }
            }

            if (profile == null)
            {
                SaveRedirect.End();
                Status =
                    "The test world has no profile to load: reset it after "
                        + "playing a world.";
                yield break;
            }

            Plugin.Log.LogInfo(
                $"Test world: loading profile {profile.id} (day "
                    + $"{profile.day})");
            yield return world.ContinueOwnProfile(profile.id, TestWorld.Dir);
        }

        private void Copy(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (var f in Directory.GetFiles(from))
            {
                File.Copy(
                    f, Path.Combine(to, Path.GetFileName(f)), overwrite: true);
            }

            foreach (var d in Directory.GetDirectories(from))
            {
                Copy(d, Path.Combine(to, Path.GetFileName(d)));
            }
        }



        private readonly List<(Transform t, string label)> _labels =
            new List<(Transform, string)>();
        private bool _checked;
        private float _nextLabelScan;

        public void Update()
        {
            if (!TestWorld.Active || !WorldShare.WorldReady)
            {
                _checked = false;
                _labels.Clear();
                return;
            }

            if (!_checked && Player.Instance != null)
            {
                // Built before, and saved since? If none of it is here (the
                // world wasn't saved after), build it again.
                _checked = true;
                FindLabels();
                // Built in another chapter (the map is a new one): not built
                // again here, so the kit isn't handed out a second time on top
                // of the player's things from the last chapter.
                int built = TestWorld.AreaChapter();
                if (_labels.Count == 0 && built != 0
                    && built != Chapters.HostChapter)
                {
                    Plugin.Log.LogInfo(
                        $"Test world: built in chapter {built}, not again in "
                            + $"chapter {Chapters.HostChapter}");
                }
                else if (_labels.Count == 0)
                {
                    Vector3? center = TestWorld.Center();
                    Vector3 at;
                    if (center.HasValue)
                    {
                        at = center.Value;
                    }
                    else
                    {
                        at = Player.Instance.transform.position;
                    }

                    TestWorld.Build(at);
                }
            }

            if (Time.unscaledTime >= _nextLabelScan)
            {
                _nextLabelScan = Time.unscaledTime + 2f;
                int was = _labels.Count;
                FindLabels();
                if (_labels.Count != was)
                {
                    Plugin.Log.LogInfo(
                        $"Test world: {_labels.Count} labelled things around "
                            + $"{TestWorld.Center()}");
                }
            }
        }

        // ---- labels ---------------------------------------------------------

        private void FindLabels()
        {
            _labels.Clear();
            var c = TestWorld.Center();
            if (c == null)
            {
                return;
            }

            var center = c.Value;
            var names = new Dictionary<string, string>();
            foreach (var st in TestWorld.Stations)
            {
                names[st.Prefab.Substring(st.Prefab.LastIndexOf('/') + 1)] =
                    st.Label;
            }

            // Placed at the top of the scene (or in the game's item container,
            // when it has one by then).
            var near = new List<Transform>();
            foreach (var root in UnityEngine.SceneManagement.SceneManager
                .GetActiveScene().GetRootGameObjects())
            {
                near.Add(root.transform);
            }

            if (Core.ItemContainer != null)
            {
                foreach (Transform t in Core.ItemContainer.transform)
                {
                    near.Add(t);
                }
            }

            foreach (var npc in UnityEngine.Object.FindObjectsOfType<NPC>())
            {
                near.Add(npc.transform);
            }

            foreach (var t in near)
            {
                if (t == null
                    || (t.position - center).sqrMagnitude > 3000f * 3000f)
                {
                    continue;
                }

                string name = t.name.Replace("(Clone)", "").Trim();
                if (names.TryGetValue(name, out var label))
                {
                    _labels.Add((t, label));
                }
            }
        }

        public void DrawLabels()
        {
            if (!TestWorld.Active || _labels.Count == 0
                || Plugin.NameTagStyle == null)
            {
                return;
            }

            var cam = Camera.main;
            if (cam == null)
            {
                return;
            }

            foreach (var (t, label) in _labels)
            {
                if (t == null)
                {
                    continue;
                }

                var sp = cam.WorldToScreenPoint(t.position);
                if (sp.z < 0 || sp.x < 0 || sp.x > Screen.width || sp.y < 0
                    || sp.y > Screen.height)
                {
                    continue;
                }

                GUI.Label(
                    new Rect(sp.x - 160, Screen.height - sp.y + 30, 320, 40),
                    label, Plugin.NameTagStyle);
            }
        }
    }
}
