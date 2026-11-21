namespace DarkwoodMP.World.Scripted
{
    /// Whose doing a trigger set off now is (the plugin's
    /// ScriptedEvents.ByOurPlayer, LeftToActorNow, SharedReplay and
    /// CauseFor).
    public static class DoerRules
    {
        /// Whether a trigger of this type, set off now, is our own
        /// player's doing. An own action counts while together in a loaded
        /// world; a replay, a part being run, not sharing or a type the
        /// world sets off never; an area when our player walked in; sight
        /// always; anything else as the stack says.
        public static Doer ByOurPlayer(in DoerFacts f)
        {
            if (f.OwnAction && (f.ActionType || f.HitType))
            {
                if (f.Active && f.InWorld && !f.Loading)
                {
                    return Doer.Yes;
                }

                return Doer.No;
            }

            if (f.Replaying || f.Current != EventPart.All || !f.Sharing
                || f.WorldType)
            {
                return Doer.No;
            }

            if (f.Area)
            {
                if (f.PlayerEntered || f.TestAsPlayer)
                {
                    return Doer.Yes;
                }

                return Doer.No;
            }

            if (f.Sight)
            {
                return Doer.Yes;
            }

            return Doer.Stack;
        }

        /// Whether a trigger set off now is left to the player whose action
        /// is being applied (they set it off in their game and send it).
        public static bool LeftToActor(in DoerFacts f)
        {
            return f.Leaving && !f.OwnAction && f.LeftType;
        }

        /// Whether a trigger left to the actor counts as one kept from
        /// starting: it is of the type set off and not switched off.
        public static bool CountsLeft(bool matches, bool disabled)
        {
            return matches && !disabled;
        }

        /// Client: a trigger set off by applying the host's action on a
        /// thing that no player did: only its shared part runs here.
        public static bool SharedReplay(in DoerFacts f)
        {
            return f.Replaying && !f.Leaving && !f.OwnAction && f.ActionType
                && f.Client;
        }

        /// Host: the client behind a world trigger of this type set off
        /// now, or 0 (none, gone, or a type no player causes).
        public static ulong CauseFor(in CauseFacts f)
        {
            if (f.Cause == 0 || !f.Host || !f.Present)
            {
                return 0;
            }

            if (f.Caused)
            {
                return f.Cause;
            }

            return 0;
        }
    }
}
