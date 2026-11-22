namespace DarkwoodMP.World.Story
{
    /// What a GameEvent step does, as far as the story cares (the game's
    /// GameEvent.Type, sorted by the shell).
    public enum StepAction : byte
    {
        /// Any other type.
        Other = 0,

        /// transportPlayerToObject.
        Transport,

        /// startDream.
        StartDream,

        /// endGame.
        EndGame,
    }
}
