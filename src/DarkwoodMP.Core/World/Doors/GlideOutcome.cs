namespace DarkwoodMP.World.Doors
{
    /// What happens to a gliding leaf this frame.
    public enum GlideOutcome
    {
        /// The door is gone, its leaf missing, or it's shut or broken
        /// (Door.close holds a shut leaf): the glide ends, the leaf left
        /// as it is.
        Stop,

        /// It's there, or the angle is old: give the Rigidbody its
        /// kinematic flag back, set the leaf at the angle, reset its
        /// watch (LeafWatchRules.Heard), and end the glide.
        Settle,

        /// Turn the leaf to the angle, a step nearer the target.
        Turn,
    }
}
