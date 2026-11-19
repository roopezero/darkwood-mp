using System;
using System.Collections.Generic;
using System.IO;
using DarkwoodMP.Net;
using HarmonyLib;
using Newtonsoft.Json;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Who is still owed their own part of a once-only trigger that fired for someone else (ScriptedEvents.LaterStep:
    /// a trip through a hole or down a ladder, its messages, an ordinary item), kept so that it outlasts a join, a
    /// world reload and a restart of the host's game. It used to be filled only as each EventFired arrived, so a
    /// player who joined after someone else used a hole (or anyone, after the world was loaded again) found it fired
    /// and was stuck on the wrong side.
    ///
    /// - A joiner is owed every once-only trigger with such steps that is fired in the world it loaded, except those
    ///   it had its own part of in this world (it set them off, or got its part since). It asks the host (kind 5) when
    ///   its player sets one off.
    /// - The host remembers, by player key, which triggers each joiner had its part of, and refuses a second one. It's
    ///   kept in memory across world reloads (as the joiners' characters are, so it matches where they stand) and
    ///   written beside the save when the host's game saves: prof&lt;id&gt;/darkwoodmp_later.json.
    /// - The host's own player is owed only the triggers a joiner set off first (the ones fired before the mod kept
    ///   this, it went through itself). That list goes back to the saved one when the host's world is loaded again,
    ///   as the host's player goes back to where the save has it.
    ///
    /// A trigger is named by its object's save id (or scene, name and place) and its index in the object's triggers.
    /// </summary>
    internal static class OwedParts
    {
        private sealed class FileData
        {
            public Dictionary<string, HashSet<string>> Had = new Dictionary<string, HashSet<string>>();
            public HashSet<string> HostOwed = new HashSet<string>();
        }

        private static Session S => Session.Instance;

        // ---- naming a trigger ---------------------------------------------------------------------------

        /// <summary>A name for this trigger that's the same in every game and after the world is loaded again.</summary>
        internal static string IdOf(global::EventTrigger t)
        {
            var et = t?.thisEventTriggers;
            if (et == null)
            {
                return null;
            }
            int index = et.eventTriggers.IndexOf(t);
            var save = et.GetComponent<SaveableObject>();
            if (save != null && save.uniqueId > 0)
            {
                return $"s{save.uniqueId}#{index}";
            }
            var p = et.transform.position;
            return FormattableString.Invariant($"{et.gameObject.scene.name}/{et.name}@{p.x:0},{p.z:0}#{index}");
        }

        // ---- a joiner's game ----------------------------------------------------------------------------

        /// <summary>Joiner: triggers whose own part our player had in the world loaded now.</summary>
        private static readonly HashSet<global::EventTrigger> OursHad = new HashSet<global::EventTrigger>();
        private static int _oursScene;

        private static HashSet<global::EventTrigger> Ours()
        {
            int scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle;
            if (scene != _oursScene)
            {
                OursHad.Clear();
                _oursScene = scene;
            }
            return OursHad;
        }

        // ---- the host's record --------------------------------------------------------------------------

        private static FileData _data;
        private static string _dir;        // the profile folder _data.Had belongs to
        private static int _owedScene;     // the loaded world _data.HostOwed belongs to

        private static string ProfileDir()
        {
            var sm = Singleton<SaveManager>.Instance;
            if (sm == null || Core.currentProfile == null)
            {
                return null;
            }
            return Path.Combine(sm.baseSaveDirectory, "prof" + Core.currentProfile.id);
        }

        private static FileData Read(string dir)
        {
            try
            {
                string path = dir != null ? Path.Combine(dir, "darkwoodmp_later.json") : null;
                if (path != null && File.Exists(path))
                {
                    var d = JsonConvert.DeserializeObject<FileData>(File.ReadAllText(path)) ?? new FileData();
                    if (d.Had == null)
                    {
                        d.Had = new Dictionary<string, HashSet<string>>();
                    }
                    if (d.HostOwed == null)
                    {
                        d.HostOwed = new HashSet<string>();
                    }
                    return d;
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Couldn't read who is owed their part of fired events in {dir}: {e.Message}");
            }
            return new FileData();
        }

        /// <summary>Host: the record for this world, read from beside its save the first time, and again on a new world.</summary>
        private static FileData Data()
        {
            string dir = ProfileDir();
            int scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle;
            if (_data == null || dir != _dir)
            {
                _data = Read(dir);
                _dir = dir;
                _owedScene = scene;
            }
            else if (scene != _owedScene)
            {
                // The world was loaded again (a reload, a new chapter): our player is where the save has them, and so
                // is what they're owed. What the joiners had stays: their characters are kept as they last sent them.
                _data.HostOwed = Read(dir).HostOwed;
                _owedScene = scene;
            }
            return _data;
        }

        private static string KeyOf(ulong peer) =>
            S != null && S.Keys.TryGetValue(peer, out var key) && !string.IsNullOrEmpty(key) ? Characters.KeyHash(key) : "#" + peer;

        /// <summary>Host: write the record beside the save (the game saved).</summary>
        internal static void HostSaved()
        {
            if (WorldShare.Snapshotting || Combat.NetEnemies.ClientMode || _data == null)
            {
                return;
            }
            string dir = ProfileDir();
            if (dir == null || dir != _dir || (_data.Had.Count == 0 && _data.HostOwed.Count == 0))
            {
                return;
            }
            try
            {
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "darkwoodmp_later.json"), JsonConvert.SerializeObject(_data));
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Couldn't write who is owed their part of fired events: {e.Message}");
            }
        }

        // ---- what ScriptedEvents asks -------------------------------------------------------------------

        /// <summary>Whether our player is owed its own part of this fired once-only trigger (see the class comment).</summary>
        internal static bool Owed(global::EventTrigger t)
        {
            var s = S;
            if (t == null || s == null || !s.Active)
            {
                return false;
            }
            if (s.IsHost)
            {
                string id = IdOf(t);
                return id != null && Data().HostOwed.Contains(id);
            }
            if (t.thisEventTriggers == null || WorldActions.OursAlone(t.thisEventTriggers.gameObject))
            {
                return false;
            }
            return !Ours().Contains(t);
        }

        /// <summary>Host: a joiner set off this once-only trigger first; our player is owed its own part of it.</summary>
        internal static bool HostOwe(global::EventTrigger t)
        {
            var s = S;
            string id = s != null && s.IsHost ? IdOf(t) : null;
            return id != null && Data().HostOwed.Add(id);
        }

        /// <summary>Our player had (or is having now) its own part of this trigger: it isn't owed any more.</summary>
        internal static bool Had(global::EventTrigger t)
        {
            var s = S;
            if (t == null || s == null || !s.Active)
            {
                return false;
            }
            if (s.IsHost)
            {
                string id = IdOf(t);
                return id != null && Data().HostOwed.Remove(id);
            }
            return Ours().Add(t);
        }

        /// <summary>Host: whether this joiner had its own part of this trigger (set it off, or got it as a later player).</summary>
        internal static bool Given(ulong peer, global::EventTrigger t)
        {
            string id = IdOf(t);
            return id != null && Data().Had.TryGetValue(KeyOf(peer), out var set) && set.Contains(id);
        }

        /// <summary>Host: this joiner has its own part of this trigger now.</summary>
        internal static void Give(ulong peer, global::EventTrigger t)
        {
            string id = IdOf(t);
            if (id == null)
            {
                return;
            }
            var had = Data().Had;
            string key = KeyOf(peer);
            if (!had.TryGetValue(key, out var set))
            {
                had[key] = set = new HashSet<string>();
            }
            set.Add(id);
        }

        /// <summary>The session ended: a joiner's record of its own goes (the host's stays with its world).</summary>
        internal static void SessionEnded() => OursHad.Clear();

        /// <summary>For the tests: the host's game saves and starts again (the record is written, then read back).</summary>
        internal static void TestReload()
        {
            if (_data == null)
            {
                Data();
            }
            HostSaved();
            _data = null;
        }
    }

    /// <summary>Host: the game saved; write who is owed their part of fired events beside it.</summary>
    [HarmonyPatch(typeof(SaveManager), nameof(SaveManager.Save), new[] { typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(bool) })]
    static class OwedPartsSavedPatch
    {
        static void Postfix() => OwedParts.HostSaved();
    }
}
