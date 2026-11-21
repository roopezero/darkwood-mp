using System.Collections.Generic;

namespace DarkwoodMP.Combat.Nights
{
    /// Host: the night attackers out at each hideout, by kind (the
    /// shell's NightSpawns._out). Each occupied hideout has the night's
    /// attackers to itself, as the one hideout of a game played alone, so
    /// players who share a hideout share its budget. The session keeps one;
    /// which hideout and kind each attacker counts against stays with the
    /// character in the shell.
    public sealed class HideoutBudgets
    {
        readonly Dictionary<int, Dictionary<string, int>> _out =
            new Dictionary<int, Dictionary<string, int>>();

        /// The attackers of a kind out at a hideout now.
        public int Out(int place, string kind)
        {
            if (!_out.TryGetValue(place, out var kinds))
            {
                return 0;
            }

            if (!kinds.TryGetValue(kind, out int n))
            {
                return 0;
            }

            return n;
        }

        /// One more of a kind sent at a hideout.
        public void Spend(int place, string kind)
        {
            if (!_out.TryGetValue(place, out var kinds))
            {
                kinds = new Dictionary<string, int>();
                _out[place] = kinds;
            }

            kinds.TryGetValue(kind, out int n);
            kinds[kind] = n + 1;
        }

        /// One of a kind sent at a hideout is gone: one fewer, never below
        /// zero. Nothing when that hideout has never had one of the kind.
        public void Gone(int place, string kind)
        {
            if (!_out.TryGetValue(place, out var kinds))
            {
                return;
            }

            if (!kinds.TryGetValue(kind, out int n))
            {
                return;
            }

            int left = n - 1;
            if (left < 0)
            {
                left = 0;
            }

            kinds[kind] = left;
        }

        /// A new day (NightScenarios.resetScenarios): every hideout's
        /// budget starts again.
        public void Clear()
        {
            _out.Clear();
        }
    }
}
