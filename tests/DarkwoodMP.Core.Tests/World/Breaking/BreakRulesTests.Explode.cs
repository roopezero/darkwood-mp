using DarkwoodMP.World.Breaking;

namespace DarkwoodMP.Tests.World.Breaking
{
    public partial class BreakRulesTests
    {
        public class Tracks
        {
            /// A barrel about to go off.
            static readonly ExplodeFacts Barrel = new ExplodeFacts
            {
                Exists = true,
            };

            [Fact]
            public void Tracks_a_barrel()
            {
                var tracked = BreakRules.Tracks(Barrel);

                tracked.ShouldBeTrue();
            }

            [Fact]
            public void Leaves_one_gone_off_already()
            {
                var bomb = Barrel with { Activated = true };

                var tracked = BreakRules.Tracks(bomb);

                tracked.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_a_copy_of_another_games_item()
            {
                var bomb = Barrel with { Proxy = true };

                var tracked = BreakRules.Tracks(bomb);

                tracked.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_a_thrown_one_to_the_throws()
            {
                var bomb = Barrel with { Thrown = true };

                var tracked = BreakRules.Tracks(bomb);

                tracked.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_a_kamikaze()
            {
                var bomb = Barrel with { Character = true };

                var tracked = BreakRules.Tracks(bomb);

                tracked.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_what_an_enemy_threw()
            {
                var bomb = Barrel with { ByEnemy = true };

                var tracked = BreakRules.Tracks(bomb);

                tracked.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_one_that_isnt_there()
            {
                var bomb = Barrel with { Exists = false };

                var tracked = BreakRules.Tracks(bomb);

                tracked.ShouldBeFalse();
            }
        }

        public class Enter
        {
            static readonly Guards Inside = new Guards
            {
                Depth = 2,
                Replaying = true,
            };

            [Fact]
            public void Makes_the_hosts_hits_outermost_and_not_replayed()
            {
                var guards = BreakRules.Enter(true, Inside);

                guards.ShouldBe(new Guards());
            }

            [Fact]
            public void Goes_one_call_deeper_elsewhere()
            {
                var guards = BreakRules.Enter(false, Inside);

                guards.ShouldBe(new Guards { Depth = 3, Replaying = true });
            }
        }

        public class Leave
        {
            static readonly Guards Saved = new Guards
            {
                Depth = 2,
                Replaying = true,
            };

            static readonly Guards Now = new Guards { Depth = 4 };

            [Fact]
            public void Puts_back_the_hosts_guards_one_deeper()
            {
                var guards = BreakRules.Leave(true, Saved, Now);

                guards.ShouldBe(new Guards { Depth = 3, Replaying = true });
            }

            [Fact]
            public void Keeps_the_guards_elsewhere()
            {
                var guards = BreakRules.Leave(false, Saved, Now);

                guards.ShouldBe(Now);
            }
        }

        public class ExplodeOwner
        {
            /// The host's barrel went off, outermost, while sharing.
            static readonly ExplodeSendFacts Off = new ExplodeSendFacts
            {
                Outermost = true,
                Sharing = true,
                Activated = true,
            };

            [Fact]
            public void Sends_the_hosts_explosion()
            {
                var owner = BreakRules.ExplodeOwner(Off);

                owner.ShouldBe(HitOwner.Ours);
            }

            [Fact]
            public void Asks_the_stack_on_a_client()
            {
                var bomb = Off with { ClientMode = true };

                var owner = BreakRules.ExplodeOwner(bomb);

                owner.ShouldBe(HitOwner.AskStack);
            }

            [Fact]
            public void Sends_nothing_for_one_that_didnt_go_off()
            {
                var bomb = Off with { Activated = false };

                var owner = BreakRules.ExplodeOwner(bomb);

                owner.ShouldBe(HitOwner.Others);
            }

            [Fact]
            public void Sends_nothing_from_an_inner_call()
            {
                var bomb = Off with { Outermost = false };

                var owner = BreakRules.ExplodeOwner(bomb);

                owner.ShouldBe(HitOwner.Others);
            }

            [Fact]
            public void Sends_nothing_while_not_sharing()
            {
                var bomb = Off with { Sharing = false, ClientMode = true };

                var owner = BreakRules.ExplodeOwner(bomb);

                owner.ShouldBe(HitOwner.Others);
            }
        }
    }
}
