using DarkwoodMP.Net.Steam;

namespace DarkwoodMP.Tests.Net.Steam
{
    public class LobbyRulesTests
    {
        /// Steam lets us into a new lobby of a host on our protocol.
        static readonly EnterFacts Entry = new EnterFacts
        {
            Success = true,
            Lobby = 109775241,
            Proto = "29",
            Ours = 29,
        };

        /// A client still in the lobby, whom Steam made the owner.
        static readonly OwnerFacts Owner = new OwnerFacts
        {
            InLobby = true,
            Owner = 76561198,
            Me = 76561198,
        };

        public class Limits
        {
            [Fact]
            public void Pins_the_lobby_keys()
            {
                var keys = new[] { LobbyRules.KeyMod, LobbyRules.KeyProto };

                keys.ShouldBe(new[] { "darkwoodmp", "proto" });
            }

            [Fact]
            public void Pins_steams_entered_bit()
            {
                var bit = LobbyRules.EnteredFlag;

                bit.ShouldBe(1u);
            }

            [Fact]
            public void Pins_the_status_lines()
            {
                var lines = new[]
                {
                    LobbyRules.NotConnected,
                    LobbyRules.Creating,
                    LobbyRules.Joining,
                    LobbyRules.Hosting,
                    LobbyRules.HostLeft,
                };

                lines.ShouldBe(new[]
                {
                    "Not connected",
                    "Creating lobby...",
                    "Joining lobby...",
                    "Hosting",
                    "Host left the game",
                });
            }
        }

        public class MayHost
        {
            [Theory]
            [InlineData(false, true)]
            [InlineData(true, false)]
            public void Hosts_only_outside_a_lobby(bool inLobby, bool hosts)
            {
                var result = LobbyRules.MayHost(inLobby);

                result.ShouldBe(hosts);
            }
        }

        public class MayLoseHost
        {
            [Theory]
            [InlineData(true, false, true)]
            [InlineData(true, true, false)]
            [InlineData(false, false, false)]
            public void Loses_the_host_only_as_a_client_in_a_lobby(
                bool inLobby,
                bool host,
                bool loses)
            {
                var result = LobbyRules.MayLoseHost(inLobby, host);

                result.ShouldBe(loses);
            }
        }

        public class Enter
        {
            [Fact]
            public void Enters_a_lobby_on_our_protocol()
            {
                var step = LobbyRules.Enter(Entry);

                step.ShouldBe(EnterStep.Enter);
            }

            [Fact]
            public void Fails_on_steams_failure()
            {
                var entry = Entry with { IoFailure = true };

                var step = LobbyRules.Enter(entry);

                step.ShouldBe(EnterStep.Failed);
            }

            [Fact]
            public void Fails_on_a_refused_entry()
            {
                var entry = Entry with { Success = false };

                var step = LobbyRules.Enter(entry);

                step.ShouldBe(EnterStep.Failed);
            }

            [Fact]
            public void Ignores_the_second_telling()
            {
                var entry = Entry with { Current = 109775241 };

                var step = LobbyRules.Enter(entry);

                step.ShouldBe(EnterStep.Ignore);
            }

            [Fact]
            public void Ignores_the_lobby_just_refused()
            {
                var entry = Entry with { Refused = 109775241, Proto = "" };

                var step = LobbyRules.Enter(entry);

                step.ShouldBe(EnterStep.Ignore);
            }

            [Fact]
            public void Refuses_a_host_on_another_protocol()
            {
                var entry = Entry with { Proto = "28" };

                var step = LobbyRules.Enter(entry);

                step.ShouldBe(EnterStep.OtherVersion);
            }

            [Theory]
            [InlineData(null)]
            [InlineData("")]
            public void Passes_a_lobby_with_no_protocol(string proto)
            {
                var entry = Entry with { Proto = proto };

                var step = LobbyRules.Enter(entry);

                step.ShouldBe(EnterStep.Enter);
            }

            [Fact]
            public void Puts_a_failure_before_the_second_telling()
            {
                var entry = Entry with
                {
                    Success = false,
                    Current = 109775241,
                };

                var step = LobbyRules.Enter(entry);

                step.ShouldBe(EnterStep.Failed);
            }

            [Fact]
            public void Puts_the_second_telling_before_the_version()
            {
                var entry = Entry with { Current = 109775241, Proto = "28" };

                var step = LobbyRules.Enter(entry);

                step.ShouldBe(EnterStep.Ignore);
            }
        }

        public class OtherVersion
        {
            [Fact]
            public void Refuses_with_the_hosts_mod_version()
            {
                var verdict = LobbyRules.OtherVersion("0.28.0");

                verdict.ShouldBe(
                    Verdict.Refuse(Refusal.OtherModVersion, "0.28.0"));
            }
        }

        public class IsHost
        {
            [Theory]
            [InlineData(5ul, 5ul, true)]
            [InlineData(5ul, 6ul, false)]
            public void Hosts_a_lobby_we_owned_at_entry(
                ulong owner,
                ulong me,
                bool host)
            {
                var result = LobbyRules.IsHost(owner, me);

                result.ShouldBe(host);
            }
        }

        public class EnteredStatus
        {
            [Theory]
            [InlineData(true, "Ann", "Hosting")]
            [InlineData(false, "Ann", "Joined Ann's game")]
            public void Says_who_hosts(bool host, string owner, string status)
            {
                var text = LobbyRules.EnteredStatus(host, owner);

                text.ShouldBe(status);
            }
        }

        public class Change
        {
            [Theory]
            [InlineData(1u, LobbyChange.Joined)]
            [InlineData(3u, LobbyChange.Joined)]
            [InlineData(2u, LobbyChange.Left)]
            [InlineData(4u, LobbyChange.Left)]
            [InlineData(0u, LobbyChange.Left)]
            public void Counts_anything_but_entering_as_leaving(
                uint change,
                LobbyChange kind)
            {
                var result = LobbyRules.Change(7, 7, change);

                result.ShouldBe(kind);
            }

            [Fact]
            public void Ignores_another_lobbys_change()
            {
                var result = LobbyRules.Change(8, 7, 2u);

                result.ShouldBe(LobbyChange.Ignore);
            }
        }

        public class HostLeftUs
        {
            [Theory]
            [InlineData(false, 5ul, 5ul, true)]
            [InlineData(false, 6ul, 5ul, false)]
            [InlineData(true, 5ul, 5ul, false)]
            public void Ends_a_client_whose_host_left(
                bool host,
                ulong who,
                ulong ours,
                bool ends)
            {
                var result = LobbyRules.HostLeftUs(host, who, ours);

                result.ShouldBe(ends);
            }
        }

        public class MadeOwner
        {
            [Fact]
            public void Ends_a_client_steam_made_the_owner()
            {
                var made = LobbyRules.MadeOwner(Owner);

                made.ShouldBeTrue();
            }

            [Fact]
            public void Keeps_a_host_that_owns_the_lobby()
            {
                var lobby = Owner with { IAmHost = true };

                var made = LobbyRules.MadeOwner(lobby);

                made.ShouldBeFalse();
            }

            [Fact]
            public void Keeps_a_client_someone_else_owns_for()
            {
                var lobby = Owner with { Owner = 1 };

                var made = LobbyRules.MadeOwner(lobby);

                made.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_nothing_once_out_of_the_lobby()
            {
                var lobby = Owner with { InLobby = false };

                var made = LobbyRules.MadeOwner(lobby);

                made.ShouldBeFalse();
            }
        }

        public class LeftStatus
        {
            [Theory]
            [InlineData(false, "Hosting", "Not connected")]
            [InlineData(true, "Host left the game", "Host left the game")]
            public void Keeps_why_the_host_was_lost(
                bool lost,
                string status,
                string after)
            {
                var text = LobbyRules.LeftStatus(lost, status);

                text.ShouldBe(after);
            }
        }

        public class LaunchLobbies
        {
            [Fact]
            public void Joins_each_lobby_given_in_order()
            {
                var args = new[]
                {
                    "+connect_lobby",
                    "11",
                    "+connect_lobby",
                    "x",
                    "+connect_lobby",
                    "12",
                };

                var lobbies = LobbyRules.LaunchLobbies(args);

                lobbies.ShouldBe(new[] { 11ul, 12ul });
            }

            [Fact]
            public void Joins_nothing_for_the_argument_given_last()
            {
                var args = new[] { "Darkwood", "+connect_lobby" };

                var lobbies = LobbyRules.LaunchLobbies(args);

                lobbies.ShouldBeEmpty();
            }
        }
    }
}
