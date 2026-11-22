using System.Collections.Generic;

namespace DarkwoodMP.World.Inventories
{
    /// Host: who has each container open, one player at a time (the
    /// plugin's Containers._locks). A container is named by its ObjRef as
    /// text. Nothing here checks reach, and a workbench's storage is never
    /// locked: the shell doesn't ask for it.
    public sealed class ContainerLocks
    {
        readonly Dictionary<string, ulong> _holders =
            new Dictionary<string, ulong>();

        /// How many containers someone has open.
        public int Count => _holders.Count;

        /// A player opens a container: they hold it, unless another player
        /// does, who is then given in holder (and the opener is told it's
        /// busy). Opening one they hold already keeps it theirs.
        public bool Open(string key, ulong who, out ulong holder)
        {
            if (_holders.TryGetValue(key, out holder) && holder != who)
            {
                return false;
            }

            _holders[key] = who;
            holder = who;
            return true;
        }

        /// A player closes a container: that lets go of it only if they
        /// hold it.
        public void Close(string key, ulong who)
        {
            if (_holders.TryGetValue(key, out var holder) && holder == who)
            {
                _holders.Remove(key);
            }
        }

        /// Whether a player other than who holds the container, and who.
        public bool HeldByOther(string key, ulong who, out ulong holder)
        {
            if (!_holders.TryGetValue(key, out holder) || holder == who)
            {
                holder = 0;
                return false;
            }

            return true;
        }

        /// A player left the session: they let go of everything they held.
        public void Leave(ulong who)
        {
            var gone = new List<string>();
            foreach (var kv in _holders)
            {
                if (kv.Value == who)
                {
                    gone.Add(kv.Key);
                }
            }

            foreach (var key in gone)
            {
                _holders.Remove(key);
            }
        }

        /// The session ended: nobody holds anything.
        public void Clear()
        {
            _holders.Clear();
        }
    }
}
