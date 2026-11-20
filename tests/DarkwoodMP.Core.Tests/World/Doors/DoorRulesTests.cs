using DarkwoodMP.World;
using DarkwoodMP.World.Doors;

namespace DarkwoodMP.Tests.World.Doors
{
    public partial class DoorRulesTests
    {
        /// A request being handled, from a player standing at the origin.
        static readonly ReachFacts Requester = new ReachFacts
        {
            Handling = true,
            Known = true,
            StandIn = new Vec2(0f, 0f),
            Target = new Vec2(100f, 0f),
            What = "Wooden door",
        };

        /// A shut, whole, unlocked door near the requester.
        static readonly DoorFacts Usable = new DoorFacts
        {
            Exists = true,
            Requester = Requester,
            KeyType = "Bunker",
        };

        static readonly DoorFacts Missing = Usable with { Exists = false };

        /// The usable door, x away from the requester.
        static DoorFacts At(float x)
        {
            var far = Requester with { Target = new Vec2(x, 0f) };
            return Usable with { Requester = far };
        }

        static readonly DoorFacts Locked = Usable with
        {
            HasLock = true,
            Locked = true,
        };

        public class Limits
        {
            [Fact]
            public void Pins_the_reaches()
            {
                var reaches = new[] { DoorRules.Reach, DoorRules.ShotReach };

                reaches.ShouldBe(new[] { 600f, 1200f });
            }

            [Fact]
            public void Pins_the_largest_hit()
            {
                DoorRules.MaxHit.ShouldBe(1000f);
            }

            [Fact]
            public void Pins_the_ways_to_unlock()
            {
                var ways = new[] { DoorRules.ByStory, DoorRules.WithKey };

                ways.ShouldBe(new[] { 0f, 1f });
            }
        }

        public class MayOpen
        {
            [Fact]
            public void Allows_a_usable_door()
            {
                var verdict = DoorRules.MayOpen(Usable);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Refuses_a_door_that_isnt_there()
            {
                var verdict = DoorRules.MayOpen(Missing);

                verdict.Reason.ShouldBe(Refusal.NoSuchDoor);
            }

            [Fact]
            public void Refuses_a_missing_door_from_a_player_with_no_stand_in()
            {
                var nobody = Requester with { Known = false };
                var door = Missing with { Requester = nobody };

                var verdict = DoorRules.MayOpen(door);

                verdict.Reason.ShouldBe(Refusal.NoSuchDoor);
            }

            [Fact]
            public void Allows_a_door_exactly_at_the_reach()
            {
                var verdict = DoorRules.MayOpen(At(600f));

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Refuses_a_door_just_past_the_reach()
            {
                var verdict = DoorRules.MayOpen(At(600.5f));

                verdict.Reason.ShouldBe(Refusal.OutOfReach);
            }

            [Fact]
            public void Words_the_reach_with_the_doors_name()
            {
                var verdict = DoorRules.MayOpen(At(4041f));

                var text = Refusals.Text(verdict);

                text.ShouldBe("Wooden door is 4041 away from them (reach 600)");
            }

            [Fact]
            public void Refuses_a_broken_door()
            {
                var door = Usable with { Destroyed = true };

                var verdict = DoorRules.MayOpen(door);

                verdict.Reason.ShouldBe(Refusal.DoorBroken);
            }

            [Fact]
            public void Refuses_a_locked_door()
            {
                var verdict = DoorRules.MayOpen(Locked);

                verdict.Reason.ShouldBe(Refusal.DoorLocked);
            }

            [Fact]
            public void Allows_a_locked_flag_without_a_lock()
            {
                var door = Usable with { Locked = true };

                var verdict = DoorRules.MayOpen(door);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Refuses_a_blocked_door()
            {
                var door = Usable with { Blocked = true };

                var verdict = DoorRules.MayOpen(door);

                verdict.Reason.ShouldBe(Refusal.DoorBlocked);
            }

            [Fact]
            public void Prefers_a_missing_door_to_the_reach()
            {
                var door = At(4041f) with { Exists = false };

                var verdict = DoorRules.MayOpen(door);

                verdict.Reason.ShouldBe(Refusal.NoSuchDoor);
            }

            [Fact]
            public void Prefers_the_reach_to_a_broken_door()
            {
                var door = At(4041f) with { Destroyed = true };

                var verdict = DoorRules.MayOpen(door);

                verdict.Reason.ShouldBe(Refusal.OutOfReach);
            }

            [Fact]
            public void Prefers_a_broken_door_to_a_locked_one()
            {
                var door = Locked with { Destroyed = true };

                var verdict = DoorRules.MayOpen(door);

                verdict.Reason.ShouldBe(Refusal.DoorBroken);
            }

            [Fact]
            public void Prefers_a_locked_door_to_a_blocked_one()
            {
                var door = Locked with { Blocked = true };

                var verdict = DoorRules.MayOpen(door);

                verdict.Reason.ShouldBe(Refusal.DoorLocked);
            }
        }

        public class MayClose
        {
            [Fact]
            public void Allows_a_broken_locked_blocked_door()
            {
                var door = Locked with { Destroyed = true, Blocked = true };

                var verdict = DoorRules.MayClose(door);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Refuses_a_door_that_isnt_there()
            {
                var verdict = DoorRules.MayClose(Missing);

                verdict.Reason.ShouldBe(Refusal.NoSuchDoor);
            }

            [Fact]
            public void Refuses_a_door_just_past_the_reach()
            {
                var verdict = DoorRules.MayClose(At(600.5f));

                verdict.Reason.ShouldBe(Refusal.OutOfReach);
            }
        }

        public class MayBarricade
        {
            [Fact]
            public void Allows_a_door_with_no_barricade()
            {
                var verdict = DoorRules.MayBarricade(Usable);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Refuses_a_barricaded_door()
            {
                var door = Usable with { Barricaded = true };

                var verdict = DoorRules.MayBarricade(door);

                verdict.Reason.ShouldBe(Refusal.DoorBarricaded);
            }

            [Fact]
            public void Allows_a_barricaded_broken_door_as_a_repair()
            {
                var door = Usable with { Barricaded = true, Destroyed = true };

                var verdict = DoorRules.MayBarricade(door);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Prefers_the_reach_to_a_barricade()
            {
                var door = At(600.5f) with { Barricaded = true };

                var verdict = DoorRules.MayBarricade(door);

                verdict.Reason.ShouldBe(Refusal.OutOfReach);
            }
        }

        public class MayDestroyBarricade
        {
            [Fact]
            public void Allows_a_barricaded_door()
            {
                var door = Usable with { Barricaded = true };

                var verdict = DoorRules.MayDestroyBarricade(door);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Refuses_a_door_with_no_barricade()
            {
                var verdict = DoorRules.MayDestroyBarricade(Usable);

                verdict.Reason.ShouldBe(Refusal.DoorNotBarricaded);
            }

            [Fact]
            public void Prefers_a_missing_door_to_no_barricade()
            {
                var verdict = DoorRules.MayDestroyBarricade(Missing);

                verdict.Reason.ShouldBe(Refusal.NoSuchDoor);
            }
        }

        public class MayDestroy
        {
            [Fact]
            public void Allows_breaking_a_broken_door_again()
            {
                var door = Usable with { Destroyed = true };

                var verdict = DoorRules.MayDestroy(door);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Refuses_a_door_just_past_the_reach()
            {
                var verdict = DoorRules.MayDestroy(At(600.5f));

                verdict.Reason.ShouldBe(Refusal.OutOfReach);
            }
        }

        public class MayHit
        {
            [Fact]
            public void Allows_a_hit_exactly_at_the_shot_reach()
            {
                var verdict = DoorRules.MayHit(At(1200f), 10f);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Refuses_a_hit_just_past_the_shot_reach()
            {
                var verdict = DoorRules.MayHit(At(1200.5f), 10f);

                verdict.Reason.ShouldBe(Refusal.OutOfReach);
            }

            [Fact]
            public void Words_the_reach_with_the_shot_reach()
            {
                var verdict = DoorRules.MayHit(At(1300f), 10f);

                var text = Refusals.Text(verdict);

                text.ShouldBe(
                    "Wooden door is 1300 away from them (reach 1200)");
            }

            [Fact]
            public void Refuses_a_broken_door()
            {
                var door = Usable with { Destroyed = true };

                var verdict = DoorRules.MayHit(door, 10f);

                verdict.Reason.ShouldBe(Refusal.DoorAlreadyBroken);
            }

            [Fact]
            public void Allows_a_hit_exactly_at_the_largest()
            {
                var verdict = DoorRules.MayHit(Usable, 1000f);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Refuses_a_hit_just_past_the_largest()
            {
                var verdict = DoorRules.MayHit(Usable, 1000.5f);

                verdict.Reason.ShouldBe(Refusal.HitTooLarge);
            }

            [Fact]
            public void Words_the_hit_as_a_whole_number()
            {
                var verdict = DoorRules.MayHit(Usable, 1500f);

                var text = Refusals.Text(verdict);

                text.ShouldBe("a hit of 1500 is more than any weapon does");
            }

            [Fact]
            public void Allows_a_negative_hit()
            {
                var verdict = DoorRules.MayHit(Usable, -50f);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Allows_a_hit_that_isnt_a_number()
            {
                var verdict = DoorRules.MayHit(Usable, float.NaN);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Prefers_a_broken_door_to_a_hit_too_large()
            {
                var door = Usable with { Destroyed = true };

                var verdict = DoorRules.MayHit(door, 1500f);

                verdict.Reason.ShouldBe(Refusal.DoorAlreadyBroken);
            }
        }

        public class MayLock
        {
            [Fact]
            public void Refuses_a_lock_as_the_storys()
            {
                var verdict = DoorRules.MayLock(Usable);

                verdict.Reason.ShouldBe(Refusal.StoryOnly);
            }

            [Fact]
            public void Gives_the_reach_for_a_far_door()
            {
                var verdict = DoorRules.MayLock(At(600.5f));

                verdict.Reason.ShouldBe(Refusal.OutOfReach);
            }

            [Fact]
            public void Refuses_a_door_that_isnt_there()
            {
                var verdict = DoorRules.MayLock(Missing);

                verdict.Reason.ShouldBe(Refusal.NoSuchDoor);
            }
        }

        public class MayUnlock
        {
            [Fact]
            public void Allows_a_key_the_host_has()
            {
                var door = Locked with { HasKey = true };

                var verdict = DoorRules.MayUnlock(door, 1f);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Refuses_a_door_with_no_lock()
            {
                var verdict = DoorRules.MayUnlock(Usable, 1f);

                verdict.Reason.ShouldBe(Refusal.DoorHasNoLock);
            }

            [Fact]
            public void Refuses_a_door_that_isnt_locked()
            {
                var door = Locked with { Locked = false };

                var verdict = DoorRules.MayUnlock(door, 1f);

                verdict.Reason.ShouldBe(Refusal.DoorNotLocked);
            }

            [Fact]
            public void Refuses_an_unlock_by_the_story()
            {
                var verdict = DoorRules.MayUnlock(Locked, 0f);

                verdict.Reason.ShouldBe(Refusal.StoryOnly);
            }

            [Fact]
            public void Refuses_a_key_nobody_has()
            {
                var verdict = DoorRules.MayUnlock(Locked, 1f);

                verdict.ShouldBe(Verdict.Refuse(Refusal.NoKey, "Bunker"));
            }

            [Theory]
            [InlineData(2f)]
            [InlineData(1.5f)]
            [InlineData(-1f)]
            public void Allows_any_other_way_without_a_check(float how)
            {
                var verdict = DoorRules.MayUnlock(Locked, how);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Prefers_the_reach_to_no_lock()
            {
                var verdict = DoorRules.MayUnlock(At(600.5f), 1f);

                verdict.Reason.ShouldBe(Refusal.OutOfReach);
            }

            [Fact]
            public void Prefers_no_lock_to_the_story()
            {
                var verdict = DoorRules.MayUnlock(Usable, 0f);

                verdict.Reason.ShouldBe(Refusal.DoorHasNoLock);
            }

            [Fact]
            public void Prefers_an_open_lock_to_the_story()
            {
                var door = Locked with { Locked = false };

                var verdict = DoorRules.MayUnlock(door, 0f);

                verdict.Reason.ShouldBe(Refusal.DoorNotLocked);
            }
        }

        public class MayUndestroy
        {
            [Fact]
            public void Allows_mending_a_door_in_any_state()
            {
                var verdict = DoorRules.MayUndestroy(Usable);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Refuses_a_door_that_isnt_there()
            {
                var verdict = DoorRules.MayUndestroy(Missing);

                verdict.Reason.ShouldBe(Refusal.NoSuchDoor);
            }
        }

        public class MayUnblock
        {
            [Fact]
            public void Refuses_an_unblock_as_the_storys()
            {
                var door = Usable with { Blocked = true };

                var verdict = DoorRules.MayUnblock(door);

                verdict.Reason.ShouldBe(Refusal.StoryOnly);
            }

            [Fact]
            public void Gives_the_reach_for_a_far_door()
            {
                var verdict = DoorRules.MayUnblock(At(600.5f));

                verdict.Reason.ShouldBe(Refusal.OutOfReach);
            }
        }
    }
}
