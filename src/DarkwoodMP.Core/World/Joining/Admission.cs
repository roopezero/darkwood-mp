namespace DarkwoodMP.World.Joining
{
    /// The host's answer to a joiner's Hello: the step, and why when it
    /// refuses or makes it wait.
    public readonly struct Admission
    {
        public AdmitStep Step { get; init; }
        public Refusal Refusal { get; init; }
        public JoinWait Wait { get; init; }
    }
}
