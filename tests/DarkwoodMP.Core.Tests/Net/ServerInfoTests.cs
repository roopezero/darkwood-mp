using System.Text;
using DarkwoodMP.Net;

namespace DarkwoodMP.Tests.Net
{
    /// The server browser's UDP info query, in goasis's format: fields
    /// ended by byte 0, the datagram ended by byte 237.
    public class ServerInfoTests
    {
        /// The text with "|" for byte 0 and "#" for byte 237.
        private static byte[] Datagram(string text)
        {
            var b = Encoding.UTF8.GetBytes(text);
            for (int i = 0; i < b.Length; i++)
            {
                if (b[i] == (byte)'|')
                {
                    b[i] = ServerInfo.Sep;
                }
                else if (b[i] == (byte)'#')
                {
                    b[i] = ServerInfo.End;
                }
            }

            return b;
        }

        private static ServerInfo Darkwood() => new ServerInfo
        {
            Name = "Night shift",
            Players = 2,
            MaxPlayers = 5,
            Password = true,
            LocalChars = false,
            Version = "0.29.0",
        };

        public class Query
        {
            [Fact]
            public void Is_info_ended_by_sep_and_end()
            {
                ServerInfo.Query.ShouldBe(Datagram("info|#"));
            }
        }

        public class IsQuery
        {
            [Fact]
            public void Knows_the_query()
            {
                var q = Datagram("info|#");

                ServerInfo.IsQuery(q, q.Length).ShouldBeTrue();
            }

            [Fact]
            public void Turns_down_anything_else()
            {
                var q = Datagram("INFO|#");

                ServerInfo.IsQuery(q, q.Length).ShouldBeFalse();
            }

            [Fact]
            public void Turns_down_the_query_with_more_after_it()
            {
                var q = Datagram("info|#x");

                ServerInfo.IsQuery(q, q.Length).ShouldBeFalse();
            }
        }

        public class EncodeAnswer
        {
            [Fact]
            public void Writes_the_fields_in_goasis_order()
            {
                var info = Darkwood();

                var a = info.EncodeAnswer();

                a.ShouldBe(Datagram("INFO|Night shift|2|5|1|0|0.29.0|#"));
            }

            [Fact]
            public void Adds_the_extra_fields_sorted_by_key()
            {
                var info = Darkwood();
                info.Fields["world"] = "Day 3";
                info.Fields["game"] = "darkwood";
                info.Fields["proto"] = "29";

                var a = info.EncodeAnswer();

                a.ShouldBe(Datagram(
                    "INFO|Night shift|2|5|1|0|0.29.0|game=darkwood|proto=29|"
                        + "world=Day 3|#"));
            }

            [Fact]
            public void Drops_sep_bytes_from_the_name()
            {
                var info = Darkwood();
                info.Name = "Night\0shift";

                var a = info.EncodeAnswer();

                a.ShouldBe(Datagram("INFO|Nightshift|2|5|1|0|0.29.0|#"));
            }

            [Fact]
            public void Drops_an_equals_sign_from_a_key()
            {
                var info = Darkwood();
                info.Fields["a=b"] = "c";

                var a = info.EncodeAnswer();

                a.ShouldBe(Datagram("INFO|Night shift|2|5|1|0|0.29.0|ab=c|#"));
            }

            [Fact]
            public void Leaves_out_a_field_with_no_key()
            {
                var info = Darkwood();
                info.Fields["="] = "c";

                var a = info.EncodeAnswer();

                a.ShouldBe(Datagram("INFO|Night shift|2|5|1|0|0.29.0|#"));
            }
        }

        public class Parse
        {
            [Fact]
            public void Reads_what_EncodeAnswer_wrote()
            {
                var info = Darkwood();
                info.Fields["proto"] = "29";
                var a = info.EncodeAnswer();

                var back = ServerInfo.Parse(a, a.Length);

                back.ShouldBeEquivalentTo(info);
            }

            [Fact]
            public void Refuses_a_datagram_without_the_end_byte()
            {
                var a = Datagram("INFO|Night shift|2|5|1|0|0.29.0|");

                ServerInfo.Parse(a, a.Length).ShouldBeNull();
            }

            [Fact]
            public void Refuses_a_datagram_with_too_few_fields()
            {
                var a = Datagram("INFO|Night shift|2|5|1|#");

                ServerInfo.Parse(a, a.Length).ShouldBeNull();
            }

            [Fact]
            public void Refuses_a_datagram_that_isnt_an_answer()
            {
                var a = Datagram("info|Night shift|2|5|1|0|0.29.0|#");

                ServerInfo.Parse(a, a.Length).ShouldBeNull();
            }

            [Fact]
            public void Refuses_a_player_count_that_isnt_a_number()
            {
                var a = Datagram("INFO|Night shift|two|5|1|0|0.29.0|#");

                ServerInfo.Parse(a, a.Length).ShouldBeNull();
            }
        }

        public class Incompatibility
        {
            [Fact]
            public void Names_a_server_of_another_game()
            {
                var info = Darkwood();
                info.Fields["game"] = "other";

                info.Incompatibility.ShouldBe("not a Darkwood server");
            }

            [Fact]
            public void Names_another_protocol_by_the_servers_version()
            {
                var info = Darkwood();
                info.Fields["game"] = Protocol.Game;
                info.Fields["proto"] = "1";

                info.Incompatibility.ShouldBe(
                    "different DarkwoodMP version (0.29.0)");
            }

            [Fact]
            public void Has_nothing_against_the_same_protocol()
            {
                var info = Darkwood();
                info.Fields["game"] = Protocol.Game;
                info.Fields["proto"] = Protocol.Version.ToString();

                info.Incompatibility.ShouldBeNull();
            }
        }
    }
}
