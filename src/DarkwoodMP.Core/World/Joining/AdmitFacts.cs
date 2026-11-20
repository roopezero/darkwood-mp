namespace DarkwoodMP.World.Joining
{
    /// What the host looks at when a joiner says Hello
    /// (WorldShare.HostBeforeWelcome).
    public readonly struct AdmitFacts
    {
        /// EpilogueSync.JoinRefusal() gave a reason.
        public bool Ending { get; init; }

        /// JoinGateRules.NotReady's answer.
        public JoinWait Wait { get; init; }

        /// This joiner was offered a world in this scene, and its Hello
        /// names that world's hash.
        public bool Offered { get; init; }

        /// FreshHostWorld gave a world, and the Hello names its hash.
        public bool Fresh { get; init; }

        public bool FreshMatches { get; init; }

        /// WorldShare.InWorld.
        public bool InWorld { get; init; }
    }
}
