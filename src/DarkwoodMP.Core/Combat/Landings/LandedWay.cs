namespace DarkwoodMP.Combat.Landings
{
    /// What a client does with an EnemyLanded.
    public enum LandedWay
    {
        /// Make the host's thing here, with its id.
        Make,

        /// Nothing: a thing with that id is here already.
        Ignore,

        /// Nothing, with a warning: no thrown thing here leaves one of
        /// that name.
        Unknown,
    }
}
