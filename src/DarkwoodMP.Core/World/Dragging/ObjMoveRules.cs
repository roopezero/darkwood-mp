namespace DarkwoodMP.World.Dragging
{
    /// ObjMoves heard (the plugin's DragSync.OnMove): who takes them, what
    /// the host relays, and what our game does with one.
    public static class ObjMoveRules
    {
        /// Whether an ObjMove is taken, and who decides on it.
        public static Arrival Arrive(in ArrivalFacts move)
        {
            if (!move.InWorld)
            {
                return Arrival.Drop;
            }

            if (move.Client && !move.FromHost)
            {
                return Arrival.Drop;
            }

            if (move.Host)
            {
                return Arrival.Check;
            }

            return Arrival.Apply;
        }

        /// Host: who a taken move is not relayed to. An end goes back to
        /// its mover too, so a push of the host's own sent at the same
        /// time can't be the mover's last word; a slide doesn't.
        public static ulong RelayExcept(bool last, ulong from)
        {
            if (last)
            {
                return 0;
            }

            return from;
        }

        /// What our game does with a move: not here, our own drag, our echo
        /// or shown. A client that knows an owner for a thing found hears
        /// of them.
        public static ApplyStep Apply(in ApplyFacts move)
        {
            if (!move.Found)
            {
                var action = ApplyAction.NotHere;
                if (move.Client && move.Last && move.Spawned)
                {
                    action = ApplyAction.TellHostIds;
                }

                return new ApplyStep { Action = action };
            }

            bool refresh = move.Client && move.Owned;
            if (move.Ours)
            {
                return Step(ApplyAction.KeepHostWord, refresh);
            }

            if (move.Client && move.Last)
            {
                return Step(ApplyAction.CheckEcho, refresh);
            }

            return Step(ApplyAction.Show, refresh);
        }

        static ApplyStep Step(ApplyAction action, bool refresh)
        {
            return new ApplyStep { Action = action, Refresh = refresh };
        }
    }
}
