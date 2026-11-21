namespace DarkwoodMP.World.Dreaming
{
    /// Level dreams dreamt together (DreamSync): the host draws the dream
    /// and sends it, clients go in once their menus let them, and an end in
    /// any game ends everyone's.
    public static class DreamRules
    {
        /// How long a client keeps trying to go into the host's dream.
        public static readonly Seconds GiveUpAfter = new Seconds(60f);

        /// How long a client waits after the stove's menu before it goes
        /// into the host's dream.
        public static readonly Seconds StoveWait = new Seconds(0.7f);

        /// How long after a replayed end the held-player check begins.
        public static readonly Seconds InputsGrace = new Seconds(6f);

        /// How long after it began the held-player check gives up.
        public static readonly Seconds InputsGiveUp = new Seconds(40f);

        /// Whether the game is in a dream or on its way into one.
        public static bool InDream(in DreamFacts dream)
        {
            return dream.HasDreams
                && (dream.Dreaming
                    || dream.Prepared
                    || dream.Switching
                    || dream.EnteringDream);
        }

        /// Whether a client's level keeps the game's own dream off
        /// (Dreams.disabled for the call): a client playing together. The
        /// host's levels bring the shared dreams.
        public static bool ClientDreamOff(
            bool hasDreams,
            bool clientMode,
            bool together)
        {
            return hasDreams && clientMode && together;
        }

        /// Whether the host marks its level dream and draws it at once
        /// (DreamSync.AfterConfirm), so the clients start with it.
        public static bool DrawsEarly(
            in DreamFacts dream,
            in DreamShareFacts share)
        {
            return share.HasSession
                && share.IsHost
                && share.Together
                && dream.HasDreams
                && dream.WantToDream
                && dream.EnteringDream;
        }

        /// What the host does with a preset the game drew
        /// (DreamSync.PresetChosen). While the game loads, the mark stays.
        public static PresetStep PresetChosen(
            in DreamFacts dream,
            in DreamShareFacts share)
        {
            if (!share.Marked || dream.LoadingGame)
            {
                return PresetStep.Keep;
            }

            if (!share.HasPreset
                || !share.HasSession
                || !share.IsHost
                || !share.Together)
            {
                return PresetStep.Clear;
            }

            return PresetStep.Share;
        }

        /// What our game does as its dream ends (DreamSync.Ending): only
        /// for the shared dream, ending here and not replayed. It is marked
        /// ended even when nothing is sent.
        public static EndSend Ending(
            in DreamFacts dream,
            in DreamShareFacts share)
        {
            if (!share.Shared
                || !dream.HasDreams
                || !dream.Dreaming
                || share.Ended
                || share.Replaying
                || !share.HasSession)
            {
                return EndSend.None;
            }

            if (share.IsHost)
            {
                return EndSend.Broadcast;
            }

            if (share.ClientMode)
            {
                return EndSend.ToHost;
            }

            return EndSend.Mark;
        }

        /// What the host does with a client's DreamEnd (DreamSync.OnEnd).
        /// The outcome must be one the epilogue allows; the shell asks
        /// EpilogueSync only after the other checks pass.
        public static HostEndStep HostEnd(
            in DreamFacts dream,
            in DreamShareFacts share)
        {
            if (!share.Shared
                || !dream.HasDreams
                || !dream.Dreaming
                || share.Ended
                || !share.OutcomeAllowed)
            {
                return HostEndStep.Drop;
            }

            if (!share.SameDream)
            {
                return HostEndStep.NotPassedOn;
            }

            return HostEndStep.RelayAndEnd;
        }

        /// What a client does with the host's DreamEnd (DreamSync.OnEnd).
        /// The dream's name isn't looked at.
        public static ClientEndStep ClientEnd(
            in DreamFacts dream,
            in DreamShareFacts share)
        {
            if (!share.FromHost || !share.Shared || !dream.HasDreams)
            {
                return ClientEndStep.Drop;
            }

            if (!dream.Dreaming)
            {
                if (InDream(dream) || share.PendingStart)
                {
                    return ClientEndStep.KeepPending;
                }

                return ClientEndStep.Drop;
            }

            if (share.Ended)
            {
                return ClientEndStep.Drop;
            }

            return ClientEndStep.End;
        }

        /// What a client does with the host's DreamStart
        /// (DreamSync.OnStart). A pending start clears the pending end and
        /// gives up at StartBy.
        public static StartStep Start(
            in DreamFacts dream,
            in DreamShareFacts share)
        {
            if (!share.FromHost || !share.ClientMode || !dream.HasDreams)
            {
                return StartStep.Drop;
            }

            if (InDream(dream))
            {
                return StartStep.AlreadyIn;
            }

            return StartStep.Pending;
        }

        /// When a pending start begun now gives up.
        public static Seconds StartBy(Seconds now)
        {
            return new Seconds(now.Value + GiveUpAfter.Value);
        }

        /// What a client does with a pending end this frame: once its dream
        /// has begun (dreaming, not switching), it is cleared, and ends ours
        /// unless ours had ended.
        public static PendingEnd OnPendingEnd(
            in DreamFacts dream,
            bool pending,
            bool ended)
        {
            if (!pending
                || !dream.HasDreams
                || !dream.Dreaming
                || dream.Switching)
            {
                return PendingEnd.Keep;
            }

            if (ended)
            {
                return PendingEnd.Drop;
            }

            return PendingEnd.End;
        }

        /// What a client does with a pending start this frame
        /// (DreamSync.Update): drop it when it can't go, give up after
        /// GiveUpAfter, wait for the skill choice or a talk, wait StoveWait
        /// after the stove's menu, then go.
        public static JoinStep Join(in JoinFacts join)
        {
            if (!join.ClientMode
                || !join.InWorld
                || !join.HasDreams
                || !join.HasPlayer)
            {
                return Step(JoinAction.Drop, join.StartAt);
            }

            if (join.InDream)
            {
                return Step(JoinAction.Drop, join.StartAt);
            }

            if (join.Now > join.StartBy)
            {
                return Step(JoinAction.GiveUp, join.StartAt);
            }

            if (join.SkillsMenuOpen || join.InDialogue)
            {
                return Step(JoinAction.Wait, join.StartAt);
            }

            if (join.AtStove)
            {
                var at = new Seconds(join.Now.Value + StoveWait.Value);
                return Step(JoinAction.Stove, at);
            }

            if (join.Now < join.StartAt)
            {
                return Step(JoinAction.Wait, join.StartAt);
            }

            return Step(JoinAction.Go, join.StartAt);
        }

        /// When the held-player check begins after a replayed end; only a
        /// client checks (EndHere's ClientMode).
        public static Seconds InputsFrom(Seconds now)
        {
            return new Seconds(now.Value + InputsGrace.Value);
        }

        /// The held-player check this frame (DreamSync.Update and
        /// CheckInputs). A From of 0 or less means no check.
        public static InputStep Inputs(in InputFacts inputs)
        {
            if (!(inputs.From.Value > 0f) || !(inputs.Now > inputs.From))
            {
                return InputStep.NotYet;
            }

            float last = inputs.From.Value + InputsGiveUp.Value;
            if (inputs.Now.Value > last || !inputs.InWorld)
            {
                return InputStep.Stop;
            }

            if (!inputs.ForbidInputs)
            {
                return InputStep.Stop;
            }

            if (!inputs.HeldByNothing)
            {
                return InputStep.Wait;
            }

            return InputStep.LetGo;
        }

        /// Whether nothing of the game's own holds the player now, so a
        /// leftover Core.forbidInputs can be let go. Prologue uses it too.
        public static bool HeldByNothing(in HoldFacts hold)
        {
            return hold.HasPlayer
                && !hold.Locked
                && !hold.LoadingGame
                && !hold.Cutscene
                && !hold.OutsideLoading
                && !hold.Switching
                && !hold.EnteringDream
                && !hold.InDialogue;
        }

        /// Whether the shared dream is over here (DreamSync.AfterEnd): not
        /// dreaming, not switching, and no epilogue playing. The shell then
        /// forgets it.
        public static bool Over(in DreamFacts dream, in DreamShareFacts share)
        {
            return share.Shared
                && dream.HasDreams
                && !dream.Dreaming
                && !dream.Switching
                && !share.EpiloguePlaying;
        }

        static JoinStep Step(JoinAction action, Seconds at)
        {
            return new JoinStep { Action = action, StartAt = at };
        }
    }
}
