using DarkwoodMP.World.Throws;

namespace DarkwoodMP.Tests.World.Throws
{
    /// What a game does with an ItemThrown.
    public partial class ThrowRulesTests
    {
        public class Receive
        {
            /// On the host (id 1): a throw from player 2, whose stand-in is
            /// at the origin, exactly at the reach, naming player 3.
            static readonly ThrownFacts AtHost = new ThrownFacts
            {
                InWorld = true,
                IsHost = true,
                From = 2,
                HostId = 1,
                LocalId = 1,
                ReportedBy = 3,
                Known = true,
                Pos = new Vec2(400f, 0f),
            };

            /// On client 2: the host's relay of player 3's throw.
            static readonly ThrownFacts AtClient = new ThrownFacts
            {
                InWorld = true,
                IsClient = true,
                From = 1,
                HostId = 1,
                LocalId = 2,
                ReportedBy = 3,
            };

            [Fact]
            public void Relays_a_throw_on_the_host()
            {
                var receipt = ThrowRules.Receive(AtHost);

                receipt.Relays.ShouldBeTrue();
            }

            [Fact]
            public void Copies_a_throw_on_the_host()
            {
                var receipt = ThrowRules.Receive(AtHost);

                receipt.Copies.ShouldBeTrue();
            }

            [Fact]
            public void Names_the_sender_as_the_thrower_on_the_host()
            {
                var receipt = ThrowRules.Receive(AtHost);

                receipt.By.ShouldBe(2UL);
            }

            [Fact]
            public void Refuses_a_throw_just_past_the_reach()
            {
                var thrown = AtHost with { Pos = new Vec2(400.5f, 0f) };

                var receipt = ThrowRules.Receive(thrown);

                receipt.ShouldBe(new ThrowReceipt { Refused = true });
            }

            [Fact]
            public void Allows_any_reach_from_one_with_no_stand_in()
            {
                var thrown = AtHost with
                {
                    Known = false,
                    Pos = new Vec2(9000f, 0f),
                };

                var receipt = ThrowRules.Receive(thrown);

                receipt.Refused.ShouldBeFalse();
            }

            [Fact]
            public void Copies_the_hosts_relay_on_a_client()
            {
                var receipt = ThrowRules.Receive(AtClient);

                receipt.ShouldBe(new ThrowReceipt { Copies = true, By = 3 });
            }

            [Fact]
            public void Measures_no_reach_on_a_client()
            {
                var thrown = AtClient with
                {
                    Known = true,
                    Pos = new Vec2(9000f, 0f),
                };

                var receipt = ThrowRules.Receive(thrown);

                receipt.Copies.ShouldBeTrue();
            }

            [Fact]
            public void Makes_no_copy_of_our_own_throw()
            {
                var thrown = AtClient with { ReportedBy = 2 };

                var receipt = ThrowRules.Receive(thrown);

                receipt.Copies.ShouldBeFalse();
            }

            [Fact]
            public void Ignores_a_client_throw_not_from_the_host()
            {
                var thrown = AtClient with { From = 4 };

                var receipt = ThrowRules.Receive(thrown);

                receipt.ShouldBe(default(ThrowReceipt));
            }

            [Fact]
            public void Ignores_a_throw_outside_a_world()
            {
                var thrown = AtHost with { InWorld = false };

                var receipt = ThrowRules.Receive(thrown);

                receipt.ShouldBe(default(ThrowReceipt));
            }

            [Fact]
            public void Puts_the_world_before_the_reach()
            {
                var thrown = AtHost with
                {
                    InWorld = false,
                    Pos = new Vec2(9000f, 0f),
                };

                var receipt = ThrowRules.Receive(thrown);

                receipt.Refused.ShouldBeFalse();
            }
        }
    }
}
