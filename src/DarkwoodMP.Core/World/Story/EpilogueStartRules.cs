using System;
using System.Collections.Generic;

namespace DarkwoodMP.World.Story
{
    /// A new game started at the epilogue, for trying the ending together:
    /// it starts as chapter 1 with the prologue skipped, and the host starts
    /// the epilogue with a button once the others have joined (nobody can
    /// join once it plays).
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

        /// Whether the Multiplayer window shows the button: in a game started
        /// at the epilogue, not a joiner's, until the epilogue plays.
        public static bool Shows(in EpilogueStartFacts game) =>
            game.AtEpilogue && !game.Joiner && !game.InEpilogue;

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
                return Verdict.Refuse(Refusal.PlayingEnding);
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
