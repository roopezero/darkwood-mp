using System;
using System.Collections.Generic;
using System.Linq;

namespace DarkwoodMP.Menus
{
    /// The server browser's sort: which column and which way, and the
    /// list in that order (DirectPanel._sortColumn, _sortDown, Sorted and
    /// the header's click). One per browser, kept while the game runs.
    public sealed class BrowserOrder
    {
        /// The lock column: password first when sorted down.
        public const int Lock = 0;

        /// The name column.
        public const int Name = 1;

        /// The players column.
        public const int Players = 2;

        /// The world column.
        public const int World = 3;

        /// The ping column, the one sorted by at first.
        public const int Ping = 4;

        /// The column sorted by.
        public int Column { get; private set; } = Ping;

        /// Sorted from the largest down; else from the smallest up.
        public bool Down { get; private set; }

        /// A click on a column's header: the same column turns the order
        /// round, another one sorts by it from the smallest up.
        public void Click(int column)
        {
            if (Column == column)
            {
                Down = !Down;
                return;
            }

            Column = column;
            Down = false;
        }

        /// The addresses in the order the list shows them. The lock column
        /// sorted up shows the saved order as it is, except on the LAN tab,
        /// which has none; the ping column, the first one, is sorted from
        /// the start. Down is the up order reversed, so equal servers are
        /// reversed too. Text is compared as string.CompareTo does (the
        /// current culture), as the shell's IComparable keys are.
        public List<string> Sorted(IReadOnlyList<ServerRow> rows, bool lan)
        {
            if (Column == Lock && !Down && !lan)
            {
                return rows.Select(row => row.Address).ToList();
            }

            var sorted = rows.OrderBy(Key);
            IEnumerable<ServerRow> shown = sorted;
            if (Down)
            {
                shown = sorted.Reverse();
            }

            return shown.Select(row => row.Address).ToList();
        }

        /// What a row is sorted by in the column: a server that didn't
        /// answer has no lock, its address as its name, -1 players, an
        /// empty world and the longest ping.
        IComparable Key(ServerRow row)
        {
            switch (Column)
            {
                case Lock:
                    if (row.Known && row.Password)
                    {
                        return 1;
                    }

                    return 0;
                case Name:
                    if (row.Known && row.Name != null)
                    {
                        return row.Name.ToLowerInvariant();
                    }

                    return row.Address.ToLowerInvariant();
                case Players:
                    if (row.Known)
                    {
                        return row.Players;
                    }

                    return -1;
                case World:
                    if (row.Known && row.World != null)
                    {
                        return row.World;
                    }

                    return "";
                default:
                    if (row.Known)
                    {
                        return row.Ping;
                    }

                    return int.MaxValue;
            }
        }
    }
}
