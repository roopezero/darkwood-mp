using System.Collections.Generic;

namespace DarkwoodMP.World.Story
{
    /// The epilogue played together: who may join, which dream ends count,
    /// the pan over the burning forest, the outcomes and the fire
    /// (EpilogueSync).
    public static class EpilogueRules
    {
        /// The world event of the pan over the burning forest.
        public const string PanEvent = "epilogue_cameraPanOverBurningForest";

        /// The crater's events that set the Being on fire.
        public const string BurnEvents = "Istota_lv1_events";

        /// The start of every epilogue dream's preset name.
        public const string DreamPrefix = "epilog_";

        /// Seconds (realtime) after the first death in the burning forest
        /// before the pan runs anyway.
        public const float PanWait = 60f;

        /// Our world is in the epilogue on its own, so it can't be hosted
        /// (EpilogueSync.InEpilogueAlone): never while Playing; our player
        /// is in it, or a dream whose preset is an epilogue's is on.
        public static bool InEpilogueAlone(
            bool playing,
            bool inEpilogue,
            in DreamFacts dreams)
        {
            if (playing)
            {
                return false;
            }

            if (inEpilogue)
            {
                return true;
            }

            return dreams.Exists
                && (dreams.Dreaming || dreams.Wanted || dreams.Prepared)
                && dreams.HasPreset
                && dreams.Preset != null
                && dreams.Preset.StartsWith(DreamPrefix);
        }

        /// Host: whether a new player may join now
        /// (EpilogueSync.JoinRefusal): not once the host's epilogue is at
        /// its end (the outcomes screen, then the credits). Before that a
        /// joiner goes into the host's part (PartToJoin).
        public static Verdict JoinRefusal(in EpilogueJoinFacts facts)
        {
            if (facts.Playing && facts.Host && facts.AtEnd)
            {
                return Verdict.Refuse(Refusal.PlayingEnding);
            }

            return Verdict.Allow;
        }

        /// Host: a joiner waits while our game is between two parts of the
        /// epilogue: a dream on its way (wantToDream), one part switching to
        /// the next, or a location loading.
        public static bool BetweenParts(in DreamFacts dreams, bool loading)
        {
            return loading || dreams.Wanted || dreams.Switching;
        }

        /// Host: the part a joiner goes into: our dream's preset when it is
        /// an epilogue's, else the first part.
        public static string PartToJoin(in DreamFacts dreams)
        {
            if (dreams.Exists
                && dreams.HasPreset
                && dreams.Preset != null
                && dreams.Preset.StartsWith(DreamPrefix))
            {
                return dreams.Preset;
            }

            return StoryRequestRules.EpilogueDream;
        }

        /// Joiner: the dream a save goes back into as it loads
        /// (Dreams.prepareDream with loadingSaveGameInDream): the host's
        /// part (joining; null when we aren't joining the host's
        /// epilogue) in place of an epilogue's, else the save's own.
        public static string LoadedPart(
            string joining,
            string preset,
            bool loadingInDream)
        {
            if (joining == null
                || !loadingInDream
                || preset == null
                || !preset.StartsWith(DreamPrefix))
            {
                return preset;
            }

            return joining;
        }

        /// Host: our world went into the epilogue with no shared one on (a
        /// save made in it was loaded, or it began before hosting): it
        /// becomes the shared epilogue, which others then join. Not once
        /// this session's epilogue went to the credits.
        public static bool Adopts(bool host, bool alone, bool over)
        {
            return host && alone && !over;
        }

        /// Whether a dream's end counts (EpilogueSync.OutcomeAllowed):
        /// while Playing, only one of the preset's outcomes by name.
        /// outcomes holds the names of the preset's outcomes that aren't
        /// null; null means no Dreams or no preset, and allows any.
        public static bool OutcomeAllowed(
            bool playing,
            IReadOnlyList<string> outcomes,
            string outcome)
        {
            if (!playing || outcomes == null)
            {
                return true;
            }

            for (var i = 0; i < outcomes.Count; i++)
            {
                if (outcomes[i] == outcome)
                {
                    return true;
                }
            }

            return false;
        }

        /// What going to the credits does (EpilogueSync.BeforeCredits).
        public static CreditsStep BeforeCredits(
            bool session,
            bool playing,
            bool host)
        {
            if (!session || !playing)
            {
                return CreditsStep.None;
            }

            if (host)
            {
                return CreditsStep.HostStops;
            }

            return CreditsStep.JoinerLeaves;
        }

        /// What happens to a world event (EpilogueSync.WorldEventFired).
        /// Once the pan is released, the first of ours and the game's own
        /// runs; before that it waits.
        public static PanEventStep WorldEvent(in PanEventFacts facts)
        {
            if (!facts.Playing || !facts.IsPan || !facts.InSession)
            {
                return PanEventStep.Runs;
            }

            if (facts.Released)
            {
                if (facts.PanRan)
                {
                    return PanEventStep.Held;
                }

                return PanEventStep.RunsFirst;
            }

            return PanEventStep.Waits;
        }

        /// Whether our player's death is the crawl's end in the burning
        /// forest, counted once (EpilogueSync.AfterDie).
        public static bool CountsDeath(in DeathFacts facts)
        {
            return facts.Playing
                && !facts.Counted
                && facts.InSession
                && facts.HasPlayer
                && facts.InEpilogue
                && facts.Crawling
                && facts.Dying;
        }

        /// Whether our dead player watches the others, a while after the
        /// death (EpilogueSync.WatchSoon).
        public static bool WatchesSoon(
            bool playing,
            bool released,
            bool spectating)
        {
            return playing && !released && !spectating;
        }

        /// Whether the pan's timeout releases it (EpilogueSync.PanTimeout).
        public static bool PanTimesOut(bool playing, bool released)
        {
            return playing && !released;
        }

        /// The body's clip that petting the dog in the epilogue's flat
        /// plays (Player.special_petDog); the player stands still until it
        /// ends.
        public const string PetDogClip = "PetDog";

        /// Whether our player may start petting the dog: in a session, only
        /// when the body's animation library has the clip. Only the
        /// epilogue's clothes (PlayerEpilogueAnims) have it; without it
        /// the clip never plays, so it never ends, and the player stood
        /// still for good (a joiner whose clothes the host's change had
        /// used up). Playing alone is left as the game does it.
        public static bool MayPetDog(bool inSession, bool hasClip)
        {
            return !inSession || hasClip;
        }

        /// Whether the host sends its outcomes (EpilogueSync.SendOutcomes).
        public static bool SendsOutcomes(bool playing, bool host, int peers)
        {
            return playing && host && peers != 0;
        }

        /// What EpilogueOutcomes.Start does (EpilogueSync.OutcomesStart).
        public static OutcomeStep OutcomesStart(in OutcomeFacts facts)
        {
            if (!facts.Exists || !facts.Playing || !facts.InSession)
            {
                return OutcomeStep.None;
            }

            if (facts.IsHost)
            {
                return OutcomeStep.Send;
            }

            if (facts.Host == HostBurned.Unheard
                || facts.Burned == (facts.Host == HostBurned.Yes))
            {
                return OutcomeStep.Keep;
            }

            return OutcomeStep.TakeHost;
        }

        /// The host's outcome from an Outcomes step's value: "1" is burned,
        /// anything else isn't.
        public static HostBurned HeardBurned(string value)
        {
            if (value == "1")
            {
                return HostBurned.Yes;
            }

            return HostBurned.No;
        }

        /// Whether a joiner moves off the others when an epilogue part
        /// starts (EpilogueSync.PartStarted).
        public static bool OffsetsPart(bool playing, bool client)
        {
            return playing && client;
        }

        /// Whether the free spot is far enough to move to
        /// (EpilogueSync.OffsetSoon): 1 or more away, squared.
        public static bool Moves(Vec3 at, Vec3 to)
        {
            var x = to.X - at.X;
            var y = to.Y - at.Y;
            var z = to.Z - at.Z;
            return !(x * x + y * y + z * z < 1f);
        }

        /// What an EpilogueStep message does (EpilogueSync.OnStep). A joiner
        /// takes JoinPart from the host at any time; nothing else while not
        /// Playing. The host takes Burn and Crawled from a peer;
        /// a joiner takes Outcomes, Burn and Pan from the host. Nothing else
        /// is checked.
        public static StepHandling OnStep(in EpilogueStepFacts facts)
        {
            // the host's word to a new joiner, which isn't Playing yet
            if (!facts.IsHost
                && facts.FromHost
                && facts.Step == EpilogueStep.JoinPart)
            {
                return StepHandling.JoinPart;
            }

            if (!facts.Playing)
            {
                return StepHandling.Ignore;
            }

            if (facts.IsHost)
            {
                return FromPeer(facts);
            }

            if (!facts.FromHost)
            {
                return StepHandling.Ignore;
            }

            switch (facts.Step)
            {
                case EpilogueStep.Outcomes:
                    return StepHandling.Outcomes;
                case EpilogueStep.Burn:
                    return StepHandling.Burned;
                case EpilogueStep.Pan:
                    return StepHandling.Pan;
                case EpilogueStep.Layout:
                    return StepHandling.Layout;
                default:
                    return StepHandling.Ignore;
            }
        }

        static StepHandling FromPeer(in EpilogueStepFacts facts)
        {
            if (!facts.FromPeer)
            {
                return StepHandling.Ignore;
            }

            if (facts.Step == EpilogueStep.Burn)
            {
                return StepHandling.Burned;
            }

            if (facts.Step == EpilogueStep.Crawled)
            {
                return StepHandling.Crawled;
            }

            if (facts.Step == EpilogueStep.LayoutAsk)
            {
                return StepHandling.LayoutAsk;
            }

            return StepHandling.Ignore;
        }

        /// Whether our player's fire is shared with the others
        /// (EpilogueSync.EventsFired): once, the crater's events, not a
        /// replay.
        public static bool SharesBurn(in BurnShareFacts facts)
        {
            return facts.Playing
                && !facts.Shared
                && facts.Name == BurnEvents
                && !facts.Replaying
                && facts.InSession;
        }

        /// What another player's fire does here (EpilogueSync.Burned).
        /// Anything but Ignored marks it shared, relays it from the host
        /// and shows the notice first.
        public static BurnStep Burned(in BurnFacts facts)
        {
            if (facts.Shared)
            {
                return BurnStep.Ignored;
            }

            if (!facts.Found)
            {
                return BurnStep.Missing;
            }

            if (facts.Fired && !facts.Multiple)
            {
                return BurnStep.AlreadyFired;
            }

            return BurnStep.Runs;
        }
    }
}
