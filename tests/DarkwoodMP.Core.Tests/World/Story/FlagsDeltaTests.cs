using System;
using System.Collections.Generic;
using DarkwoodMP.Net;
using DarkwoodMP.Tests.Net;
using DarkwoodMP.World.Story;

namespace DarkwoodMP.Tests.World.Story
{
    public class FlagsDeltaTests
    {
        static PacketWriter Written(
            List<KeyValuePair<string, string>> entries)
        {
            var w = new PacketWriter(new RecordingLog());
            w.Begin(MsgType.FlagsDelta);
            FlagsDelta.Write(w, entries);
            return w;
        }

        static byte[] Bytes(PacketWriter w)
        {
            var bytes = new byte[w.Length];
            Array.Copy(w.Buffer, bytes, w.Length);
            return bytes;
        }

        static KeyValuePair<string, string> Pair(string key, string value)
        {
            return new KeyValuePair<string, string>(key, value);
        }

        public class Write
        {
            [Fact]
            public void Writes_the_count_then_each_key_and_value()
            {
                var w = Written(new List<KeyValuePair<string, string>>
                {
                    Pair("k", "20"),
                    Pair("r:a", "+-5"),
                });

                Bytes(w).ShouldBe(new byte[]
                {
                    27,
                    2, 0,
                    1, 0, (byte)'k',
                    2, 0, (byte)'2', (byte)'0',
                    3, 0, (byte)'r', (byte)':', (byte)'a',
                    3, 0, (byte)'+', (byte)'-', (byte)'5',
                });
            }

            [Fact]
            public void Writes_no_entries_as_a_count_of_0()
            {
                var w = Written(new List<KeyValuePair<string, string>>());

                Bytes(w).ShouldBe(new byte[] { 27, 0, 0 });
            }

            [Fact]
            public void Writes_an_empty_value_as_length_0()
            {
                var w = Written(new List<KeyValuePair<string, string>>
                {
                    Pair("jl:x", ""),
                });

                Bytes(w).ShouldBe(new byte[]
                {
                    27,
                    1, 0,
                    4, 0, (byte)'j', (byte)'l', (byte)':', (byte)'x',
                    0, 0,
                });
            }
        }

        public class Read
        {
            [Fact]
            public void Reads_back_what_was_written_in_order()
            {
                var w = Written(new List<KeyValuePair<string, string>>
                {
                    Pair("f:door", "True|1"),
                    Pair("je:note", "-"),
                    Pair("jl:place", ""),
                });

                var r = new PacketReader(w.Buffer, w.Length);

                var got = FlagsDelta.Read(r);

                got.ShouldBe(new[]
                {
                    Pair("f:door", "True|1"),
                    Pair("je:note", "-"),
                    Pair("jl:place", ""),
                });
            }
        }
    }
}
