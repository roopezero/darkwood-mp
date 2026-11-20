namespace DarkwoodMP.World.Scripted
{
    /// World events fired by name: a dialogue's outcome and a character's
    /// animation (the plugin's WorldEventPatch and
    /// ScriptedEvents.FromAnimation).
    public static class WorldEventRules
    {
        /// Whether a world event fired now may be our player's to share:
        /// nothing is being run or replayed, we share, it isn't the
        /// epilogue's pan (each game runs its own) and it can still
        /// happen. The shell then asks the stack whether our player did it.
        public static bool MaySend(in WorldEventFacts f)
        {
            if (f.Current != EventPart.All || f.Replaying || !f.Sharing
                || f.AnimationSent)
            {
                return false;
            }

            if (f.Pan)
            {
                return false;
            }

            if (!f.Known || TriggerRules.Spent(f.Fired, f.MultipleFire))
            {
                return false;
            }

            return true;
        }

        /// Our player's world event is sent unless it happened in an area
        /// of our own (a dream: the host isn't there).
        public static bool Sends(bool ours, bool ownArea)
        {
            return ours && !ownArea;
        }

        /// A character's animation fires a world event. It's the world
        /// acting by itself, so the host's: a client leaves it to the host
        /// (unless the character is in an area of its own), and the host
        /// sends it to its peers while it can still happen.
        public static AnimationEvent FromAnimation(in AnimationFacts f)
        {
            if (!f.Active || !f.InWorld || f.Current != EventPart.All
                || f.Replaying)
            {
                return AnimationEvent.Run;
            }

            if (!f.Character || !f.Known)
            {
                return AnimationEvent.Run;
            }

            if (f.Client)
            {
                if (f.LocalOnly)
                {
                    return AnimationEvent.Run;
                }

                return AnimationEvent.LeaveToHost;
            }

            if (f.Peers == 0 || TriggerRules.Spent(f.Fired, f.MultipleFire))
            {
                return AnimationEvent.Run;
            }

            return AnimationEvent.SendAndRun;
        }
    }
}
