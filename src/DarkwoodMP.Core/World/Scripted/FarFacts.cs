namespace DarkwoodMP.World.Scripted
{
    /// A location (the game's Location) whose enter or exit events start,
    /// or that the host looks at each culling refresh.
    public readonly struct FarFacts
    {
        /// A session is active and WorldShare.InWorld.
        public bool Together { get; init; }

        /// Location.isSubLocation and isOutsideLocation.
        public bool Sub { get; init; }

        public bool Outside { get; init; }

        /// The part being run now, and Coop.Guards.Replaying.
        public EventPart Current { get; init; }

        public bool Replaying { get; init; }

        /// This game is a client.
        public bool Client { get; init; }

        /// Host: a client has a stand-in (Proxies.Any).
        public bool AnyProxies { get; init; }

        /// Host: the host's own player.
        public HostSpot Host { get; init; }

        /// Our culling's distances (SharedCullingPatch.Distances): dx on
        /// the x axis, dy on the z axis.
        public int CullX { get; init; }

        public int CullZ { get; init; }

        /// The location, on the ground plane.
        public Vec2 At { get; init; }

        /// Host: whether the host's player was in it when last looked at
        /// (FarEvents.HostIn), and whether there is such a record.
        public bool Recorded { get; init; }

        public bool HostIn { get; init; }

        /// Location.entered, finishedLoading and activatingOverTime.
        public bool Entered { get; init; }

        public bool Loaded { get; init; }

        public bool Activating { get; init; }
    }
}
