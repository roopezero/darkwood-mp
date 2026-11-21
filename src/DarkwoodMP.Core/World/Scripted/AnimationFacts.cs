namespace DarkwoodMP.World.Scripted
{
    /// A character's animation frame fires a world event
    /// (AnimationTriggerListener.checkFrameTrigger).
    public readonly struct AnimationFacts
    {
        /// A session exists and is active, and WorldShare.InWorld.
        public bool Active { get; init; }

        public bool InWorld { get; init; }

        /// The part being run now, and WorldActions.Replaying.
        public EventPart Current { get; init; }

        public bool Replaying { get; init; }

        /// It comes from a Character that isn't a Player.
        public bool Character { get; init; }

        /// The world event has a name and this game has it.
        public bool Known { get; init; }

        /// This game is a client, and the character is in an area only
        /// this client is in (NetEnemies.LocalOnly).
        public bool Client { get; init; }

        public bool LocalOnly { get; init; }

        /// How many peers the session has.
        public int Peers { get; init; }

        /// The world event's GameEvents.fired and multipleFire.
        public bool Fired { get; init; }

        public bool MultipleFire { get; init; }
    }
}
