using System.Collections.Generic;
using DarkwoodMP.Net;

namespace DarkwoodMP.World.Traders
{
    /// The bytes of MsgType.TraderTrade: the trader (TraderWho), what was
    /// taken and what was given (each u16 n, then n of str type + i32
    /// count), bigstr the stock after, i32 the reputation change.
    public static class TraderTrade
    {
        /// The payload, after Begin(MsgType.TraderTrade).
        public static void Write(PacketWriter w, in Trade trade)
        {
            trade.Trader.Write(w);
            WriteItems(w, trade.Took);
            WriteItems(w, trade.Gave);
            w.WriteBigStr(trade.Stock);
            w.W.Write(trade.Change);
        }

        /// A type that comes twice in a list counts both.
        public static Trade Read(PacketReader r)
        {
            var trader = TraderWho.Read(r);
            var took = ReadItems(r);
            var gave = ReadItems(r);
            string stock = r.ReadBigStr();
            int change = r.R.ReadInt32();
            return new Trade
            {
                Trader = trader,
                Took = took,
                Gave = gave,
                Stock = stock,
                Change = change,
            };
        }

        static void WriteItems(
            PacketWriter w,
            IReadOnlyDictionary<string, int> items)
        {
            w.W.Write((ushort)items.Count);
            foreach (var kv in items)
            {
                w.WriteStr(kv.Key);
                w.W.Write(kv.Value);
            }
        }

        static Dictionary<string, int> ReadItems(PacketReader r)
        {
            var items = new Dictionary<string, int>();
            int n = r.R.ReadUInt16();
            for (int i = 0; i < n; i++)
            {
                string type = r.ReadStr();
                int count = r.R.ReadInt32();
                items.TryGetValue(type, out int was);
                items[type] = was + count;
            }

            return items;
        }
    }
}
