using System.Collections.Generic;

namespace DarkwoodMP.World.Dragging
{
    /// Client: where we said one thing came to rest, oldest first, until
    /// the host passes each back to us (DragSync._unechoed's list). The
    /// shell keeps one per thing and forgets it once Empty.
    public sealed class EchoList
    {
        /// The most rests kept for one thing.
        public const int Most = 16;

        /// Squared distance under which a move is one of ours (0.1 units).
        public const float Match = 0.01f;

        readonly List<Vec3> _sent = new List<Vec3>();

        /// Nothing is waiting to be passed back.
        public bool Empty => _sent.Count == 0;

        public int Count => _sent.Count;

        /// We sent this rest to the host. Past Most, it isn't kept.
        public void Sent(Vec3 pos)
        {
            if (_sent.Count < Most)
            {
                _sent.Add(pos);
            }
        }

        /// The host sent this last move: whether it is one of ours passed
        /// back. The first rest of ours within Match is, and it and every
        /// older one are dropped. When none is, the whole list goes, so
        /// every later move is taken too.
        public bool Take(Vec3 pos)
        {
            int found = -1;
            for (int i = 0; i < _sent.Count; i++)
            {
                if (Square(_sent[i], pos) < Match)
                {
                    found = i;
                    break;
                }
            }

            if (found < 0)
            {
                _sent.Clear();
                return false;
            }

            _sent.RemoveRange(0, found + 1);
            return true;
        }

        /// As Unity's (a - b).sqrMagnitude.
        static float Square(Vec3 a, Vec3 b)
        {
            float x = a.X - b.X;
            float y = a.Y - b.Y;
            float z = a.Z - b.Z;
            return x * x + y * y + z * z;
        }
    }
}
