namespace DarkwoodMP.Combat.Enemies
{
    /// An enemy's id and a yes or no about it, as EnemyArmor (its shadow
    /// armour is on) and EnemyDespawn (its corpse is kept) carry them.
    public readonly struct EnemyMark
    {
        public uint Id { get; init; }

        public bool On { get; init; }
    }
}
