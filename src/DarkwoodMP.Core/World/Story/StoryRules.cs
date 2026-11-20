namespace DarkwoodMP.World.Story
{
    /// The story's turning points: which GameEvent steps are story steps,
    /// whether one runs in this game, and what the host does with a
    /// joiner's (Chapters).
    public static class StoryRules
    {
        /// The epilogue's dream (Chapters.EpilogueDream).
        public const string EpilogueDream = "epilog_part1a_dream";

        /// The game's last chapter: the epilogue comes after it.
        public const int LastChapter = 2;

        /// How far from its event a joiner may set off a story step. The
        /// refusal's words have the number written in.
        public const float Reach = 3000f;

        /// A joiner sends at most one StoryRequest in this many seconds
        /// (realtime), of any kind.
        public const float RequestGap = 5f;

        /// What a joiner's last request time starts at: long ago.
        public const float NeverAsked = -999f;

        /// The result of a request that already ran starts with this.
        public const string DonePrefix = "done: ";

        /// The result of a held request starts with this.
        public const string PendingPrefix = "pending: ";

        /// Why a request is done: the host ran it already.
        public const string AlreadyStarted =
            "the host's game has already started it";

        /// Why a request waits: the host is changing chapter or ending.
        public const string OnItsWay =
            "the host's game is on its way to another part of the story";

        /// Why a request waits: the host's world isn't ready.
        public const string NotInWorld = "the host isn't in its world yet";

        /// Why a request waits: the host is in a dream.
        public const string InDream = "the host is in a dream";

        /// The story step a GameEvent step is (Chapters.KindOf). The chapter
        /// is checked first.
        public static StoryKind KindOf(in StoryStepFacts step)
        {
            if (step.Action == StepAction.Transport && step.ActiveModifier)
            {
                return StoryKind.Chapter;
            }

            if (step.Action == StepAction.StartDream
                && step.Value == EpilogueDream)
            {
                return StoryKind.Epilogue;
            }

            if (step.Action == StepAction.EndGame)
            {
                return StoryKind.EndScreen;
            }

            return StoryKind.None;
        }

        /// The key that says a story step ran (Chapters.KeyOf): "chapter N"
        /// for a chapter, "end" for both endings, and any other kind too.
        public static string KeyOf(StoryKind kind, int chapter)
        {
            if (kind == StoryKind.Chapter)
            {
                return "chapter " + chapter;
            }

            return "end";
        }

        /// The step in words, for the log and the joiner's notices
        /// (Chapters.What).
        public static string What(StoryKind kind, int chapter)
        {
            if (kind == StoryKind.Chapter)
            {
                return "chapter " + chapter;
            }

            return "the game's end (" + kind + ")";
        }

        /// Whether a story step runs in this game (Chapters.StoryStepRuns).
        /// With no session it runs. A joiner never runs it, and asks the
        /// host only for its own player's part. The host runs it only when
        /// every part runs, and tells the joiners first about a chapter.
        public static StepRun StepRuns(in StepRunFacts facts)
        {
            if (!facts.InSession)
            {
                return StepRun.Run;
            }

            if (facts.IsClient)
            {
                if (facts.OwnPart)
                {
                    return StepRun.AskHost;
                }

                return StepRun.Skip;
            }

            if (!facts.AllParts)
            {
                return StepRun.Skip;
            }

            if (facts.Kind == StoryKind.Chapter)
            {
                return StepRun.StartChapter;
            }

            return StepRun.Run;
        }

        /// Whether a joiner may send a StoryRequest now, given when it sent
        /// the last (Chapters.SendStoryRequest). When it may, the shell
        /// keeps now as the last; a dropped request is lost.
        public static bool MaySend(Seconds now, Seconds last)
        {
            return !(now.Value - last.Value < RequestGap);
        }

        /// What the joiner is told about the host's answer, or null for
        /// nothing (the request's callback in Chapters.SendStoryRequest).
        public static string NoticeFor(in ReplyFacts reply)
        {
            if (!reply.Ok)
            {
                return "The host didn't start " + reply.What + ": "
                    + reply.Reason;
            }

            if (reply.Result != null
                && reply.Result.StartsWith(PendingPrefix))
            {
                return "The host will start " + reply.What
                    + " as soon as it can ("
                    + reply.Result.Substring(PendingPrefix.Length) + ")";
            }

            return null;
        }

        /// Whether the host's player is in a dream, or on the way into or
        /// out of one (Chapters.HostDreaming).
        public static bool HostDreaming(in DreamFacts dreams)
        {
            if (dreams.Entering)
            {
                return true;
            }

            return dreams.Exists
                && (dreams.Dreaming
                    || dreams.Prepared
                    || dreams.Switching
                    || dreams.Transition);
        }

        /// Host: what to do with a joiner's story step now
        /// (Chapters.Decide). The first check that applies wins: a step that
        /// isn't one, then done, then the waits, then the chapter, then the
        /// event's step, its reach (checked once per held request), and an
        /// event the host doesn't have.
        public static StoryAnswer Decide(in StoryFacts facts)
        {
            var kind = facts.Kind;
            if (kind != StoryKind.Chapter
                && kind != StoryKind.Epilogue
                && kind != StoryKind.EndScreen)
            {
                return Refuse(facts, Verdict.Refuse(Refusal.NoSuchStoryStep));
            }

            if (facts.Ran)
            {
                return Answer(facts, StoryOutcome.Done, AlreadyStarted);
            }

            if (facts.OnTheWay)
            {
                return Answer(facts, StoryOutcome.Wait, OnItsWay);
            }

            if (!facts.HostInWorld)
            {
                return Answer(facts, StoryOutcome.Wait, NotInWorld);
            }

            if (HostDreaming(facts.Dreams))
            {
                return Answer(facts, StoryOutcome.Wait, InDream);
            }

            var now = facts.HostChapter;
            if (kind == StoryKind.Chapter && facts.Chapter != now + 1)
            {
                var detail = Refusals.Detail(
                    facts.Chapter.ToString(),
                    now.ToString());
                return Refuse(
                    facts,
                    Verdict.Refuse(Refusal.NotNextChapter, detail));
            }

            if (kind == StoryKind.Epilogue && now < LastChapter)
            {
                var detail = Refusals.Detail(
                    LastChapter.ToString(),
                    now.ToString());
                return Refuse(
                    facts,
                    Verdict.Refuse(Refusal.EpilogueTooEarly, detail));
            }

            if (facts.EventFound)
            {
                return InEvent(facts);
            }

            if (!facts.WasAway && !facts.IsAway)
            {
                return Refuse(facts, Verdict.Refuse(Refusal.EventNotInWorld));
            }

            return Answer(facts, StoryOutcome.Run, null);
        }

        /// Decide for an event the host has: its step must be the one
        /// asked for, and the joiner near it the first time this is
        /// checked.
        static StoryAnswer InEvent(in StoryFacts facts)
        {
            var step = facts.Step;
            if (KindOf(step) != facts.Kind
                || (facts.Kind == StoryKind.Chapter
                    && step.Chapter != facts.Chapter))
            {
                return Refuse(
                    facts,
                    Verdict.Refuse(Refusal.WrongStep, facts.EventName));
            }

            if (facts.ReachChecked)
            {
                return Answer(facts, StoryOutcome.Run, null);
            }

            var reach = new ReachFacts
            {
                Known = facts.Known,
                StandIn = facts.StandIn,
                Target = facts.EventAt,
                Reach = Reach,
            };

            if (!ReachRules.Near(reach))
            {
                return new StoryAnswer
                {
                    Outcome = StoryOutcome.Refuse,
                    Verdict = Verdict.Refuse(
                        Refusal.EventTooFar,
                        facts.EventName),
                    ReachChecked = true,
                };
            }

            return new StoryAnswer
            {
                Outcome = StoryOutcome.Run,
                ReachChecked = true,
            };
        }

        static StoryAnswer Answer(
            in StoryFacts facts,
            StoryOutcome outcome,
            string reason)
        {
            return new StoryAnswer
            {
                Outcome = outcome,
                Reason = reason,
                ReachChecked = facts.ReachChecked,
            };
        }

        static StoryAnswer Refuse(in StoryFacts facts, Verdict verdict)
        {
            return new StoryAnswer
            {
                Outcome = StoryOutcome.Refuse,
                Verdict = verdict,
                ReachChecked = facts.ReachChecked,
            };
        }
    }
}
