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
    /// Host: the world made ready and offered, and the join gate.
    /// </summary>
    public sealed partial class WorldShare
    {
        private IEnumerator PrepareWorld()
        {
            _preparing = true;
            Snapshot world = null;
            // an exception in here must not leave _preparing set: every later
            // joiner would wait for ever
            try
            {
                yield return null; // the JoinWait goes out first
                int scene = ActiveScene;
                var sm = Singleton<SaveManager>.Instance;
                if (InWorld && sm != null)
                {
                    string snapDir = null;
                    if (Plugin.Cfg.LiveWorld.Value)
                    {
                        yield return SaveSnapshot();
                        snapDir = _snapshotDir;
                    }
                    else // the last save: what was where then isn't known
                    {
                        CoopSession coop = Coop.Current;
                        if (coop != null)
                        {
                            coop.Drags.ForgetSnapshot();
                            coop.Story.ForgetSnapshot();
                        }
                    }

                    string profDir = snapDir;
                    if (profDir == null && Core.currentProfile != null)
                    {
                        profDir = ProfDir(
                            sm.baseSaveDirectory, Core.currentProfile.id);
                    }

                    // no save, or we left the world meanwhile: nothing to offer
                    string stamp;
                    if (profDir == null || !InWorld)
                    {
                        stamp = "";
                    }
                    else
                    {
                        stamp = WorldFiles.Stamp(profDir);
                    }

                    if (_hostWorld != null
                        && SnapshotRules.Reuses(
                            stamp, _hostWorld.Stamp, _hostWorld.Scene == scene))
                    {
                        world = _hostWorld;
                    }
                    else if (stamp.Length > 0)
                    {
                        string json =
                            JsonConvert.SerializeObject(Core.currentProfile);
                        int ids = SharedIds.CounterOf(snapDir != null);
                        int day = Core.currentProfile.day;
                        var job = System.Threading.Tasks.Task.Run(
                            () => Pack(profDir, json));
                        while (!job.IsCompleted)
                        {
                            yield return null;
                        }

                        if (job.IsFaulted)
                        {
                            AggregateException error = job.Exception;
                            string why = null;
                            if (error != null)
                            {
                                why = error.GetBaseException().Message;
                            }

                            Plugin.Log.LogWarning(
                                $"Couldn't read the world for joiners: {why}");
                        }
                        else if ((world = job.Result) != null)
                        {
                            world.Stamp = stamp;
                            world.IdCounter = ids;
                            string packing = "";
                            if (world.Packing == 1)
                            {
                                packing = " (Deflate)";
                            }

                            string hash = WorldFiles.Hex(world.Hash)
                                .Substring(0, 12);
                            string files = string.Join(
                                ", ",
                                world.Files.Select(
                                    f => $"{f.Name} {f.Size / 1024} KiB"));
                            Plugin.Log.LogInfo(
                                "Host world "
                                    + $"{hash}, day {day}: {files}; " +
                                               "sent as "
                                    + $"{world.Sent.Length / 1024} "
                                    + $"KiB{packing}; {world.Timing}");
                        }
                    }
                }

                if (world != null)
                {
                    world.Scene = scene;
                    world.MadeAt = Time.realtimeSinceStartup;
                    _hostWorld = world;
                }
            }
            finally
            {
                _preparing = false;
                var waiting = _waiting.ToList();
                _waiting.Clear();
                foreach (var peer in waiting)
                {
                    if (world == null)
                    {
                        _session.Refuse(peer, NotInWorld);
                    }
                    else
                    {
                        Offer(peer, world);
                    }
                }
            }
        }

        /// <summary>
        /// Worker thread: read a profile folder's save files, hash them and
        /// pack them for sending.
        /// </summary>
        private static Snapshot Pack(string profDir, string json)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var files = WorldFiles.Read(profDir);
            if (!files.Any(f => f.Name == "sav.dat"))
            {
                return null;
            }

            var hash = WorldFiles.Hash(json, files);
            var raw = new byte[files.Sum(f => (long)f.Data.Length)];
            int at = 0;
            foreach (var f in files)
            {
                Buffer.BlockCopy(f.Data, 0, raw, at, f.Data.Length);
                at += f.Data.Length;
            }

            long readMs = sw.ElapsedMilliseconds;
            byte[] sent = raw;
            byte packing = 0;
            string packError = null;
            try
            {
                var packed = WorldFiles.Deflate(raw);
                if (SnapshotRules.SendsPacked(packed.Length, raw.Length))
                {
                    sent = packed;
                    packing = 1;
                }
            }
            catch (Exception e)
            {
                packError = e.Message;
            }

            var snapshot = new Snapshot
            {
                Hash = hash,
                ProfileJson = json,
                Files = files.Select(f => (f.Name, f.Data.Length)).ToList(),
                Sent = sent,
                Packing = packing,
                RawSize = raw.Length,
            };

            string timing = $"read and hashed in {readMs} ms, ";
            if (packError == null)
            {
                timing += $"packed in {sw.ElapsedMilliseconds - readMs} ms";
            }
            else
            {
                timing += $"couldn't pack it ({packError})";
            }

            snapshot.Timing = timing + " on a worker thread";
            return snapshot;
        }

        private void Offer(ulong peer, Snapshot world)
        {
            _offered[peer] = (world, world.Scene);
            _staleTold.Remove(peer);
            var files = new List<OfferedFile>();
            foreach (var f in world.Files)
            {
                files.Add(
                    new OfferedFile { Name = f.Name, Size = (uint)f.Size });
            }

            var w = _session.Begin(MsgType.SaveOffer);
            SaveOffer.Write(
                w,
                new SaveOfferBody
                {
                    Hash = world.Hash,
                    Size = (uint)world.Sent.Length,
                    ProfileJson = world.ProfileJson,
                    Files = files,
                    Packing = world.Packing,
                    Raw = (uint)world.RawSize,
                });

            _session.SendTo(peer, w, Channel.Control, reliable: true);
            Plugin.Log.LogInfo(
                $"Offered the world to #{peer} ({world.Sent.Length / 1024} KiB "
                    + $"to send, {world.RawSize / 1024} KiB unpacked)");
            WatchFrames(peer);
        }

        // ---- the host's frame times while someone joins ---------------------

        private float _watchUntil = -1f, _watchFrom, _longestFrame;
        private int _slowFrames, _frames;
        private ulong _watchPeer;

        /// <summary>
        /// Host: note the longest frame from now until the joiner is in (or a
        /// couple of minutes), for the log.
        /// </summary>
        private void WatchFrames(ulong peer)
        {
            if (_watchUntil < 0f)
            {
                _watchFrom = Time.realtimeSinceStartup;
                _longestFrame = 0f;
                _slowFrames = 0;
                _frames = 0;
            }

            _watchPeer = peer;
            _watchUntil = Time.realtimeSinceStartup + 120f;
        }

        private void EndWatch(string why)
        {
            if (_watchUntil < 0f)
            {
                return;
            }

            Plugin.Log.LogInfo(
                $"[join] host frames while #{_watchPeer} joined ({why}, "
                    + $"{Time.realtimeSinceStartup - _watchFrom:0.0} s): "
                    + $"longest {_longestFrame * 1000f:0} ms, {_slowFrames} of "
                    + $"{_frames} over 50 ms");
            LastJoinLongestFrameMs = _longestFrame * 1000f;
            _watchUntil = -1f;
        }

        private void UpdateWatch()
        {
            if (_watchUntil < 0f)
            {
                return;
            }

            float dt = Time.unscaledDeltaTime;
            _frames++;
            if (dt > _longestFrame)
            {
                _longestFrame = dt;
            }

            if (dt > 0.05f)
            {
                _slowFrames++;
            }

            if (Time.realtimeSinceStartup > _watchUntil)
            {
                EndWatch("not in after 2 minutes");
            }
        }

        /// <summary>
        /// Why a joiner can't come in yet, or null. A dream is a place of its
        /// own the game loads for the player who's in it; someone joining then
        /// would be put where the host stands, in a place their game hasn't
        /// got, and be sent to load again when the host comes out. The
        /// prologue's stages are the exception (World.Story.Prologue).
        /// </summary>
        private string HostNotReady()
        {
            return JoinWaits.Text(JoinGateRules.NotReady(ReadyNow()));
        }

        /// The facts of the join gate. The test hold starts at the first ask
        /// in a world, and the prologue says why it waits (and logs it) only
        /// when that is what decides, as before.
        private ReadyFacts ReadyNow()
        {
            bool inWorld = InWorld;
            bool holding = false;
            if (inWorld)
            {
                var hold = JoinGateRules.Hold(
                    _testHold, new Seconds(Time.realtimeSinceStartup));
                _testHold = hold.Hold;
                holding = hold.Holding;
            }

            // The prologue on a new game: the intro film
            // (WorldGenerator.playingIntro), then the forest
            // (dream_tutorial_00, a separate location), then a dream
            // (dream_tutorial_01). The two stages are played together: a joiner
            // goes into the stage the host is in as it loads the snapshot
            // (World.Story.Prologue). Only the film, and the moments between
            // the stages or in their cutscenes, keep it waiting.
            bool film = World.Story.Prologue.Film;
            bool prologue = World.Story.Prologue.Active;
            bool waits = false;
            if (inWorld && !holding && !film && prologue)
            {
                string why = World.Story.Prologue.WhyWait(WorldReady);
                CoopSession coop = Coop.Current;
                if (coop != null)
                {
                    coop.Prologue.NoteWait(why);
                }

                waits = why != null;
            }

            var outside = Singleton<OutsideLocations>.Instance;
            var dreams = Singleton<Dreams>.Instance;
            // A separate location is loaded only in the host's game, and the
            // joiner would be put beside the host there (Characters), on the
            // world's grid of a game that hasn't loaded it. The snapshot would
            // also be saved while away, which the game itself never does.
            return new ReadyFacts
            {
                InWorld = inWorld,
                WorldReady = WorldReady,
                Holding = holding,
                Film = film,
                Prologue = prologue,
                PrologueWaits = waits,
                Dreaming = dreams != null && dreams.dreaming,
                Away = outside != null && outside.playerInOutsideLocation,
            };
        }

        private static readonly string NotInWorld =
            Refusals.Text(Verdict.Refuse(Refusal.NoSavedGame));

        /// <summary>Test (map test's "hold-joiners"): for this many seconds
        /// from the first joiner, answer as if the host were watching the
        /// prologue's intro film.</summary>
        internal float TestHoldSecs
        {
            get => _testHold.Secs;
            set => _testHold = _testHold with { Secs = value };
        }

        private TestHold _testHold = TestHold.Off;

        private bool HostBeforeWelcome(ulong peer, byte[] clientHash)
        {
            // The world we offered this joiner, now downloaded: let them in
            // with it, even if a newer snapshot would be taken now. Over
            // Steam's relay a download can outlast SnapshotReuse; asking for a
            // fresh snapshot then gave a new hash, a new offer and another
            // download, for ever.
            bool offered = _offered.TryGetValue(peer, out var offer)
                && offer.Scene == ActiveScene
                && WorldFiles.Equal(offer.World.Hash, clientHash);
            Snapshot world = null;
            if (!offered)
            {
                world = FreshHostWorld();
            }

            bool ending = false;
            CoopSession current = Coop.Current;
            if (current != null)
            {
                ending = current.Epilogue.JoinRefusal() != null;
            }

            var admission = JoinGateRules.Admit(
                new AdmitFacts
                {
                    Ending = ending,
                    Wait = JoinGateRules.NotReady(ReadyNow()),
                    Offered = offered,
                    Fresh = world != null,
                    FreshMatches = world != null
                        && WorldFiles.Equal(world.Hash, clientHash),
                    InWorld = InWorld,
                });

            switch (admission.Step)
            {
                case AdmitStep.Refuse:
                    _session.Refuse(
                        peer,
                        Refusals.Text(Verdict.Refuse(admission.Refusal)));
                    return false;
                case AdmitStep.Wait:
                    JoinWait(peer, JoinWaits.Text(admission.Wait));
                    return false;
                case AdmitStep.LetInOffered:
                    LetIn(peer, offer.World.IdCounter);
                    return true;
                case AdmitStep.Prepare:
                    // Make one (PrepareWorld), and offer it when it's ready.
                    // Until then the joiner is told to wait, and its Hellos
                    // meanwhile get the same answer.
                    _waiting.Add(peer);
                    if (!_preparing)
                    {
                        _runner.StartCoroutine(PrepareWorld());
                    }

                    WatchFrames(peer);
                    JoinWait(peer, JoinWaits.Text(admission.Wait));
                    return false;
                case AdmitStep.LetIn:
                    LetIn(peer, world.IdCounter);
                    return true;
                default:
                    Offer(peer, world);
                    return false;
            }
        }

        private void LetIn(ulong peer, int idCounter)
        {
            _offered.Remove(peer);
            _waiting.Remove(peer);
            SharedIds.Joined(peer, idCounter);
            if (_watchPeer == peer)
            {
                EndWatch("let in");
            }
        }

        private void JoinWait(ulong peer, string why)
        {
            var ww = _session.Begin(MsgType.JoinWait);
            JoinWaitMessage.Write(ww, why);
            _session.SendTo(peer, ww, Channel.Control, reliable: true);
        }

        private void OnNeedSave(ulong peer, PacketReader r)
        {
            if (!_session.IsHost)
            {
                return;
            }

            var asked = NeedSave.Read(r);
            var hash = asked.Hash;
            uint offset = asked.Offset;
            if (!_offered.TryGetValue(peer, out var offer)
                || !WorldFiles.Equal(offer.World.Hash, hash))
            {
                // Not (or no longer) offered to them: the offer went with a
                // dropped connection, or the world was loaded again. Without an
                // answer the joiner would wait for this piece for ever; this
                // has it ask again.
                bool told = _staleTold.TryGetValue(peer, out var at);
                if (JoinGateRules.TellsStale(
                    told,
                    new Seconds(at),
                    new Seconds(Time.realtimeSinceStartup)))
                {
                    _staleTold[peer] = Time.realtimeSinceStartup;
                    Plugin.Log.LogInfo(
                        $"#{peer} asked for a world it isn't offered: telling "
                            + "it to ask again");
                    JoinWait(
                        peer,
                        JoinWaits.Text(World.Joining.JoinWait.WorldChanged));
                }

                return;
            }

            var world = offer.World;
            int n = OfferRules.Serves(offset, asked.Length, world.Sent.Length);
            if (n < 0)
            {
                return;
            }

            var w = _session.Begin(MsgType.SaveData);
            SaveData.Write(
                w,
                new WorldPiece
                {
                    Hash = world.Hash,
                    Offset = offset,
                    Length = (uint)n,
                },
                world.Sent);

            _session.SendTo(peer, w, Channel.Control, reliable: true);
        }
    }
}
