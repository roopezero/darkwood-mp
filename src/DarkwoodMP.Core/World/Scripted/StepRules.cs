namespace DarkwoodMP.World.Scripted
{
    /// Which steps of a scripted event run in this game (the plugin's
    /// ScriptedEvents.Runs and LaterStep).
    public static class StepRules
    {
        /// Whether this step runs in the part being run now. A later
        /// player's part runs only the steps a later player gets; a move
        /// between world containers is the host's, not the player's.
        public static bool Runs(EventPart current, in StepFacts step)
        {
            if (current == EventPart.LaterPlayer)
            {
                return LaterStep(step);
            }

            bool player = false;
            bool host = true;
            if (!step.WorldMove)
            {
                player = step.PlayerType || step.AreaLook
                    || step.TargetsPlayer;
                host = step.HostType;
            }

            switch (current)
            {
                case EventPart.SharedAndMessages:
                    return !host && (!player || step.Message);
                case EventPart.PlayerAndShared:
                    return !host;
                case EventPart.HostAndShared:
                    return !player;
                case EventPart.SharedOnly:
                    return !player && !host;
                case EventPart.PlayerOnly:
                    return player && !host;
                default:
                    return true;
            }
        }

        /// Whether a later player gets this step of a once-only trigger
        /// that fired for someone else: trips, what goes with them on the
        /// screen, the player's own records, ordinary items added to the
        /// player's own inventory, and any other step that acts on the
        /// player. Never a dialogue, a move into a container or the end.
        public static bool LaterStep(in StepFacts step)
        {
            if (!step.Exists || step.Disabled)
            {
                return false;
            }

            switch (step.Type)
            {
                case StepType.PlayerTrip:
                    return !step.ActiveModifier;
                case StepType.Later:
                    return true;
                case StepType.Item:
                    if (!step.ActiveModifier || step.ActiveModifier2
                        || step.RelentlessPursuit)
                    {
                        return false;
                    }

                    return !step.SpecialItem;
                case StepType.NotLater:
                    return false;
                default:
                    return step.TargetsPlayer;
            }
        }
    }
}
