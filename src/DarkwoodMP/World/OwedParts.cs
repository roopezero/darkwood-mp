using System;
using System.Collections.Generic;
using System.IO;
using DarkwoodMP.Net;
using DarkwoodMP.World.Owed;
using HarmonyLib;
using Newtonsoft.Json;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Who is still owed their own part of a once-only trigger that fired for
    /// someone else (ScriptedEvents.LaterStep: a trip through a hole or down a
    /// ladder, its messages, an ordinary item), kept so that it outlasts a
    /// join, a world reload and a restart of the host's game. It used to be
    /// filled only as each EventFired arrived, so a player who joined after
    /// someone else used a hole (or anyone, after the world was loaded again)
    /// found it fired and was stuck on the wrong side.
    ///
    /// - A joiner is owed every once-only trigger with such steps that is fired
    ///   in the world it loaded, except those it had its own part of in this
    ///   world (it set them off, or got its part since). It asks the host (kind
    ///   5) when its player sets one off.
    /// - The host remembers, by player key, which triggers each joiner had its
    ///   part of, and refuses a second one. It's kept in memory across world
    ///   reloads (as the joiners' characters are, so it matches where they
    ///   stand) and written beside the save when the host's game saves:
    ///   prof&lt;id&gt;/darkwoodmp_later.json.
    /// - The host's own player is owed only the triggers a joiner set off first
    ///   (the ones fired before the mod kept this, it went through itself).
    ///   That list goes back to the saved one when the host's world is loaded
    ///   again, as the host's player goes back to where the save has it.
    ///
    /// A trigger is named by its object's save id (or scene, name and place)
    /// and its index in the object's triggers.
    /// </summary>
    internal static class OwedParts
    {
        private static Session S => Session.Instance;

        // ---- naming a trigger -----------------------------------------------

        /// <summary>
        /// A name for this trigger that's the same in every game and after the
        /// world is loaded again.
        /// </summary>
        internal static string IdOf(global::EventTrigger t)
        {
            EventTriggers et = null;
            if (t != null)
            {
                et = t.thisEventTriggers;
            }

            if (et == null)
            {
                return null;
            }

            return OwedRules.IdOf(FactsOf(et, t));
        }

        private static TriggerNameFacts FactsOf(
            EventTriggers et, global::EventTrigger t)
        {
            var save = et.GetComponent<SaveableObject>();
            int saveId = 0;
            if (save != null)
            {
                saveId = save.uniqueId;
            }

            var p = et.transform.position;
            return new TriggerNameFacts
            {
                Saved = save != null,
                SaveId = saveId,
                Index = et.eventTriggers.IndexOf(t),
                Scene = et.gameObject.scene.name,
                Name = et.name,
                X = p.x,
                Z = p.z,
            };
        }

        // ---- a joiner's game ------------------------------------------------

        /// <summary>
        /// Joiner: triggers whose own part our player had in the world loaded
        /// now.
        /// </summary>
        private static readonly HashSet<global::EventTrigger> OursHad =
            new HashSet<global::EventTrigger>();
        private static int _oursScene;

        private static HashSet<global::EventTrigger> Ours()
        {
            int scene =
                UnityEngine.SceneManagement.SceneManager
                    .GetActiveScene().handle;
            if (scene != _oursScene)
            {
                OursHad.Clear();
                _oursScene = scene;
            }

            return OursHad;
        }

        // ---- the host's record ----------------------------------------------

        private static OwedRecord _data;
        // the profile folder _data.Had belongs to
        private static string _dir;
        // the loaded world _data.HostOwed belongs to
        private static int _owedScene;

        private static string ProfileDir()
        {
            var sm = Singleton<SaveManager>.Instance;
            if (sm == null || Core.currentProfile == null)
            {
                return null;
            }

            return Path.Combine(
                sm.baseSaveDirectory, "prof" + Core.currentProfile.id);
        }

        private static OwedRecord Read(string dir)
        {
            try
            {
                string path = null;
                if (dir != null)
                {
                    path = Path.Combine(dir, OwedRules.FileName);
                }

                if (path != null && File.Exists(path))
                {
                    OwedRecord d = JsonConvert.DeserializeObject<OwedRecord>(
                        File.ReadAllText(path));
                    if (d == null)
                    {
                        d = new OwedRecord();
                    }

                    d.Repair();
                    return d;
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning(
                    "Couldn't read who is owed their part of fired events in "
                        + $"{dir}: {e.Message}");
            }

            return new OwedRecord();
        }

        /// <summary>
        /// Host: the record for this world, read from beside its save the first
        /// time, and again on a new world.
        /// </summary>
        private static OwedRecord Data()
        {
            string dir = ProfileDir();
            int scene =
                UnityEngine.SceneManagement.SceneManager
                    .GetActiveScene().handle;
            RecordRead read = OwedRules.Reads(new RecordReadFacts
            {
                Loaded = _data != null,
                Profile = dir,
                RecordProfile = _dir,
                Scene = scene,
                OwedScene = _owedScene,
            });

            if (read == RecordRead.Whole)
            {
                _data = Read(dir);
                _dir = dir;
                _owedScene = scene;
            }
            else if (read == RecordRead.HostOwed)
            {
                // The world was loaded again (a reload, a new chapter): our
                // player is where the save has them, and so is what they're
                // owed. What the joiners had stays: their characters are kept
                // as they last sent them.
                _data.HostOwed = Read(dir).HostOwed;
                _owedScene = scene;
            }

            return _data;
        }

        private static string KeyOf(ulong peer)
        {
            string key = null;
            if (S != null)
            {
                S.Keys.TryGetValue(peer, out key);
            }

            if (OwedRules.UsesKey(key))
            {
                return Characters.KeyHash(key);
            }

            return OwedRules.Unkeyed(peer);
        }

        /// <summary>
        /// Host: write the record beside the save (the game saved).
        /// </summary>
        internal static void HostSaved()
        {
            string dir = ProfileDir();
            bool empty = false;
            if (_data != null)
            {
                empty = _data.Empty;
            }

            bool writes = OwedRules.Writes(new RecordSaveFacts
            {
                Snapshotting = WorldShare.Snapshotting,
                Client = Combat.NetEnemies.ClientMode,
                Loaded = _data != null,
                Profile = dir,
                RecordProfile = _dir,
                Empty = empty,
            });

            if (!writes)
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(dir);
                File.WriteAllText(
                    Path.Combine(dir, OwedRules.FileName),
                    JsonConvert.SerializeObject(
                        new { _data.Had, _data.HostOwed }));
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning(
                    "Couldn't write who is owed their part of fired events: "
                        + $"{e.Message}");
            }
        }

        // ---- what ScriptedEvents asks ---------------------------------------

        /// <summary>
        /// Whether our player is owed its own part of this fired once-only
        /// trigger (see the class comment).
        /// </summary>
        internal static bool Owed(global::EventTrigger t)
        {
            return OwedRules.Owed(FactsOf(t));
        }

        private static OwedFacts FactsOf(global::EventTrigger t)
        {
            var s = S;
            bool active = s != null && s.Active;
            if (t == null || !active)
            {
                return new OwedFacts { Trigger = t != null, Active = active };
            }

            if (s.IsHost)
            {
                return new OwedFacts
                {
                    Trigger = true,
                    Active = true,
                    Host = true,
                    HostOwed = Data().IsHostOwed(IdOf(t)),
                };
            }

            EventTriggers et = t.thisEventTriggers;
            bool alone = et != null && WorldActions.OursAlone(et.gameObject);
            bool had = false;
            if (et != null && !alone)
            {
                had = Ours().Contains(t);
            }

            return new OwedFacts
            {
                Trigger = true,
                Active = true,
                HasObject = et != null,
                OursAlone = alone,
                HadHere = had,
            };
        }

        /// <summary>
        /// Host: a joiner set off this once-only trigger first; our player is
        /// owed its own part of it.
        /// </summary>
        internal static bool HostOwe(global::EventTrigger t)
        {
            var s = S;
            string id = null;
            if (s != null && s.IsHost)
            {
                id = IdOf(t);
            }

            return id != null && Data().HostOwe(id);
        }

        /// <summary>
        /// Our player had (or is having now) its own part of this trigger: it
        /// isn't owed any more.
        /// </summary>
        internal static bool Had(global::EventTrigger t)
        {
            var s = S;
            if (t == null || s == null || !s.Active)
            {
                return false;
            }

            if (s.IsHost)
            {
                return Data().HostHad(IdOf(t));
            }

            return Ours().Add(t);
        }

        /// <summary>
        /// Host: whether this joiner had its own part of this trigger (set it
        /// off, or got it as a later player).
        /// </summary>
        internal static bool Given(ulong peer, global::EventTrigger t)
        {
            string id = IdOf(t);
            return id != null && Data().Given(KeyOf(peer), id);
        }

        /// <summary>
        /// Host: this joiner has its own part of this trigger now.
        /// </summary>
        internal static void Give(ulong peer, global::EventTrigger t)
        {
            string id = IdOf(t);
            if (id == null)
            {
                return;
            }

            Data().Give(KeyOf(peer), id);
        }

        /// <summary>
        /// The session ended: a joiner's record of its own goes (the host's
        /// stays with its world).
        /// </summary>
        internal static void SessionEnded() => OursHad.Clear();

        /// <summary>
        /// For the tests: the host's game saves and starts again (the record is
        /// written, then read back).
        /// </summary>
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

    /// <summary>
    /// Host: the game saved; write who is owed their part of fired events
    /// beside it.
    /// </summary>
    [HarmonyPatch(typeof(SaveManager), nameof(SaveManager.Save), new[]
    {
        typeof(bool),
        typeof(bool),
        typeof(bool),
        typeof(bool),
        typeof(bool),
        typeof(bool),
        typeof(bool)
    })]
    static class OwedPartsSavedPatch
    {
        static void Postfix() => OwedParts.HostSaved();
    }
}
