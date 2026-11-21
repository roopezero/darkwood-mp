using DarkwoodMP.World.Inventories;

namespace DarkwoodMP.Tests.World.Inventories
{
    public class ContainerRulesTests
    {
        /// A chest the host has open, not ours alone and not a storage.
        static readonly OpenContainerFacts Chest = new OpenContainerFacts
        {
            Kind = ContainerKind.Item,
        };

        /// A client with a stand-in exactly at the reach of an open chest.
        static readonly ContainerFacts AtReach = new ContainerFacts
        {
            Known = true,
            StandIn = new Vec2(0f, 0f),
            At = new Vec2(600f, 0f),
        };

        public class Limits
        {
            [Fact]
            public void Pins_how_often_open_containers_are_looked_at()
            {
                var every = ContainerRules.PollEvery;

                every.ShouldBe(0.25f);
            }
        }

        public class IsWorldContainer
        {
            [Theory]
            [InlineData(ContainerKind.Item)]
            [InlineData(ContainerKind.DeathDrop)]
            [InlineData(ContainerKind.Shop)]
            public void Shares_item_death_drop_and_shop_inventories(
                ContainerKind kind)
            {
                var shared = ContainerRules.IsWorldContainer(kind, false);

                shared.ShouldBeTrue();
            }

            [Fact]
            public void Leaves_out_other_inventories()
            {
                var shared = ContainerRules.IsWorldContainer(
                    ContainerKind.Other,
                    false);

                shared.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_out_a_workbenchs_own_inventory()
            {
                var shared = ContainerRules.IsWorldContainer(
                    ContainerKind.Item,
                    true);

                shared.ShouldBeFalse();
            }
        }

        public class PathOf
        {
            [Fact]
            public void Shares_the_hosts_chest_whole()
            {
                var path = ContainerRules.PathOf(Chest);

                path.ShouldBe(ContainerPath.Whole);
            }

            [Fact]
            public void Shares_a_clients_chest_slot_by_slot_with_the_lock()
            {
                var chest = Chest with { IsClient = true };

                var path = ContainerRules.PathOf(chest);

                path.ShouldBe(ContainerPath.LockedSlots);
            }

            [Theory]
            [InlineData(false)]
            [InlineData(true)]
            public void Shares_a_storage_slot_by_slot_in_every_game(
                bool client)
            {
                var storage = Chest with { Storage = true, IsClient = client };

                var path = ContainerRules.PathOf(storage);

                path.ShouldBe(ContainerPath.Slots);
            }

            [Fact]
            public void Ignores_a_container_ours_alone()
            {
                var dream = Chest with { OursAlone = true, Storage = true };

                var path = ContainerRules.PathOf(dream);

                path.ShouldBe(ContainerPath.Ignore);
            }

            [Fact]
            public void Ignores_a_workbenchs_own_inventory()
            {
                var bench = Chest with { Workbench = true, Storage = true };

                var path = ContainerRules.PathOf(bench);

                path.ShouldBe(ContainerPath.Ignore);
            }

            [Fact]
            public void Ignores_another_kind_of_inventory()
            {
                var other = Chest with { Kind = ContainerKind.Other };

                var path = ContainerRules.PathOf(other);

                path.ShouldBe(ContainerPath.Ignore);
            }
        }

        public class LockedReason
        {
            [Fact]
            public void Allows_an_open_container()
            {
                var verdict = ContainerRules.LockedReason(new LockFacts());

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Refuses_a_crate_not_picked_open()
            {
                var crate = new LockFacts { Crate = true };

                var verdict = ContainerRules.LockedReason(crate);

                verdict.Reason.ShouldBe(Refusal.Locked);
            }

            [Fact]
            public void Allows_a_crate_picked_open()
            {
                var crate = new LockFacts { Crate = true, Searched = true };

                var verdict = ContainerRules.LockedReason(crate);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Refuses_a_locked_lock()
            {
                var shut = new LockFacts { Locked = true, Searched = true };

                var verdict = ContainerRules.LockedReason(shut);

                verdict.Reason.ShouldBe(Refusal.Locked);
            }

            [Fact]
            public void Refuses_one_not_broken_open()
            {
                var egg = new LockFacts { DestroyToAccess = true };

                var verdict = ContainerRules.LockedReason(egg);

                verdict.Reason.ShouldBe(Refusal.NotBrokenOpen);
            }

            [Fact]
            public void Allows_one_broken_open()
            {
                var egg = new LockFacts
                {
                    DestroyToAccess = true,
                    Destroyed = true,
                };

                var verdict = ContainerRules.LockedReason(egg);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Says_locked_before_not_broken_open()
            {
                var both = new LockFacts
                {
                    Locked = true,
                    DestroyToAccess = true,
                };

                var verdict = ContainerRules.LockedReason(both);

                verdict.Reason.ShouldBe(Refusal.Locked);
            }
        }

        public class MayChange
        {
            [Fact]
            public void Allows_a_container_exactly_at_the_reach()
            {
                var verdict = ContainerRules.MayChange(AtReach);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Refuses_a_container_just_past_the_reach()
            {
                var far = AtReach with { At = new Vec2(600.1f, 0f) };

                var verdict = ContainerRules.MayChange(far);

                verdict.Reason.ShouldBe(Refusal.TooFar);
            }

            [Fact]
            public void Measures_on_the_ground_plane()
            {
                var diagonal = AtReach with { At = new Vec2(360f, 480f) };

                var verdict = ContainerRules.MayChange(diagonal);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Skips_the_distance_of_a_client_with_no_stand_in()
            {
                var unknown = AtReach with
                {
                    Known = false,
                    At = new Vec2(5000f, 0f),
                };

                var verdict = ContainerRules.MayChange(unknown);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Refuses_a_locked_container_in_reach()
            {
                var locked = AtReach with
                {
                    Lock = new LockFacts { Locked = true },
                };

                var verdict = ContainerRules.MayChange(locked);

                verdict.Reason.ShouldBe(Refusal.Locked);
            }

            [Fact]
            public void Says_too_far_before_locked()
            {
                var both = AtReach with
                {
                    At = new Vec2(700f, 0f),
                    Lock = new LockFacts { Locked = true },
                };

                var verdict = ContainerRules.MayChange(both);

                verdict.Reason.ShouldBe(Refusal.TooFar);
            }
        }

        public class Accepts
        {
            [Theory]
            [InlineData(true, false, false, true)]
            [InlineData(true, true, true, true)]
            [InlineData(true, true, false, false)]
            [InlineData(false, false, true, false)]
            [InlineData(false, true, true, false)]
            public void Takes_world_messages_in_a_world_from_whom_it_should(
                bool inWorld,
                bool client,
                bool fromHost,
                bool expected)
            {
                var takes = ContainerRules.Accepts(inWorld, client, fromHost);

                takes.ShouldBe(expected);
            }
        }

        public class Received
        {
            static readonly Verdict TooFar = Verdict.Refuse(Refusal.TooFar);

            [Fact]
            public void Sends_the_host_contents_back_when_refused()
            {
                var answer = ContainerRules.Received(true, true, TooFar);

                answer.Verdict.Reason.ShouldBe(Refusal.TooFar);
            }

            [Fact]
            public void Neither_applies_nor_relays_a_refused_change()
            {
                var answer = ContainerRules.Received(true, true, TooFar);

                (answer.Apply || answer.Relay).ShouldBeFalse();
            }

            [Fact]
            public void Applies_and_relays_an_allowed_change_on_the_host()
            {
                var answer = ContainerRules.Received(true, true, default);

                (answer.Apply && answer.Relay).ShouldBeTrue();
            }

            [Fact]
            public void Relays_unchecked_what_the_host_hasnt_got()
            {
                var answer = ContainerRules.Received(true, false, TooFar);

                answer.ShouldBe(new ContentsAnswer { Relay = true });
            }

            [Fact]
            public void Applies_the_hosts_contents_on_a_client()
            {
                var answer = ContainerRules.Received(false, true, TooFar);

                answer.ShouldBe(new ContentsAnswer { Apply = true });
            }
        }

        public class LoadPathOf
        {
            [Fact]
            public void Loads_into_an_open_window_of_the_same_size()
            {
                var path = ContainerRules.LoadPathOf(true, true, true);

                path.ShouldBe(LoadPath.InWindow);
            }

            [Fact]
            public void Closes_an_open_window_of_another_size_first()
            {
                var path = ContainerRules.LoadPathOf(true, false, true);

                path.ShouldBe(LoadPath.CloseFirst);
            }

            [Fact]
            public void Closes_an_open_window_first_when_windows_dont_redraw()
            {
                var path = ContainerRules.LoadPathOf(true, true, false);

                path.ShouldBe(LoadPath.CloseFirst);
            }

            [Fact]
            public void Loads_a_closed_container()
            {
                var path = ContainerRules.LoadPathOf(false, true, true);

                path.ShouldBe(LoadPath.Load);
            }
        }

        public class MergesInWindow
        {
            [Theory]
            [InlineData(true, true, true)]
            [InlineData(true, false, false)]
            [InlineData(false, true, false)]
            public void Merges_only_on_a_client_that_watches_it(
                bool client,
                bool watching,
                bool expected)
            {
                var merges = ContainerRules.MergesInWindow(client, watching);

                merges.ShouldBe(expected);
            }
        }

        public class RemovesAfterLoad
        {
            [Theory]
            [InlineData(true, 0, true)]
            [InlineData(true, 1, false)]
            [InlineData(false, 0, false)]
            public void Removes_an_emptied_one_that_goes_when_empty(
                bool removeWhenEmpty,
                int items,
                bool expected)
            {
                var removes = ContainerRules.RemovesAfterLoad(
                    removeWhenEmpty,
                    items);

                removes.ShouldBe(expected);
            }
        }
    }
}
