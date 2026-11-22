using System.Collections.Generic;

namespace DarkwoodMP.World
{
    /// The ids of one HostIds message: Count of List from Start.
    public readonly struct HostIdSlice
    {
        public IReadOnlyList<HostIdEntry> List { get; init; }

        public int Start { get; init; }

        public int Count { get; init; }
    }
}
