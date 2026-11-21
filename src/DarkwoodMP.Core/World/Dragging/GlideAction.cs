namespace DarkwoodMP.World.Dragging
{
    /// What a thing shown moving does this frame.
    public enum GlideAction : byte
    {
        /// Glides toward the move (lerp and slerp by GlideRules.Blend).
        Glide = 0,

        /// Is put exactly there, and stays shown moving.
        Put,

        /// Is put exactly there and let go of: released, known to be
        /// there, put into its culling node and no longer shown moving.
        Release,
    }
}
