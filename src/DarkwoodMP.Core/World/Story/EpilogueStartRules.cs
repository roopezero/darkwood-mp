using System;
using System.Collections.Generic;

namespace DarkwoodMP.World.Story
{
    /// A new game started at the epilogue, for trying the ending together:
    /// it starts as chapter 1 with the prologue skipped, and goes into the
    /// epilogue by itself a moment after its world has loaded. The others
    /// join it there (EpilogueSync's late join).
    public static class EpilogueStartRules
    {
        /// The story flag of the Talking Tree burned (Flags.cs).
        public const string TreeBurnedFlag = "talkingTree_burnt";

        /// Whether a new game started there skips the game's prologue.
        public static bool SkipsPrologue(StoryStart start) =>
            start != StoryStart.Prologue;

        /// Where a save was started, from its profile's skippedPrologue and
        /// its SaveBook record.
        public static StoryStart StartOf(bool skipped, bool atEpilogue)
        {
            if (atEpilogue)
            {
                return StoryStart.Epilogue;
            }

            if (skipped)
            {
                return StoryStart.ChapterOne;
            }

            return StoryStart.Prologue;
        }

        /// The story flags set as the epilogue starts.
        public static IReadOnlyList<string> Flags(TreeFate tree)
        {
            if (tree == TreeFate.Burned)
            {
                return new[] { TreeBurnedFlag };
            }

            return Array.Empty<string>();
        }

        /// The Tree a test names ("--dwmp-start-epilogue burned"): Burned
        /// for "burned", Standing for anything else.
        public static TreeFate TreeNamed(string name)
        {
            if (name == "burned")
            {
                return TreeFate.Burned;
            }

            return TreeFate.Standing;
        }

        /// Seconds (realtime) the world has been ready before the epilogue
        /// starts by itself: hosting begins as the world is ready
        /// (HostStart), so the epilogue starts with the session up.
        public const float StartAfter = 3f;

        /// Whether the epilogue starts by itself now: it may, it wasn't
        /// started in this world yet, and the world has been ready for
        /// StartAfter.
        public static bool StartsNow(in EpilogueStartFacts game)
        {
            if (game.Started || game.ReadyFor.Value < StartAfter)
            {
                return false;
            }

            return !MayStart(game).Refused;
        }

        /// Whether the epilogue may start now.
        public static Verdict MayStart(in EpilogueStartFacts game)
        {
            if (!game.AtEpilogue)
            {
                return Verdict.Refuse(Refusal.NotAnEpilogueStart);
            }

            if (game.Joiner)
            {
                return Verdict.Refuse(Refusal.HostStartsEpilogue);
            }

            if (game.InEpilogue)
            {
                return Verdict.Refuse(Refusal.EpilogueStarted);
            }

            if (!game.InWorld)
            {
                return Verdict.Refuse(Refusal.WorldNotLoaded);
            }

            if (game.Dreaming)
            {
                return Verdict.Refuse(Refusal.WaitForDream);
            }

            return Verdict.Allow;
        }
    }
}
