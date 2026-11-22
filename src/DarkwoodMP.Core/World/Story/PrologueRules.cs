using System;
using System.Collections.Generic;

namespace DarkwoodMP.World.Story
{
    /// The prologue's two dream stages played together: which dream is a
    /// stage, when a joiner may come in, what the host's snapshot keeps,
    /// and when a joiner's player is let go (Prologue).
    public static class PrologueRules
    {
        /// The prologue's first stage.
        public const string FirstStage = "dream_tutorial_00";

        /// The prologue's last stage: its end is the prologue's.
        public const string LastStage = "dream_tutorial_01";

        /// A slot of the far-off grid holds the stage's place when it is
        /// within this on x and on z.
        public const float SlotRange = 5000f;

        /// Seconds (unscaled) after the load before a joiner's player is
        /// let go in a stage.
        public const float LetGoAfter = 4f;

        /// A prologue stage's name (Prologue.IsStage).
        public static bool IsStage(string name)
        {
            return name == FirstStage || name == LastStage;
        }

        /// The stage the game is in, or on its way into or out of; null
        /// for none (Prologue.Stage).
        public static string StageOf(in DreamFacts dreams)
        {
            if (!dreams.Exists
                || !dreams.HasPreset
                || !(dreams.Dreaming
                    || dreams.Prepared
                    || dreams.Switching
                    || dreams.Wanted))
            {
                return null;
            }

            if (IsStage(dreams.TrueName))
            {
                return dreams.TrueName;
            }

            return null;
        }

        /// Dreaming a stage, and not switching (Prologue.InStage).
        public static bool InStage(in DreamFacts dreams)
        {
            return StageOf(dreams) != null
                && dreams.Dreaming
                && !dreams.Switching;
        }

        /// Whether a joiner may come in now (Prologue.WhyWait is null): in
        /// a stage, with the world ready.
        public static bool MayJoin(bool inStage, bool ready)
        {
            return inStage && ready;
        }

        /// Whether the host's snapshot is made for a joiner in a stage
        /// (both Prologue.ForSnapshot): it then saves the dream to go into
        /// and leaves the stages' places out.
        public static bool Snapshots(bool snapshotting, in DreamFacts dreams)
        {
            return snapshotting
                && dreams.Exists
                && dreams.Dreaming
                && dreams.HasPreset
                && IsStage(dreams.TrueName);
        }

        /// Where the stage's place stands among the grid's slots
        /// (Prologue.ForSnapshot): the first slot within SlotRange on x and
        /// z. count is actualSpawnedLocationsCount.
        public static SlotAnswer SlotOf(
            IReadOnlyList<Vec3> slots,
            Vec3 at,
            int count)
        {
            for (var i = 0; i < slots.Count; i++)
            {
                if (Math.Abs(slots[i].X - at.X) > SlotRange
                    || Math.Abs(slots[i].Z - at.Z) > SlotRange)
                {
                    continue;
                }

                if (i == count - 1)
                {
                    return new SlotAnswer { Kind = SlotKind.Last, Index = i };
                }

                return new SlotAnswer { Kind = SlotKind.Other, Index = i };
            }

            return default;
        }

        /// One frame of the prologue (Prologue.Update), without its
        /// status log. Out of a session or a world only FirstPlaySet is
        /// cleared; the load time stays.
        public static PrologueStep Update(in PrologueFacts facts)
        {
            if (!facts.InSession)
            {
                return new PrologueStep
                {
                    Loaded = facts.Loaded,
                    LoadedAt = facts.LoadedAt,
                };
            }

            string stage = null;
            if (InStage(facts.Dreams))
            {
                stage = StageOf(facts.Dreams);
            }

            string share = null;
            if (stage != null && facts.Shared != stage)
            {
                share = stage;
            }

            var loaded = facts.Loaded;
            var loadedAt = facts.LoadedAt;
            var letGo = false;
            if (stage != null
                && facts.Joiner
                && !facts.FirstPlaySet
                && facts.HasPlayer
                && !facts.Loading
                && facts.Generated)
            {
                if (!loaded)
                {
                    loaded = true;
                    loadedAt = facts.Now;
                }

                letGo = facts.Forbidden
                    && facts.Now.Value - loadedAt.Value > LetGoAfter
                    && facts.HeldByNothing;
            }
            else if (stage == null)
            {
                loaded = false;
            }

            var firstPlay = stage != null
                && facts.Joiner
                && !facts.FirstPlaySet
                && facts.WorldReady
                && facts.HasPlayer;
            var set = facts.FirstPlaySet || firstPlay;
            if (stage == null && StageOf(facts.Dreams) == null)
            {
                set = false;
            }

            return new PrologueStep
            {
                Share = share,
                LetGo = letGo,
                MakeFirstPlay = firstPlay,
                FirstPlaySet = set,
                Loaded = loaded,
                LoadedAt = loadedAt,
            };
        }

        /// Whether a dream's end is the prologue's (Prologue.AfterEnd): the
        /// last stage ended, and the game no longer dreams.
        public static bool EndsPrologue(
            string ended,
            bool dreams,
            bool dreaming)
        {
            return ended == LastStage && dreams && !dreaming;
        }
    }
}
