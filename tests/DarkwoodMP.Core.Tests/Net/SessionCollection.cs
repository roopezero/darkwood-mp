namespace DarkwoodMP.Tests.Net
{
    /// The tests that make Sessions run one at a time: each new Session sets
    /// the static Session.Instance (until CoopSession replaces it).
    [CollectionDefinition(Name, DisableParallelization = true)]
    public class SessionCollection
    {
        public const string Name = "Session";
    }
}
