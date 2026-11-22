using DarkwoodMP.World.Breaking;

namespace DarkwoodMP.Tests.World.Breaking
{
    public partial class BreakRulesTests
    {
        /// A client's own swing: no explosion, no flame, not the player's
        /// transform.
        static readonly HitFacts Client = new HitFacts { ClientMode = true };

        static readonly HitFacts OwnFlame = Client with
        {
            Flame = true,
            FlameOurs = true,
        };

        public class HostsHit
        {
            [Fact]
            public void Leaves_a_clients_own_hit_to_it()
            {
                var hosts = BreakRules.HostsHit(Client);

                hosts.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_a_hit_inside_an_explosion_to_the_host()
            {
                var hit = Client with { ThrownDepth = 1 };

                var hosts = BreakRules.HostsHit(hit);

                hosts.ShouldBeTrue();
            }

            [Fact]
            public void Leaves_another_players_flame_to_the_host()
            {
                var hit = OwnFlame with { FlameOurs = false };

                var hosts = BreakRules.HostsHit(hit);

                hosts.ShouldBeTrue();
            }

            [Fact]
            public void Leaves_its_own_flame_to_it()
            {
                var hosts = BreakRules.HostsHit(OwnFlame);

                hosts.ShouldBeFalse();
            }

            [Fact]
            public void Makes_every_hit_outside_a_client()
            {
                var hit = OwnFlame with
                {
                    ClientMode = false,
                    ThrownDepth = 1,
                    FlameOurs = false,
                };

                var hosts = BreakRules.HostsHit(hit);

                hosts.ShouldBeFalse();
            }
        }

        public class OwnFlameOf
        {
            [Fact]
            public void Takes_the_local_players_flame()
            {
                var own = BreakRules.OwnFlame(OwnFlame);

                own.ShouldBeTrue();
            }

            [Fact]
            public void Leaves_a_flame_inside_an_explosion()
            {
                var hit = OwnFlame with { ThrownDepth = 1 };

                var own = BreakRules.OwnFlame(hit);

                own.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_another_players_flame()
            {
                var hit = OwnFlame with { FlameOurs = false };

                var own = BreakRules.OwnFlame(hit);

                own.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_a_hit_with_no_flame()
            {
                var own = BreakRules.OwnFlame(Client);

                own.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_it_outside_a_client()
            {
                var hit = OwnFlame with { ClientMode = false };

                var own = BreakRules.OwnFlame(hit);

                own.ShouldBeFalse();
            }
        }

        public class OwnerOf
        {
            [Fact]
            public void Owns_every_change_outside_a_client()
            {
                var hit = Client with { ClientMode = false, ThrownDepth = 1 };

                var owner = BreakRules.OwnerOf(hit);

                owner.ShouldBe(HitOwner.Ours);
            }

            [Fact]
            public void Gives_a_change_inside_an_explosion_to_the_host()
            {
                var hit = OwnFlame with
                {
                    ThrownDepth = 1,
                    AttackerIsPlayer = true,
                };

                var owner = BreakRules.OwnerOf(hit);

                owner.ShouldBe(HitOwner.Others);
            }

            [Fact]
            public void Owns_its_own_flames_change()
            {
                var owner = BreakRules.OwnerOf(OwnFlame);

                owner.ShouldBe(HitOwner.Ours);
            }

            [Fact]
            public void Owns_a_hit_by_its_player()
            {
                var hit = Client with { AttackerIsPlayer = true };

                var owner = BreakRules.OwnerOf(hit);

                owner.ShouldBe(HitOwner.Ours);
            }

            [Fact]
            public void Asks_the_stack_about_anything_else()
            {
                var owner = BreakRules.OwnerOf(Client);

                owner.ShouldBe(HitOwner.AskStack);
            }
        }

        public class Counts
        {
            [Theory]
            [InlineData(true, true, true, true)]
            [InlineData(false, true, true, false)]
            [InlineData(true, false, true, false)]
            [InlineData(true, true, false, false)]
            public void Counts_the_outermost_call_while_sharing(
                bool outermost,
                bool sharing,
                bool exists,
                bool counts)
            {
                var counted = BreakRules.Counts(outermost, sharing, exists);

                counted.ShouldBe(counts);
            }
        }

        public class HitSend
        {
            static readonly ItemState Whole = new ItemState
            {
                Health = 5,
                Shadow = -1f,
            };

            /// Our player's hit took 2 off a barrel.
            static readonly ItemHitFacts Hit = new ItemHitFacts
            {
                Before = Whole,
                After = Whole with { Health = 3 },
                Ours = true,
                Destructible = true,
                Name = "barrel",
            };

            /// Our player's hit broke a larva egg.
            static readonly ItemHitFacts Egg = Hit with
            {
                After = Whole with { Health = 0, Destroyed = true },
                Name = "larva_egg_big",
            };

            [Fact]
            public void Sends_a_hit_that_changed_the_item()
            {
                var send = BreakRules.HitSend(Hit);

                send.ShouldBe(ItemSend.ItemHit);
            }

            [Fact]
            public void Sends_a_hit_that_changed_only_the_shadow()
            {
                var hit = Hit with { After = Whole with { Shadow = 0.5f } };

                var send = BreakRules.HitSend(hit);

                send.ShouldBe(ItemSend.ItemHit);
            }

            [Fact]
            public void Sends_nothing_for_a_hit_that_changed_nothing()
            {
                var hit = Hit with { After = Whole };

                var send = BreakRules.HitSend(hit);

                send.ShouldBe(ItemSend.Nothing);
            }

            [Fact]
            public void Sends_nothing_for_another_players_hit()
            {
                var hit = Hit with { Ours = false };

                var send = BreakRules.HitSend(hit);

                send.ShouldBe(ItemSend.Nothing);
            }

            [Fact]
            public void Sends_an_egg_the_hit_broke_as_an_egg()
            {
                var send = BreakRules.HitSend(Egg);

                send.ShouldBe(ItemSend.EggDestroyed);
            }

            [Fact]
            public void Sends_a_hit_on_an_egg_broken_already_as_a_hit()
            {
                var egg = Egg with { Before = Egg.After with { Health = 1 } };

                var send = BreakRules.HitSend(egg);

                send.ShouldBe(ItemSend.ItemHit);
            }

            [Fact]
            public void Sends_a_hit_that_broke_something_else_as_a_hit()
            {
                var hit = Egg with { Name = "barrel" };

                var send = BreakRules.HitSend(hit);

                send.ShouldBe(ItemSend.ItemHit);
            }
        }

        public class Broke
        {
            /// The outermost Item.die, sharing, that broke the item.
            static readonly ItemChangeFacts Died = new ItemChangeFacts
            {
                Outermost = true,
                Sharing = true,
                Exists = true,
                Destroyed = true,
            };

            [Fact]
            public void Sends_an_item_that_broke()
            {
                var broke = BreakRules.Broke(Died);

                broke.ShouldBeTrue();
            }

            [Fact]
            public void Leaves_an_item_broken_already()
            {
                var item = Died with { WasDestroyed = true };

                var broke = BreakRules.Broke(item);

                broke.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_an_item_that_didnt_break()
            {
                var item = Died with { Destroyed = false };

                var broke = BreakRules.Broke(item);

                broke.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_an_inner_call()
            {
                var item = Died with { Outermost = false };

                var broke = BreakRules.Broke(item);

                broke.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_it_while_not_sharing()
            {
                var item = Died with { Sharing = false };

                var broke = BreakRules.Broke(item);

                broke.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_an_item_that_isnt_there()
            {
                var item = Died with { Exists = false };

                var broke = BreakRules.Broke(item);

                broke.ShouldBeFalse();
            }
        }

        public class BrokenAs
        {
            [Theory]
            [InlineData(true, false, "larva_egg_big", ItemSend.EggDestroyed)]
            [InlineData(true, true, "cocoon", ItemSend.EggDestroyed)]
            [InlineData(true, false, "barrel", ItemSend.ItemBroken)]
            [InlineData(false, false, "larva_egg_big", ItemSend.ItemBroken)]
            public void Sends_an_egg_as_an_egg(
                bool destructible,
                bool opensByBreaking,
                string name,
                ItemSend send)
            {
                var sent = BreakRules.BrokenAs(
                    destructible,
                    opensByBreaking,
                    name);

                sent.ShouldBe(send);
            }
        }

        public class Repaired
        {
            /// The outermost Item.setNormalState, sharing, that mended it.
            static readonly ItemChangeFacts Mended = new ItemChangeFacts
            {
                Outermost = true,
                Sharing = true,
                Exists = true,
                WasDestroyed = true,
            };

            [Fact]
            public void Sends_an_item_that_was_mended()
            {
                var repaired = BreakRules.Repaired(Mended);

                repaired.ShouldBeTrue();
            }

            [Fact]
            public void Leaves_an_item_that_wasnt_broken()
            {
                var item = Mended with { WasDestroyed = false };

                var repaired = BreakRules.Repaired(item);

                repaired.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_an_item_still_broken()
            {
                var item = Mended with { Destroyed = true };

                var repaired = BreakRules.Repaired(item);

                repaired.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_an_inner_call()
            {
                var item = Mended with { Outermost = false };

                var repaired = BreakRules.Repaired(item);

                repaired.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_it_while_not_sharing()
            {
                var item = Mended with { Sharing = false };

                var repaired = BreakRules.Repaired(item);

                repaired.ShouldBeFalse();
            }
        }
    }
}
