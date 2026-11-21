namespace DarkwoodMP.World.Scripted
{
    /// What this game is doing as a trigger of some type is set off, for
    /// whose doing it is.
    public readonly struct DoerFacts
    {
        /// Our player's own action that the host decided is being done
        /// here as a replay (ScriptedEvents.OwnAction).
        public bool OwnAction { get; init; }

        /// The type is one a player's shared action on a thing sets off
        /// (ScriptedEvents.ActionTypes: doors, switches, disarms).
        public bool ActionType { get; init; }

        /// The type is one a player's hit on a character sets off
        /// (ScriptedEvents.HitTypes).
        public bool HitType { get; init; }

        /// The type is one the world sets off by itself
        /// (ScriptedEvents.WorldTypes).
        public bool WorldType { get; init; }

        /// The type is area.
        public bool Area { get; init; }

        /// The type is onInSightOfPlayer or onOutOfSightOfPlayer.
        public bool Sight { get; init; }

        /// A session exists and is active.
        public bool Active { get; init; }

        /// This game is a client (a session exists and IsClient).
        public bool Client { get; init; }

        /// WorldShare.InWorld and WorldShare.Loading.
        public bool InWorld { get; init; }

        public bool Loading { get; init; }

        /// Coop.Guards.Replaying and Coop.Guards.Sharing.
        public bool Replaying { get; init; }

        public bool Sharing { get; init; }

        /// The part being run now (EventParts.Current).
        public EventPart Current { get; init; }

        /// Our own player's collider is entering or leaving an area
        /// (ScriptedEvents.PlayerEntered).
        public bool PlayerEntered { get; init; }

        /// The map test acts as our player (WorldActions.TestAsPlayer).
        public bool TestAsPlayer { get; init; }

        /// Another player's action is being applied
        /// (ScriptedEvents.LeftToActor isn't null).
        public bool Leaving { get; init; }

        /// The type is in that action's set (LeftToActor contains it).
        public bool LeftType { get; init; }
    }
}
