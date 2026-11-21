namespace DarkwoodMP.World.Scripted
{
    /// A trigger set off now as some type, for whether the host's world
    /// runs it for everyone.
    public readonly struct WorldTriggerFacts
    {
        /// The trigger and its EventTriggers exist, and its type is the
        /// type set off.
        public bool Matches { get; init; }

        /// The type set off is one the host's world sets off for everyone
        /// (ScriptedEvents.HostWorldTypes).
        public bool HostWorldType { get; init; }

        /// The type set off is onSpawned or onGameObjectActivated.
        public bool Appears { get; init; }

        /// A session exists and is active.
        public bool Active { get; init; }

        /// This game is a client.
        public bool Client { get; init; }

        /// The part being run now (ScriptedEvents.Current).
        public EventPart Current { get; init; }

        /// How many event steps are running (ScriptedEvents.StepDepth).
        public int StepDepth { get; init; }

        /// The EventTriggers' scene is the active scene.
        public bool InActiveScene { get; init; }

        /// It is in an area only this client is in
        /// (NetEnemies.LocalOnly).
        public bool LocalOnly { get; init; }

        /// Its ObjRef is valid.
        public bool Named { get; init; }

        /// Its ObjRef is of a saved object (ObjRef.Kind.Saved).
        public bool Saved { get; init; }

        /// EventTrigger.multipleFire.
        public bool MultipleFire { get; init; }
    }
}
