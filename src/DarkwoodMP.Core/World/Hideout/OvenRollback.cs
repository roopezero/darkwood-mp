namespace DarkwoodMP.World.Hideout
{
    /// What a refused oven undoes. Zero: nothing.
    public readonly struct OvenRollback
    {
        /// Put it out again (ExperienceMachine.disable).
        public bool PutOut { get; init; }

        /// Make the old oven our player's own again.
        public bool GiveBack { get; init; }
    }
}
