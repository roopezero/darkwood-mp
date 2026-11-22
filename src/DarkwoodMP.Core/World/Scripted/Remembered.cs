namespace DarkwoodMP.World.Scripted
{
    /// What to remember about a trigger's coroutine (the plugin's
    /// Started). The zero value remembers nothing.
    public readonly struct Remembered
    {
        /// Something is remembered.
        public bool Remembers { get; init; }

        /// The host's world set it off by itself (sent as kind 3).
        public bool World { get; init; }

        /// Our own player (or, on the host, the world) set it off, and
        /// it's to be shared when it fires.
        public bool ByPlayer { get; init; }

        /// The part its steps run in.
        public EventPart Part { get; init; }
    }
}
