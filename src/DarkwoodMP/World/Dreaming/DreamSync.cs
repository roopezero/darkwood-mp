using DarkwoodMP.Combat;
using DarkwoodMP.Net;
using DarkwoodMP.World.Hideout;
using UnityEngine;

namespace DarkwoodMP.World.Dreaming
{
    /// <summary>
    /// The level-up dreams are dreamt together. In Darkwood, taking certain
    /// levels at the stove (2, 3, 5, 6 and 7, once each: Dreams.hadDreamAtLvlN)
    /// starts a dream (SkillsMenu.confirmSkills: Dreams.startTransition; with
    /// the mod, Progression.TakeLevelAtStove does the same when the stove gives
    /// the level), the level 2 one a set dream and the others one drawn from
    /// the dreams left (Dreams.getPreset).
    ///
    /// In co-op only the host's levels bring these dreams (a client's level is
    /// taken with Dreams.disabled, so it brings no dream; progression is
    /// shared, see World.Progression). When the host's game draws the dream,
    /// the host sends its name (DreamStart), and every client goes into that
    /// same dream through the game's own transition. The dream's place is the
    /// host's (World.Places.Locations), so the players see each other there.
    ///
    /// The dream ends together: when any player's dream ends (they died in it,
    /// or reached its end: Dreams.initiateEndDreaming), everyone's ends with
    /// that outcome (DreamEnd; a client's goes through the host). Outcomes that
    /// lead into the next dream (transferToDream) are the same in every game,
    /// so everyone goes on into the same next dream.
    ///
    /// The shared dream is run by the host, as a separate location the host
    /// placed is (Whereabouts.SharedDream): its enemies (the church dream's
    /// chomper) and its events are the host's, a client's own copies of them
    /// are frozen and hidden and the host's are shown, and the host's enemies
    /// there go for whoever has the most threat. Dreams that come from the
    /// story or from a kill (the night trader's) stay the player's own,
    /// except the few every player dreams together (the village cellar's:
    /// StoryDreamRules, DreamSync.Story).
    ///
    /// What happens is the core's DreamRules; DreamPatches hand the game's
    /// calls here.
    /// </summary>
    internal sealed partial class DreamSync
    {
        private readonly CoopSession _coop;

        /// Host: our confirmSkills just started a level dream.
        private bool _hostLevelDream;

        /// The shared dream's end was sent or replayed.
        private bool _ended;
        private string _pendingStart;
        private string _pendingEnd;
        private Seconds _startBy;
        private Seconds _startAt;

        /// Client: when the held-player check begins (0: no check). A dream
        /// ended by another game's say can end in the middle of one of our
        /// own cutscenes or fades, and their end (which gives the player
        /// back) may never come then. A few seconds after the end, once
        /// nothing of the game's own holds the player (no cutscene,
        /// transition, load or dialogue), let them go.
        private Seconds _inputsFrom;

        public DreamSync(CoopSession coop)
        {
            _coop = coop;
            coop.Handle(MsgType.DreamStart, OnStart);
            coop.Handle(MsgType.DreamEnd, OnEnd);
        }

        private Session S => _coop.Net;

        private static Seconds Now => new Seconds(Time.unscaledTime);

        /// <summary>
        /// The shared dream's preset name while it runs (null: none).
        /// </summary>
        public string Shared { get; private set; }

        private static DreamFacts DreamOf(Dreams d)
        {
            if (d == null)
            {
                return new DreamFacts { LoadingGame = Core.loadingGame };
            }

            return new DreamFacts
            {
                HasDreams = true,
                Dreaming = d.dreaming,
                Prepared = d.dreamPrepared,
                Switching = d.switchingDream,
                WantToDream = d.wantToDream,
                EnteringDream = Core.EnteringDream,
                LoadingGame = Core.loadingGame,
            };
        }

        private DreamShareFacts ShareOf()
        {
            return new DreamShareFacts
            {
                HasSession = true,
                IsHost = S.IsHost,
                ClientMode = NetEnemies.ClientMode,
                Together = Hideouts.Together,
                Shared = Shared != null,
                Ended = _ended,
                Replaying = _coop.Guards.Replaying,
                Marked = _hostLevelDream,
                PendingStart = _pendingStart != null,
                EpiloguePlaying = _coop.Epilogue.Playing,
            };
        }

        // ---- host -----------------------------------------------------------

        /// <summary>
        /// SkillsMenu.confirmSkills is about to run: a client's level brings
        /// no dream of its own (Dreams.disabled for the call), the host's
        /// levels bring the shared ones. Returns what disabled was, for
        /// Confirmed (false when it wasn't changed).
        /// </summary>
        public bool Confirming(Dreams d)
        {
            bool off = DreamRules.ClientDreamOff(
                d != null, NetEnemies.ClientMode, Hideouts.Together);
            if (!off)
            {
                return false;
            }

            bool was = d.disabled;
            d.disabled = true;
            return was;
        }

        /// <summary>
        /// SkillsMenu.confirmSkills ran: a client's Dreams.disabled goes back
        /// as it was, and the host draws its level dream at once.
        /// </summary>
        public void Confirmed(Dreams d, bool was)
        {
            bool off = DreamRules.ClientDreamOff(
                d != null, NetEnemies.ClientMode, Hideouts.Together);
            if (off)
            {
                d.disabled = was;
            }

            AfterConfirm(d);
        }

        public void AfterConfirm(Dreams d)
        {
            if (DreamRules.DrawsEarly(DreamOf(d), ShareOf()))
            {
                _hostLevelDream = true;
                DrawNow(d);
            }
        }

        /// <summary>
        /// The game draws the dream only after its transition has played
        /// (DreamTransition.onFinishedVideo -> Dreams.prepareDream ->
        /// getPreset), several seconds after the level. Drawing it here, as
        /// the transition starts, sends it (PresetChosen) then, so every
        /// client's transition plays at the same time as ours; the transition
        /// then goes into the dream drawn (getPreset by name doesn't draw
        /// again).
        /// </summary>
        private static void DrawNow(Dreams d)
        {
            try
            {
                string presetName = d.startTransition.dreamToTransitionTo;
                if (presetName == null)
                {
                    presetName = "";
                }

                var preset = d.getPreset(presetName);
                if (preset != null)
                {
                    d.startTransition.dreamToTransitionTo = preset.name;
                }
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning(
                    "[dream] couldn't draw the level dream early, it's sent "
                        + $"when the game draws it: {e.Message}");
            }
        }

        public void PresetChosen(DreamPreset preset)
        {
            var share = ShareOf() with { HasPreset = preset != null };
            PresetStep step = DreamRules.PresetChosen(DreamOf(null), share);
            if (step == PresetStep.Keep)
            {
                if (!_hostLevelDream)
                {
                    StoryDrawn(preset);
                }

                return;
            }

            _hostLevelDream = false;
            if (step == PresetStep.Clear)
            {
                return;
            }

            Shared = preset.name;
            _ended = false;
            var w = S.Begin(MsgType.DreamStart);
            DreamStart.Write(w, preset.name);
            S.Broadcast(w, Channel.Control, reliable: true);
            Plugin.Log.LogInfo(
                $"[dream] level dream {preset.name}: everyone goes in");
        }

        // ---- both -----------------------------------------------------------

        /// <summary>
        /// Dreams.initiateEndDreaming is about to run in our game.
        /// </summary>
        public void Ending(Dreams d)
        {
            EndSend send = DreamRules.Ending(DreamOf(d), ShareOf());
            if (send == EndSend.None)
            {
                return;
            }

            _ended = true;
            var w = EndMessage(d.outcome);
            if (send == EndSend.Broadcast)
            {
                S.Broadcast(w, Channel.Control, reliable: true);
            }
            else if (send == EndSend.ToHost)
            {
                S.SendToHost(w, Channel.Control, reliable: true);
            }

            Plugin.Log.LogInfo(
                $"[dream] our dream {Shared} ends ({d.outcome}): everyone's "
                    + "does");
        }

        private PacketWriter EndMessage(string outcome)
        {
            var w = S.Begin(MsgType.DreamEnd);
            var end = new DreamEnded { Outcome = outcome, Dream = Shared };
            DreamEnd.Write(w, end);
            return w;
        }

        public void Started()
        {
            _ended = false;
            EnteredAt =
                System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
        }

        /// <summary>
        /// For tests: when our game last started dreaming (Unix seconds; the
        /// test games share one clock).
        /// </summary>
        internal double EnteredAt { get; private set; }

        /// <summary>
        /// The prologue's stages are dreamt together too (World.Prologue):
        /// this is the one every game is in.
        /// </summary>
        public void SharePrologue(string stage)
        {
            Share(stage);
            Plugin.Log.LogInfo(
                $"[dream] the prologue's {stage}: dreamt together");
        }

        /// <summary>
        /// A dream from the story everyone dreams together (the epilogue,
        /// EpilogueSync).
        /// </summary>
        public void Share(string name)
        {
            Shared = name;
            _ended = false;
        }

        public void AfterEnd(Dreams d)
        {
            if (DreamRules.Over(DreamOf(d), ShareOf()))
            {
                Plugin.Log.LogInfo($"[dream] back from {Shared}");
                Shared = null;
            }
        }

        private void EndHere(Dreams d, string outcome)
        {
            _ended = true;
            if (NetEnemies.ClientMode)
            {
                _inputsFrom = DreamRules.InputsFrom(Now);
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

        private void OnEnd(ulong from, PacketReader r)
        {
            DreamEnded end = DreamEnd.Read(r);
            string outcome = end.Outcome;
            var d = Singleton<Dreams>.Instance;
            DreamFacts dream = DreamOf(d);
            DreamShareFacts share = ShareOf() with
            {
                FromHost = from == S.HostId,
                SameDream = end.Dream == Shared,
            };

            if (S.IsHost)
            {
                OnClientEnd(from, d, end, dream, share);
                return;
            }

            ClientEndStep step = DreamRules.ClientEnd(dream, share);
            if (step == ClientEndStep.KeepPending)
            {
                _pendingEnd = outcome;
                return;
            }

            if (step != ClientEndStep.End)
            {
                return;
            }

            Plugin.Log.LogInfo(
                $"[dream] the host's dream ended ({outcome}): ours ends");
            EndHere(d, outcome);
        }

        private void OnClientEnd(
            ulong from, Dreams d, DreamEnded end, DreamFacts dream,
            DreamShareFacts share)
        {
            // The epilogue is asked only once the other checks pass.
            bool asks = share.Shared && d != null && d.dreaming && !_ended;
            share = share with
            {
                OutcomeAllowed =
                    asks && _coop.Epilogue.OutcomeAllowed(d, end.Outcome),
            };

            HostEndStep step = DreamRules.HostEnd(dream, share);
            if (step == HostEndStep.Drop)
            {
                return;
            }

            // The end of a dream we're no longer in (a prologue stage that
            // ended here a moment before).
            if (step == HostEndStep.NotPassedOn)
            {
                Plugin.Log.LogInfo(
                    $"[dream] {S.NameOf(from)}'s {end.Dream} ended, but we're "
                        + $"in {Shared}: not passed on");
                return;
            }

            Plugin.Log.LogInfo(
                $"[dream] {S.NameOf(from)}'s dream ended ({end.Outcome}): "
                    + "ours and everyone's end");
            S.Broadcast(
                EndMessage(end.Outcome), Channel.Control, reliable: true,
                except: from);
            EndHere(d, end.Outcome);
        }
    }
}
