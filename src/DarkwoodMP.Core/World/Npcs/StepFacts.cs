namespace DarkwoodMP.World.Npcs
{
    /// Host: a scripted event step as it starts (GameEvent.fire), for
    /// FollowerRules. Each flag compares one of the step's fields with one
    /// value.
    public readonly struct StepFacts
    {
        /// Session.Instance exists and is the host.
        public bool Hosting { get; init; }

        /// The peer whose event this is (EventParts.By); 0 for the
        /// host's own and the world's.
        public ulong By { get; init; }

        /// GameEvent.type is modifyCharacter.
        public bool ModifiesCharacter { get; init; }

        /// GameEvent.type is gameObject.
        public bool SwitchesObject { get; init; }

        /// GameEvent.characterModifyType is aggressiveness.
        public bool SetsAggressiveness { get; init; }

        /// GameEvent.destAggressiveness is follower.
        public bool ToFollower { get; init; }

        /// GameEvent.characterModifyType is behaviour.
        public bool SetsBehaviour { get; init; }

        /// GameEvent.destCharacterBehaviour is following.
        public bool ToFollowing { get; init; }
    }
}
