namespace DarkwoodMP.World.Switches
{
    /// Switches: the host's checks of a client's ItemOn, ItemOff (the
    /// plugin's WorldActions.Allowed) and InteractiveSwitch
    /// (ObjectActions.Allowed).
    public static class SwitchRules
    {
        /// Host: whether a client's player could have turned this Item on
        /// or off: here and in reach. There is no state check, so a lamp
        /// that is on already can be turned on again.
        public static Verdict MayTurn(in SwitchFacts item)
        {
            if (!item.Exists)
            {
                return Verdict.Refuse(Refusal.NotHere);
            }

            return InReach(item.Requester);
        }

        /// Host: whether a client's player could have switched this
        /// InteractiveItem: here, a switch, in reach, and not already as
        /// the client says it is now.
        public static Verdict MaySwitch(in SwitchFacts item)
        {
            if (!item.Exists)
            {
                return Verdict.Refuse(Refusal.NotHere);
            }

            if (!item.IsSwitch)
            {
                return Verdict.Refuse(Refusal.NotASwitch);
            }

            var reach = InReach(item.Requester);
            if (reach.Refused)
            {
                return reach;
            }

            if (item.IsOn == item.On && item.On)
            {
                return Verdict.Refuse(Refusal.AlreadyOn);
            }

            if (item.IsOn == item.On)
            {
                return Verdict.Refuse(Refusal.AlreadyOff);
            }

            return Verdict.Allow;
        }

        static Verdict InReach(in ReachFacts requester)
        {
            return ReachRules.InReach(
                requester with { Reach = ReachRules.Reach });
        }
    }
}
