namespace DarkwoodMP.World.Mornings
{
    /// What the host does to its clock this frame.
    public enum ClockChange : byte
    {
        /// Leave Controller.DoUpdateTime as it is.
        None = 0,

        /// A joiner is out: DoUpdateTime = true, logged.
        Run,

        /// Every joiner is back in a morning: DoUpdateTime = false, logged.
        Stop,
    }
}
