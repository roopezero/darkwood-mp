using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DarkwoodMP.Net;
using DarkwoodMP.World.Joining;
using Newtonsoft.Json;
using UnityEngine;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Host: the snapshot of its world saved for a joiner.
    /// </summary>
    public sealed partial class WorldShare
    {
        // =====================================================================
        // Host
        // =====================================================================

        private sealed class Snapshot
        {
            public byte[] Hash;
            public string ProfileJson;
            // in order, unpacked sizes
            public List<(string Name, int Size)> Files;
            // what goes over the wire: Deflate of the files back to back
            // (Packing 1), or the files (0)
            public byte[] Sent;
            public byte Packing;
            public int RawSize;     // the files' sizes added up
            public string Stamp;
            // SaveManager.currentUniqueID in it (SharedIds)
            public int IdCounter;
            // the loaded world it was made in
            public int Scene;
            public float MadeAt;
            // how long reading and packing it took (logged on the main thread)
            public string Timing;
        }

        private Snapshot _hostWorld;
        // What each joiner was offered, and in which loaded world (scene
        // handle): a reload makes the offer stale.
        private readonly Dictionary<ulong, (Snapshot World, int Scene)>
            _offered = new Dictionary<ulong, (Snapshot, int)>();
        // Joiners waiting for the world being made for them (PrepareWorld),
        // offered it when it's ready.
        private readonly HashSet<ulong> _waiting = new HashSet<ulong>();
        private bool _preparing;
        // A NeedSave for a world we no longer offer that joiner gets one
        // JoinWait, not one per piece in flight.
        private readonly Dictionary<ulong, float> _staleTold =
            new Dictionary<ulong, float>();

        /// <summary>
        /// The JoinWait reason while the host makes a snapshot for the joiner
        /// (the client shows its loading screen for it).
        /// </summary>
        public const string Preparing =
            "The host is saving its world for you...";

        /// <summary>
        /// True while the host saves a snapshot of its world for a joiner.
        /// </summary>
        public static bool Snapshotting
        {
            get
            {
                WorldShare share = Current;
                return share != null && share._snapshotting;
            }
        }

        private bool _snapshotting;
        private float _lastSnapshot = -999f;
        private int _lastSnapshotScene;
        private static readonly float SnapshotReuse =
            JoinGateRules.SnapshotReuse.Value;

        /// <summary>
        /// Where every loose object is (DragSync.NoteSnapshot) and what the
        /// story is (FlagsSync.NoteSnapshot) in the snapshot being read,
        /// while a session runs.
        /// </summary>
        private static void NoteSnapshot()
        {
            CoopSession coop = Coop.Current;
            if (coop != null)
            {
                coop.Drags.NoteSnapshot();
                coop.Story.NoteSnapshot();
            }
        }

        /// <summary>
        /// The world as it is now, for someone joining: the host's game saves
        /// it, as it would save it, into a folder of the mod's (its own save is
        /// left as it was), with the night-time and loading checks forced past.
        /// Sets _snapshotDir to that folder's profile directory, or to null to
        /// fall back on the host's last save.
        ///
        /// Only the dynamic part (sav.dat: the player, the clock, every object
        /// that moves or changes) is saved. The static part (savs.dat: the
        /// map's layout and its pathfinding graph, about 9 MB, and savch.dat
        /// after chapter 1) is copied from the host's own save, as the game
        /// itself does: it writes savs.dat only when the world is made or a new
        /// location has been added (SaveManager.Save), and in that case it
        /// writes it into the snapshot too. Saving savs.dat would be most of
        /// the snapshot's time, on the host's main thread.
        ///
        /// When the static part was copied, sav.dat is saved by SnapshotSave:
        /// the world is read in one frame, as the game reads it, and written
        /// out as JSON on a worker thread (the writing was most of the stop).
        /// If that fails, or the static part has to be saved too, the game's
        /// own SaveManager.Save does it all on the main thread.
        /// </summary>
        private SnapshotFacts FactsOf(SaveManager sm, string dir)
        {
            bool saves = sm != null;
            bool dontSave = saves && sm.dontSave;
            bool hasSave = false;
            if (saves && !dontSave && !Core.loadingGame)
            {
                string profDir = ProfDir(dir, Core.currentProfile.id);
                hasSave = File.Exists(Path.Combine(profDir, "sav.dat"));
            }

            return new SnapshotFacts
            {
                Saves = saves,
                DontSave = dontSave,
                Loading = Core.loadingGame,
                Now = new Seconds(Time.realtimeSinceStartup),
                LastAt = new Seconds(_lastSnapshot),
                LastScene = _lastSnapshotScene,
                Scene = ActiveScene,
                HasSave = hasSave,
            };
        }

        private IEnumerator SaveSnapshot()
        {
            _snapshotDir = null;
            var sm = Singleton<SaveManager>.Instance;
            string dir = Path.Combine(_cacheDir, "..", "host-snapshot");
            var plan = SnapshotRules.Plan(FactsOf(sm, dir));
            if (plan == SnapshotStep.Skip)
            {
                yield break;
            }

            string profDir = ProfDir(dir, Core.currentProfile.id);
            if (plan == SnapshotStep.Reuse)
            {
                _snapshotDir = profDir;
                yield break;
            }

            // before the redirect below
            string ownDir =
                ProfDir(sm.baseSaveDirectory, Core.currentProfile.id);
            var lastSaved = sm.lastTimeSaved;
            bool haveSaved = sm.haveSaved;
            var outside = Singleton<OutsideLocations>.Instance;
            bool newLocation = outside != null && outside.addedNewLocation;
            bool haveStatic = false;
            long copied = 0;
            System.Threading.Tasks.Task<long> writing = null;
            void Restore() => RestoreAfterSnapshot(
                sm, lastSaved, haveSaved, outside, newLocation);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                RemoveDir(dir);
                Directory.CreateDirectory(profDir);
                foreach (var n in new[] { "savs.dat", "savch.dat" })
                {
                    string from = Path.Combine(ownDir, n);
                    if (!File.Exists(from))
                    {
                        continue;
                    }

                    File.Copy(from, Path.Combine(profDir, n));
                    if (n == "savs.dat")
                    {
                        haveStatic = true;
                    }
                }

                copied = sw.ElapsedMilliseconds;
                if (SnapshotRules.OffThread(
                    new OffThreadFacts
                    {
                        HaveStatic = haveStatic,
                        NewLocation = newLocation,
                        Setting = Plugin.Cfg.SnapshotOffThread.Value,
                        Broken = _offThreadBroken,
                    }))
                {
                    _snapshotting = true;
                    Plugin.Instance.Host.SaveFolders.BeginTemporary(
                        Path.GetFullPath(dir));
                    try
                    {
                        // the same frame the world is read in
                        NoteSnapshot();
                        writing = SnapshotSave.Begin(
                            sm, Path.Combine(profDir, "sav.dat"));
                        // as the patch on SaveManager.Save records it
                        SharedIds.OnSaved(sm.currentUniqueID);
                    }
                    catch (Exception e)
                    {
                        // most likely a game member it uses is missing: don't
                        // try again this run
                        _offThreadBroken = true;
                        Plugin.Log.LogWarning(
                            "Couldn't read the world for a snapshot off the "
                                + "main thread, saving it the game's way: "
                                + $"{e.GetBaseException().Message}");
                    }
                    finally
                    {
                        Plugin.Instance.Host.SaveFolders.EndTemporary();
                        // SnapshotSave.Begin set them to the snapshot folder's
                        sm.updateFilePaths();
                        _snapshotting = false;
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning(
                    "Couldn't snapshot the world, offering the last save "
                        + $"instead: {e.Message}");
                Restore();
                yield break;
            }

            if (writing != null)
            {
                // the main thread's part: copying the static part and reading
                // the world
                long stopped = sw.ElapsedMilliseconds;
                LastSnapshotStopMs = stopped;
                while (!writing.IsCompleted)
                {
                    yield return null;
                }

                if (!writing.IsFaulted
                    && File.Exists(Path.Combine(profDir, "sav.dat")))
                {
                    Plugin.Log.LogInfo(
                        "Saved a snapshot of the world for a joiner: the game "
                            + $"stopped for {stopped} ms (static part copied "
                            + $"from your save in {copied} ms, the world read "
                            + $"in {stopped - copied} ms), sav.dat written in "
                            + $"{writing.Result} ms on a worker thread");
                    Restore();
                    _lastSnapshot = Time.realtimeSinceStartup;
                    _lastSnapshotScene = ActiveScene;
                    _snapshotDir = profDir;
                    LastSnapshotOffThread = true;
                    yield break;
                }

                // Most likely the game changed a list while it was being
                // written. Save it the game's way instead.
                AggregateException error = writing.Exception;
                string why = null;
                if (error != null)
                {
                    why = error.GetBaseException().Message;
                }

                Plugin.Log.LogWarning(
                    "Couldn't write the snapshot off the main thread, saving "
                        + $"it the game's way: {why}");
                if (!SnapshotRules.FallsBack(InWorld, Core.loadingGame))
                {
                    Restore();
                    yield break;
                }
            }

            NoteSnapshot();
            _snapshotDir = SaveSnapshotOnMainThread(
                sm, dir, profDir, haveStatic, copied, newLocation);
            Restore();
        }

        private string _snapshotDir;
        /// <summary>
        /// Set when SnapshotSave couldn't read this game's world: only the
        /// game's own save is used after.
        /// </summary>
        private bool _offThreadBroken;
        /// <summary>
        /// How long the host's game stopped for the last snapshot, in ms (-1:
        /// none yet).
        /// </summary>
        public long LastSnapshotStopMs { get; private set; } = -1;
        /// <summary>
        /// Whether the last snapshot's sav.dat was written on a worker thread
        /// (SnapshotSave), not by the game's save.
        /// </summary>
        public bool LastSnapshotOffThread { get; private set; }
        /// <summary>
        /// The longest host frame while the last joiner joined, in ms (-1: none
        /// yet).
        /// </summary>
        public float LastJoinLongestFrameMs { get; private set; } = -1f;

        /// <summary>
        /// The snapshot saved by the game's own SaveManager.Save, in one go on
        /// the main thread.
        /// </summary>
        private string SaveSnapshotOnMainThread(
            SaveManager sm, string dir, string profDir, bool haveStatic,
            long copied, bool newLocation)
        {
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                _snapshotting = true;
                Plugin.Instance.Host.SaveFolders.BeginTemporary(
                    Path.GetFullPath(dir));
                sm.Save(
                    doJson: true, doSaveProfile: true, force: true,
                    forceSaveStatic: !haveStatic, showSavingIndicator: false,
                    closeAndOpenStadiaSave: false);
                LastSnapshotStopMs = sw.ElapsedMilliseconds + copied;
                LastSnapshotOffThread = false;
                string text = "static part saved too: your save hasn't got one";
                if (haveStatic)
                {
                    text = $"static part copied from your save in {copied} ms";
                }

                string locationNote = "";
                if (newLocation)
                {
                    locationNote = ", with a new location's static part";
                }

                Plugin.Log.LogInfo(
                    "Saved a snapshot of the world for a joiner the game's "
                        + $"way: the game stopped for {LastSnapshotStopMs} ms "
                        + $"({text}{locationNote})");
                _lastSnapshot = Time.realtimeSinceStartup;
                _lastSnapshotScene = ActiveScene;
                if (File.Exists(Path.Combine(profDir, "sav.dat")))
                {
                    return profDir;
                }

                return null;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning(
                    "Couldn't snapshot the world, offering the last save "
                        + $"instead: {e.Message}");
                return null;
            }
            finally
            {
                Plugin.Instance.Host.SaveFolders.EndTemporary();
                sm.updateFilePaths();
                _snapshotting = false;
            }
        }

        private static void RestoreAfterSnapshot(
            SaveManager sm, DateTime lastSaved, bool haveSaved,
            OutsideLocations outside, bool newLocation)
        {
            // the menu's "progress since your last save" is about your own save
            sm.lastTimeSaved = lastSaved;
            sm.haveSaved = haveSaved;
            // The game clears this once it has saved the static part; that was
            // the snapshot's, so the host's own next save must still write its
            // own.
            if (outside != null && newLocation)
            {
                outside.addedNewLocation = true;
            }
        }

        /// <summary>
        /// The world made for joiners lately, if it's still the one to give
        /// them (null: make another).
        /// </summary>
        private Snapshot FreshHostWorld()
        {
            var w = _hostWorld;
            if (!JoinGateRules.Fresh(FactsOf(w)))
            {
                return null;
            }

            return w;
        }

        private static FreshFacts FactsOf(Snapshot made)
        {
            int scene = ActiveScene;
            bool live = SourceNow() == WorldSource.Live;
            var sm = Singleton<SaveManager>.Instance;
            bool saves = sm != null && Core.currentProfile != null;
            if (made == null)
            {
                return new FreshFacts { Scene = scene };
            }

            // the save files' stamp is read only when it's what counts
            string stamp = null;
            if (saves && !live && made.Scene == scene)
            {
                stamp = WorldFiles.Stamp(
                    ProfDir(sm.baseSaveDirectory, Core.currentProfile.id));
            }

            return new FreshFacts
            {
                Made = true,
                MadeScene = made.Scene,
                MadeAt = new Seconds(made.MadeAt),
                MadeStamp = made.Stamp,
                Scene = scene,
                Now = new Seconds(Time.realtimeSinceStartup),
                LiveWorld = live,
                Saves = saves,
                Stamp = stamp,
            };
        }
    }
}
