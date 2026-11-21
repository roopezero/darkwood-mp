using System.Collections.Generic;

namespace DarkwoodMP.Tests.Net
{
    /// Where one host and any number of clients meet in memory. Ids are as
    /// TcpBackend gives them: the host is 1, clients 1000 and up.
    public sealed class LoopbackHub
    {
        public const ulong HostId = 1;

        private readonly List<LoopbackBackend> _all =
            new List<LoopbackBackend>();
        private ulong _nextId = 1000;

        /// The backend hosting now (null: none).
        public LoopbackBackend Hosting { get; internal set; }

        /// Whether nothing is waiting to be delivered anywhere.
        public bool Idle
        {
            get
            {
                foreach (var b in _all)
                {
                    if (b.Waiting > 0)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        public LoopbackBackend NewBackend()
        {
            var b = new LoopbackBackend(this);
            _all.Add(b);
            return b;
        }

        internal ulong NextClientId() => _nextId++;

        internal LoopbackBackend Find(ulong id)
        {
            foreach (var b in _all)
            {
                if (b.InSession && b.Id == id)
                {
                    return b;
                }
            }

            return null;
        }
    }
}
