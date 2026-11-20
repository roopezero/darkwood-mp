namespace DarkwoodMP.World
{
    /// Host: the save id counters of the joiners let in to one loaded
    /// world. The zero value is "none": Min counts only when Max is above
    /// 0 (the plugin starts it at int.MaxValue instead).
    public readonly struct JoinerIds
    {
        public int Min { get; init; }

        public int Max { get; init; }

        /// The scene's handle the counters are for.
        public int Scene { get; init; }
    }
}
