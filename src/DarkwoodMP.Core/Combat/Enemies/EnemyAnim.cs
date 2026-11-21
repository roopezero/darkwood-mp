namespace DarkwoodMP.Combat.Enemies
{
    /// One sprite animator of the host's enemy, as EnemyState carries it.
    public readonly struct EnemyAnim
    {
        /// StableHash of the animator's path under the character.
        public int Animator { get; init; }

        /// StableHash of its clip's name, 0 for none.
        public int Clip { get; init; }

        public ushort Frame { get; init; }

        public bool Playing { get; init; }
    }
}
