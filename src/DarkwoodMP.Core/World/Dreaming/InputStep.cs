namespace DarkwoodMP.World.Dreaming
{
    /// What a client's check of a held player does this frame.
    public enum InputStep
    {
        /// Not yet begun.
        NotYet,

        /// The check ends: given up, out of the world, or inputs free.
        Stop,

        /// Something of the game's own holds the player: check again.
        Wait,

        /// The check ends, and the player is let go.
        LetGo,
    }
}
