using System.Collections.Generic;
using DarkwoodMP.Combat;
using DarkwoodMP.Net;
using HarmonyLib;
using UnityEngine;
using DreamSync = DarkwoodMP.World.Dreaming.DreamSync;

namespace DarkwoodMP.World.Story
{
    /// <summary>
    /// The prologue is played together. In Darkwood it isn't a chapter of its
    /// own: a new game (Player.firstPlay, the prologue not skipped) makes the
    /// chapter 1 world, plays the intro film (WorldGenerator.playingIntro), and
    /// puts the player into two dreams one after the other: the forest
    /// (dream_tutorial_00, a separate location made with the world) and then
    /// dream_tutorial_01 (the first one's outcome, transferToDream). Its end
    /// (Dreams.endDreaming of dream_tutorial_01) wakes the player at the
    /// hideout (WorldGenerator.playerBase) at 5 o'clock, with Player.firstPlay
    /// false from then on.
    ///
    /// Joining: only the film keeps a joiner waiting
    /// (World.WorldShare.HostNotReady), and the moments between the stages.
    /// Otherwise the host's snapshot is written as a save made while going into
    /// that stage (Dreams.SaveState.wantToDream and dreamName; the game itself
    /// saves while dreaming only then), so the joiner's game goes into the same
    /// stage as it loads, by the game's own path for a save made in a dream
    /// (Dreams.loadingSaveGameInDream): the stage's place, its starting items,
    /// the player at its start. As in the game's own save going into a dream,
    /// the snapshot has the player still in the world and the stage's place not
    /// yet made (OutsideLocations.SaveState), so the joiner's game makes its
    /// own copy of it, in the slot the host's stands in. Once in, the joiner's
    /// player is a first play too, as the host's is.
    ///
    /// The stages are shared dreams (World.DreamSync.Shared is the stage's name
    /// in every game), so the players see each other, and when any player's
    /// stage ends, everyone's ends with the same outcome: the forest leads
    /// everyone into dream_tutorial_01, and its end wakes everyone at the
    /// hideout. A client's end goes to the host, which checks it's the stage
    /// the host is in. As in the level dreams, each game runs the stage's
    /// characters and events by itself.
    ///
    /// Characters (World.Characters): a joiner's character isn't made empty or
    /// a copy of the host's while in the prologue (the stage gives its own
    /// items), only put beside the host, and none is sent to the host until the
    /// prologue is over, so a character saved with the world is never one from
    /// the prologue. At the end, each joiner is moved to a free spot beside the
    /// hideout's start, off the host.
    /// </summary>
    internal sealed class Prologue
    {
        private static Session S => Plugin.Instance.Host.Session;
        private static Dreams D => Singleton<Dreams>.Instance;

        public static bool IsStage(string name) =>
            PrologueRules.IsStage(name);

        /// <summary>
        /// The only code reading the game's Dreams for the rules.
        /// </summary>
        internal static DreamFacts DreamsOf(Dreams d)
        {
            if (d == null)
            {
                return new DreamFacts { Entering = Core.EnteringDream };
            }

            string preset = null;
            string trueName = null;
            if (d.preset != null)
            {
                preset = d.preset.name;
                trueName = Core.getTrueLocationName(preset);
            }

            return new DreamFacts
            {
                Exists = true,
                Entering = Core.EnteringDream,
                Dreaming = d.dreaming,
                Prepared = d.dreamPrepared,
                Switching = d.switchingDream,
                Wanted = d.wantToDream,
                Transition = d.currentTransition != null
                    && d.currentTransition.isPlaying,
                HasPreset = d.preset != null,
                Preset = preset,
                TrueName = trueName,
            };
        }

        /// <summary>
        /// The prologue stage our game is in, or going into or out of (null:
        /// not in the prologue).
        /// </summary>
        public static string Stage => PrologueRules.StageOf(DreamsOf(D));

        public static bool Active => Stage != null;

        /// <summary>
        /// In a stage, settled: not between stages or going in
        /// (Dreams.dreamPrepared stays set until the dream ends).
        /// </summary>
        public static bool InStage => PrologueRules.InStage(DreamsOf(D));

        /// <summary>
        /// Host: why a joiner waits in the prologue, for the log (null: it
        /// doesn't).
        /// </summary>
        internal static string WhyWait(bool worldReady)
        {
            if (PrologueRules.MayJoin(InStage, worldReady))
            {
                return null;
            }

            var d = D;
            return $"stage {Stage}, dreaming {d.dreaming}, switching "
                + $"{d.switchingDream}, entering {Core.EnteringDream}, world "
                + $"ready {worldReady}, inputs forbidden {Core.forbidInputs}, "
                + $"loading {Core.loadingGame}";
        }

        private string _lastWhy;

        /// <summary>Host: log why joiners wait, when it changes.</summary>
        internal void NoteWait(string why)
        {
            if (why == _lastWhy)
            {
                return;
            }

            _lastWhy = why;
            if (why != null)
            {
                Plugin.Log.LogInfo($"[prologue] a joiner waits: {why}");
            }
        }

        /// <summary>
        /// The intro film before the forest (WorldGenerator.playingIntro).
        /// </summary>
        public static bool Film => Singleton<WorldGenerator>.Instance != null
            && Singleton<WorldGenerator>.Instance.playingIntro;

        /// <summary>
        /// Host, while the snapshot for a joiner is saved: the world as if
        /// saved going into the stage we're in, so the joiner's game goes into
        /// it as it loads (SaveManager.Load: Dreams.wantToDream).
        /// </summary>
        internal void ForSnapshot(Dreams.SaveState state, Dreams d)
        {
            if (!PrologueRules.Snapshots(WorldShare.Snapshotting, DreamsOf(d)))
            {
                return;
            }

            state.wantToDream = true;
            state.dreamName = d.preset.name;
            Plugin.Log.LogInfo(
                $"[prologue] snapshot saved in {d.preset.name}: the joiner "
                    + "goes into it as it loads");
        }

        /// <summary>
        /// Host, while the snapshot is saved in a stage: the player not yet in
        /// the stage's place, as in the game's own save going into a dream
        /// (Dreams.prepareDream saves before OutsideLocations.prepareLocation).
        /// Loaded as in the place, the load entered it before the world was
        /// ready and the dream's start then failed (NullReferenceExceptions in
        /// SaveManager.Load and Dreams.startDreaming), so the joiner's world
        /// never finished loading. The stages' places are left out of the
        /// spawned ones too: a save doesn't make a spawned place again (its
        /// Location came back null, and Dreams.dreamLocation with it), so the
        /// joiner's game makes the stage's place afresh as it goes into the
        /// dream (OutsideLocations.spawnLocation), as for a game saved going
        /// into a level dream. What happened in the host's copy of the stage
        /// stays the host's.
        /// </summary>
        internal void ForSnapshot(OutsideLocations.SaveState state)
        {
            var d = D;
            if (!PrologueRules.Snapshots(WorldShare.Snapshotting, DreamsOf(d)))
            {
                return;
            }

            state.playerInOutsideLocation = false;
            state.currentLocationName = "";
            for (int i = state.spawnedLocationsKeys.Count - 1; i >= 0; i--)
            {
                if (!IsStage(
                    Core.getTrueLocationName(state.spawnedLocationsKeys[i])))
                {
                    continue;
                }

                state.spawnedLocationsKeys.RemoveAt(i);
                if (i < state.spawnedLocationsValuesIDs.Count)
                {
                    state.spawnedLocationsValuesIDs.RemoveAt(i);
                }
            }

            // Made afresh, the place takes the next free slot of the far-off
            // grid (OutsideLocations.locationPositions); give it the slot it
            // has here, so the players stand in the same place in both games.
            var o = Singleton<OutsideLocations>.Instance;
            List<Vector3> slots = null;
            if (o != null)
            {
                slots = Traverse.Create(o).Field("locationPositions")
                    .GetValue<List<Vector3>>();
            }

            if (slots == null || d.dreamLocation == null)
            {
                return;
            }

            var at = d.dreamLocation.transform.position;
            var spots = new List<Vec3>();
            foreach (Vector3 slot in slots)
            {
                spots.Add(new Vec3(slot.x, slot.y, slot.z));
            }

            SlotAnswer answer = PrologueRules.SlotOf(
                spots,
                new Vec3(at.x, at.y, at.z),
                state.actualSpawnedLocationsCount);
            if (answer.Kind == SlotKind.Last)
            {
                state.actualSpawnedLocationsCount = answer.Index;
            }
            else if (answer.Kind == SlotKind.Other)
            {
                Plugin.Log.LogWarning(
                    $"[prologue] {d.preset.name} is in slot {answer.Index} of "
                        + $"{state.actualSpawnedLocationsCount}: a joiner "
                        + "makes it in another");
            }
        }

        private bool _firstPlaySet;
        private bool _loaded;
        private Seconds _loadedAt;
        private float _nextWhy;

        /// <summary>Call every frame.</summary>
        public void Update()
        {
            if (S == null || !S.Active || !WorldShare.InWorld)
            {
                // the rule keeps the load's time out of a session
                _firstPlaySet = false;
                return;
            }

            var d = D;
            var p = Player.Instance;
            string shared = null;
            if (Coop.Current != null)
            {
                shared = Coop.Current.Dreaming.Shared;
            }

            var facts = new PrologueFacts
            {
                InSession = true,
                Dreams = DreamsOf(d),
                Shared = shared,
                Joiner = NetEnemies.ClientMode,
                HasPlayer = p != null,
                Loading = Core.loadingGame,
                Generated = Core.worldGenFinished(),
                Forbidden = Core.forbidInputs,
                HeldByNothing = DreamSync.InputsHeldByNothing(d),
                WorldReady = WorldShare.WorldReady,
                Now = new Seconds(Time.unscaledTime),
                FirstPlaySet = _firstPlaySet,
                Loaded = _loaded,
                LoadedAt = _loadedAt,
            };

            PrologueStep step = PrologueRules.Update(facts);
            _loaded = step.Loaded;
            _loadedAt = step.LoadedAt;
            string stage = null;
            if (InStage)
            {
                stage = Stage;
            }

            // Every game in a stage dreams it together with the others.
            CoopSession coop = Coop.Current;
            if (step.Share != null && coop != null)
            {
                coop.Dreaming.SharePrologue(step.Share);
            }

            // A joiner's player is a first play too, as the host's is, until
            // the prologue's end clears it (Dreams.endDreaming). Set once the
            // load has finished: it changes how the game ends a load.
            // A joiner that loaded into a stage: the stage's place is entered
            // after the load has let the player go
            // (OutsideLocations.prepareLocation forbids inputs, and the game's
            // activatePlayer has already run), so let it go once, a few seconds
            // after the load, unless a cutscene holds it.
            if (step.LetGo)
            {
                Core.forbidInputs = false;
                Plugin.Log.LogInfo(
                    $"[prologue] loaded into {stage}: the player is let "
                        + "go");
            }

            // Why a joiner in a stage isn't ready yet, every 10 seconds, for
            // the log.
            if (stage != null && NetEnemies.ClientMode
                && !WorldShare.WorldReady && Time.unscaledTime > _nextWhy)
            {
                _nextWhy = Time.unscaledTime + 10f;
                var ctl = Singleton<Controller>.Instance;
                var o = Singleton<OutsideLocations>.Instance;
                string location = null;
                if (o != null)
                {
                    location = o.currentLocationName;
                }

                WorldGenerator wg = Singleton<WorldGenerator>.Instance;
                bool? finished = null;
                if (wg != null)
                {
                    finished = wg.finished;
                }

                bool? firstPlay = null;
                if (p != null)
                {
                    firstPlay = p.firstPlay;
                }

                Plugin.Log.LogInfo(
                    $"[prologue] in {stage}, not ready: generated "
                        + $"{Core.worldGenFinished()}, loading "
                        + $"{Core.loadingGame}, inputs forbidden "
                        + $"{Core.forbidInputs} "
                        + $"(locked {Core.cantChangeForbidInputs}, cutscene "
                        + $"{ctl != null && ctl.playingCutscene}, place "
                        + $"loading {o != null && o.loading}, switching "
                        + $"{d.switchingDream}, "
                        + $"entering {Core.EnteringDream}, dialogue "
                        + $"{p != null && p.inDialogue}), first play set "
                        + $"{_firstPlaySet}; "
                        + $"dream prepared {d.dreamPrepared}, loading in dream "
                        + $"{d.loadingSaveGameInDream}, want {d.wantToDream}, "
                        + $"outside {o != null && o.playerInOutsideLocation} "
                        + $"{location}, "
                        + $"spawning {o != null && o.spawningLocations}, "
                        + $"generator finished {finished}, first play "
                        + $"{firstPlay}");
            }

            if (step.MakeFirstPlay)
            {
                Plugin.Log.LogInfo(
                    $"[prologue] in the host's prologue ({stage}): a first "
                        + $"play (was {p.firstPlay})");
                p.firstPlay = true;
            }

            _firstPlaySet = step.FirstPlaySet;
        }

        /// <summary>
        /// The true name of the dream that is ending, read before
        /// Dreams.endDreaming (null: not dreaming).
        /// </summary>
        internal static string Ending(Dreams d)
        {
            if (d.dreaming && d.preset != null)
            {
                return Core.getTrueLocationName(d.preset.name);
            }

            return null;
        }

        /// <summary>
        /// Dreams.endDreaming has run: the prologue's end puts a joiner beside
        /// the hideout's start, off the host.
        /// </summary>
        internal void AfterEnd(Dreams d, string ended)
        {
            if (!PrologueRules.EndsPrologue(ended, d != null,
                d != null && d.dreaming))
            {
                return;
            }

            Plugin.Log.LogInfo(
                "[prologue] the prologue is over: awake at the hideout");
            if (!NetEnemies.ClientMode)
            {
                return;
            }

            var p = Player.Instance;
            var gen = Singleton<WorldGenerator>.Instance;
            GameObject spawn = null;
            if (gen != null && gen.playerBase != null)
            {
                Location baseLocation = gen.playerBase.GetComponent<Location>();
                if (baseLocation != null)
                {
                    spawn = baseLocation.playerSpawn;
                }
                else
                {
                    spawn = null;
                }
            }

            if (p != null && spawn != null)
            {
                KeptCharacters.Characters.PlaceFree(
                    p, spawn.transform.position);
            }
        }
    }
}
