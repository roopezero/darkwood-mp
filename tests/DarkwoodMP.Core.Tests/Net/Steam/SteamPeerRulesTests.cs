using DarkwoodMP.Net.Steam;

namespace DarkwoodMP.Tests.Net.Steam
{
    public class SteamPeerRulesTests
    {
        /// A host asked by a lobby member.
        static readonly AcceptFacts Member = new AcceptFacts
        {
            InLobby = true,
            IsHost = true,
            Member = true,
        };

        /// A client asked by its host.
        static readonly AcceptFacts Host = new AcceptFacts
        {
            InLobby = true,
            FromHost = true,
        };

        /// A host whose peer that got in failed.
        static readonly LostFacts PeerLost = new LostFacts
        {
            InLobby = true,
            HasSession = true,
            IsHost = true,
            KnownPeer = true,
        };

        /// A client let in, whose host failed.
        static readonly LostFacts HostLost = new LostFacts
        {
            InLobby = true,
            HasSession = true,
            FromHost = true,
            Connected = true,
        };

        public class Limits
        {
            [Fact]
            public void Pins_why_a_client_leaves()
            {
                var why = SteamPeerRules.LostHost;

                why.ShouldBe("Lost the connection to the host");
            }
        }

        public class MayAccept
        {
            [Fact]
            public void Allows_a_member_to_a_host()
            {
                var verdict = SteamPeerRules.MayAccept(Member);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Refuses_a_stranger_to_a_host()
            {
                var peer = Member with { Member = false, FromHost = true };

                var verdict = SteamPeerRules.MayAccept(peer);

                verdict.Reason.ShouldBe(Refusal.NotInLobby);
            }

            [Fact]
            public void Allows_the_host_to_a_client()
            {
                var verdict = SteamPeerRules.MayAccept(Host);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Refuses_another_member_to_a_client()
            {
                var peer = Host with { FromHost = false, Member = true };

                var verdict = SteamPeerRules.MayAccept(peer);

                verdict.Reason.ShouldBe(Refusal.NotInLobby);
            }

            [Fact]
            public void Refuses_anyone_outside_a_lobby()
            {
                var peer = Member with { InLobby = false, FromHost = true };

                var verdict = SteamPeerRules.MayAccept(peer);

                verdict.Reason.ShouldBe(Refusal.NotInLobby);
            }
        }

        public class Lost
        {
            [Fact]
            public void Drops_a_peer_that_got_in()
            {
                var step = SteamPeerRules.Lost(PeerLost);

                step.ShouldBe(LostStep.DropPeer);
            }

            [Fact]
            public void Ignores_a_peer_that_never_got_in()
            {
                var lost = PeerLost with { KnownPeer = false };

                var step = SteamPeerRules.Lost(lost);

                step.ShouldBe(LostStep.Ignore);
            }

            [Fact]
            public void Loses_the_host_of_a_client_let_in()
            {
                var step = SteamPeerRules.Lost(HostLost);

                step.ShouldBe(LostStep.LoseHost);
            }

            [Fact]
            public void Ignores_the_host_before_it_let_us_in()
            {
                var lost = HostLost with { Connected = false };

                var step = SteamPeerRules.Lost(lost);

                step.ShouldBe(LostStep.Ignore);
            }

            [Fact]
            public void Ignores_another_peer_of_a_client()
            {
                var lost = HostLost with { FromHost = false, KnownPeer = true };

                var step = SteamPeerRules.Lost(lost);

                step.ShouldBe(LostStep.Ignore);
            }

            [Fact]
            public void Ignores_any_peer_outside_a_lobby()
            {
                var lost = PeerLost with { InLobby = false };

                var step = SteamPeerRules.Lost(lost);

                step.ShouldBe(LostStep.Ignore);
            }

            [Fact]
            public void Ignores_any_peer_without_a_session()
            {
                var lost = HostLost with { HasSession = false };

                var step = SteamPeerRules.Lost(lost);

                step.ShouldBe(LostStep.Ignore);
            }
        }

        public class ActsOnLeft
        {
            [Theory]
            [InlineData(true, false, true)]
            [InlineData(true, true, true)]
            [InlineData(false, true, true)]
            [InlineData(false, false, false)]
            public void Acts_for_a_host_or_on_the_host_leaving(
                bool host,
                bool fromHost,
                bool acts)
            {
                var result = SteamPeerRules.ActsOnLeft(host, fromHost);

                result.ShouldBe(acts);
            }
        }

        public class MayRejoin
        {
            [Theory]
            [InlineData(false, true, true)]
            [InlineData(true, true, false)]
            [InlineData(false, false, false)]
            public void Rejoins_only_a_client_in_a_lobby(
                bool host,
                bool inLobby,
                bool rejoins)
            {
                var result = SteamPeerRules.MayRejoin(host, inLobby);

                result.ShouldBe(rejoins);
            }
        }
    }
}
