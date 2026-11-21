using System.Collections.Generic;
using DarkwoodMP.Combat.Joiners;

namespace DarkwoodMP.Tests.Combat.Joiners
{
    public class JoinerAiRulesTests
    {
        /// An active, awake enemy on the host, bumped by a living, visible
        /// joiner's stand-in in the world.
        static readonly BumpFacts Bumped = new BumpFacts
        {
            Collider = true,
            On = true,
            Active = true,
            StandIn = true,
            StandInAlive = true,
        };

        /// A spawned character on the host, sent at nobody.
        static readonly ActivityFacts Spawned = new ActivityFacts
        {
            On = true,
            Exists = true,
        };

        /// An enemy about to check out a location, chasing a living joiner
        /// who is indoors.
        static readonly ChaseFacts Chasing = new ChaseFacts
        {
            On = true,
            StandIn = true,
            StandInAlive = true,
            StandInInside = true,
        };

        /// The brother with three attacks, fighting a joiner at 19% of the
        /// host's player's 100 health.
        static readonly BrotherFacts Brother = new BrotherFacts
        {
            HealthPercent = 19,
            HasHost = true,
            HostMaxHealth = 100f,
            Attacks = 3,
        };

        public class Limits
        {
            [Fact]
            public void Pins_the_joiner_ai_limits()
            {
                var limits = new[]
                {
                    JoinerAiRules.NearRange,
                    JoinerAiRules.WeakHealth,
                    JoinerAiRules.DefaultMaxHealth,
                    JoinerAiRules.WeakAttacks,
                };

                limits.ShouldBe(new[] { 1500f, 20f, 100f, 3f });
            }
        }

        public class Bump
        {
            [Fact]
            public void Reacts_to_a_visible_joiner()
            {
                JoinerAiRules.Bump(Bumped).ShouldBe(BumpStep.React);
            }

            [Fact]
            public void Leaves_it_to_the_game_with_no_collider()
            {
                var bump = Bumped with { Collider = false };

                JoinerAiRules.Bump(bump).ShouldBe(BumpStep.Game);
            }

            [Fact]
            public void Leaves_it_to_the_game_when_off()
            {
                var bump = Bumped with { On = false };

                JoinerAiRules.Bump(bump).ShouldBe(BumpStep.Game);
            }

            [Fact]
            public void Leaves_it_to_the_game_on_a_client()
            {
                var bump = Bumped with { ClientMode = true };

                JoinerAiRules.Bump(bump).ShouldBe(BumpStep.Game);
            }

            [Fact]
            public void Leaves_it_to_the_game_for_an_inactive_enemy()
            {
                var bump = Bumped with { Active = false };

                JoinerAiRules.Bump(bump).ShouldBe(BumpStep.Game);
            }

            [Fact]
            public void Leaves_it_to_the_game_for_another_collider()
            {
                var bump = Bumped with { StandIn = false };

                JoinerAiRules.Bump(bump).ShouldBe(BumpStep.Game);
            }

            [Fact]
            public void Leaves_it_to_the_game_for_a_dead_joiner()
            {
                var bump = Bumped with { StandInAlive = false };

                JoinerAiRules.Bump(bump).ShouldBe(BumpStep.Game);
            }

            [Fact]
            public void Leaves_it_to_the_game_for_an_away_joiner()
            {
                var bump = Bumped with { StandInAway = true };

                JoinerAiRules.Bump(bump).ShouldBe(BumpStep.Game);
            }

            [Fact]
            public void Starts_a_banshee_s_attack()
            {
                var bump = Bumped with { Banshee = true };

                JoinerAiRules.Bump(bump).ShouldBe(BumpStep.Banshee);
            }

            [Fact]
            public void Starts_a_sleeping_banshee_s_attack_before_waking()
            {
                var bump = Bumped with { Banshee = true, Sleeping = true };

                JoinerAiRules.Bump(bump).ShouldBe(BumpStep.Banshee);
            }

            [Fact]
            public void Wakes_a_sleeper()
            {
                var bump = Bumped with { Sleeping = true };

                JoinerAiRules.Bump(bump).ShouldBe(BumpStep.Wake);
            }

            [Fact]
            public void Lets_a_sleeper_that_wakes_only_by_hand_react()
            {
                var bump = Bumped with
                {
                    Sleeping = true,
                    WakesOnlyByHand = true,
                };

                JoinerAiRules.Bump(bump).ShouldBe(BumpStep.React);
            }

            [Fact]
            public void Only_touches_for_an_invisible_joiner()
            {
                var bump = Bumped with { StandInInvisible = true };

                JoinerAiRules.Bump(bump).ShouldBe(BumpStep.Touch);
            }

            [Fact]
            public void Only_touches_for_an_enemy_that_dies_on_contact()
            {
                var bump = Bumped with { DiesOnContact = true };

                JoinerAiRules.Bump(bump).ShouldBe(BumpStep.Touch);
            }
        }

        public class LooksForJoiners
        {
            [Theory]
            [InlineData(false, true, true)]
            [InlineData(true, true, false)]
            [InlineData(false, false, false)]
            public void Looks_when_the_game_said_no_while_on(
                bool result,
                bool on,
                bool looks)
            {
                JoinerAiRules.LooksForJoiners(result, on).ShouldBe(looks);
            }
        }

        public class IsNear
        {
            [Fact]
            public void Counts_a_joiner_just_inside_the_range()
            {
                JoinerAiRules.IsNear(true, false, 1499.9f).ShouldBeTrue();
            }

            [Fact]
            public void Leaves_out_a_joiner_exactly_at_the_range()
            {
                JoinerAiRules.IsNear(true, false, 1500f).ShouldBeFalse();
            }

            [Fact]
            public void Leaves_out_a_dead_joiner()
            {
                JoinerAiRules.IsNear(false, false, 10f).ShouldBeFalse();
            }

            [Fact]
            public void Leaves_out_an_away_joiner()
            {
                JoinerAiRules.IsNear(true, true, 10f).ShouldBeFalse();
            }
        }

        public class AttacksOnSight
        {
            [Theory]
            [InlineData(false, true)]
            [InlineData(true, false)]
            public void Keeps_a_defensive_character_defensive(
                bool defensive,
                bool attacks)
            {
                JoinerAiRules.AttacksOnSight(defensive).ShouldBe(attacks);
            }
        }

        public class TargetInside
        {
            [Theory]
            [InlineData(true, true, false, true)]
            [InlineData(true, false, true, false)]
            [InlineData(false, false, true, true)]
            [InlineData(false, true, false, false)]
            public void Asks_the_stand_in_s_ground_else_the_host_s(
                bool standIn,
                bool standInInside,
                bool hostInside,
                bool inside)
            {
                var result = JoinerAiRules.TargetInside(
                    standIn,
                    standInInside,
                    hostInside);

                result.ShouldBe(inside);
            }
        }

        public class HatchesAtHost
        {
            [Theory]
            [InlineData(true, true, true, true)]
            [InlineData(true, true, false, false)]
            [InlineData(false, true, false, true)]
            [InlineData(true, false, false, true)]
            public void Hatches_at_the_host_unless_a_joiner_is_nearest(
                bool on,
                bool exists,
                bool nearestIsHost,
                bool atHost)
            {
                var result = JoinerAiRules.HatchesAtHost(
                    on,
                    exists,
                    nearestIsHost);

                result.ShouldBe(atHost);
            }
        }

        public class SpawnedLocation
        {
            [Fact]
            public void Uses_the_host_s_for_one_sent_at_nobody()
            {
                var pick = JoinerAiRules.SpawnedLocation(Spawned);

                pick.ShouldBe(LocationPick.Host);
            }

            [Fact]
            public void Uses_the_location_it_was_sent_at()
            {
                var a = Spawned with
                {
                    Sent = true,
                    SentLocation = true,
                    SentStandIn = true,
                    StandInLocation = true,
                };

                JoinerAiRules.SpawnedLocation(a).ShouldBe(LocationPick.Sent);
            }

            [Fact]
            public void Uses_the_host_s_when_off()
            {
                var a = Spawned with
                {
                    On = false,
                    Sent = true,
                    SentLocation = true,
                };

                JoinerAiRules.SpawnedLocation(a).ShouldBe(LocationPick.Host);
            }

            [Fact]
            public void Uses_the_host_s_with_no_character()
            {
                var a = Spawned with
                {
                    Exists = false,
                    Sent = true,
                    SentLocation = true,
                };

                JoinerAiRules.SpawnedLocation(a).ShouldBe(LocationPick.Host);
            }

            [Fact]
            public void Ignores_a_location_when_not_sent()
            {
                var a = Spawned with { SentLocation = true };

                JoinerAiRules.SpawnedLocation(a).ShouldBe(LocationPick.Host);
            }

            [Fact]
            public void Uses_the_sent_stand_in_s_location()
            {
                var a = Spawned with
                {
                    Sent = true,
                    SentStandIn = true,
                    StandInLocation = true,
                };

                var pick = JoinerAiRules.SpawnedLocation(a);

                pick.ShouldBe(LocationPick.StandIn);
            }

            [Fact]
            public void Uses_the_best_stand_in_s_location()
            {
                var a = Spawned with
                {
                    BestStandIn = true,
                    StandInLocation = true,
                };

                var pick = JoinerAiRules.SpawnedLocation(a);

                pick.ShouldBe(LocationPick.StandIn);
            }

            [Fact]
            public void Uses_the_host_s_when_the_stand_in_is_in_none()
            {
                var a = Spawned with { BestStandIn = true };

                JoinerAiRules.SpawnedLocation(a).ShouldBe(LocationPick.Host);
            }
        }

        public class ActivityPlayer
        {
            [Fact]
            public void Goes_for_the_host_with_no_best_player()
            {
                JoinerAiRules.ActivityPlayer(Spawned).ShouldBe(PlayerPick.Host);
            }

            [Fact]
            public void Goes_for_the_living_stand_in_it_was_sent_at()
            {
                var a = Spawned with
                {
                    Sent = true,
                    SentStandIn = true,
                    SentAlive = true,
                    BestPlayer = true,
                };

                JoinerAiRules.ActivityPlayer(a).ShouldBe(PlayerPick.Sent);
            }

            [Fact]
            public void Goes_for_the_best_when_the_sent_stand_in_is_dead()
            {
                var a = Spawned with
                {
                    Sent = true,
                    SentStandIn = true,
                    BestPlayer = true,
                };

                JoinerAiRules.ActivityPlayer(a).ShouldBe(PlayerPick.Best);
            }

            [Fact]
            public void Goes_for_the_best_when_not_sent()
            {
                var a = Spawned with
                {
                    SentStandIn = true,
                    SentAlive = true,
                    BestPlayer = true,
                };

                JoinerAiRules.ActivityPlayer(a).ShouldBe(PlayerPick.Best);
            }

            [Fact]
            public void Goes_for_the_host_when_off()
            {
                var a = Spawned with { On = false, BestPlayer = true };

                JoinerAiRules.ActivityPlayer(a).ShouldBe(PlayerPick.Host);
            }

            [Fact]
            public void Goes_for_the_host_with_no_character()
            {
                var a = Spawned with { Exists = false, BestPlayer = true };

                JoinerAiRules.ActivityPlayer(a).ShouldBe(PlayerPick.Host);
            }
        }

        public class NotesChase
        {
            [Fact]
            public void Notes_a_living_joiner_indoors()
            {
                JoinerAiRules.NotesChase(Chasing).ShouldBeTrue();
            }

            [Fact]
            public void Notes_nothing_when_off()
            {
                var chase = Chasing with { On = false };

                JoinerAiRules.NotesChase(chase).ShouldBeFalse();
            }

            [Fact]
            public void Notes_nothing_while_already_checking_out()
            {
                var chase = Chasing with { CheckingOut = true };

                JoinerAiRules.NotesChase(chase).ShouldBeFalse();
            }

            [Fact]
            public void Notes_nothing_for_another_super_target()
            {
                var chase = Chasing with { StandIn = false };

                JoinerAiRules.NotesChase(chase).ShouldBeFalse();
            }

            [Fact]
            public void Notes_nothing_for_a_dead_joiner()
            {
                var chase = Chasing with { StandInAlive = false };

                JoinerAiRules.NotesChase(chase).ShouldBeFalse();
            }

            [Fact]
            public void Notes_nothing_for_a_joiner_outdoors()
            {
                var chase = Chasing with { StandInInside = false };

                JoinerAiRules.NotesChase(chase).ShouldBeFalse();
            }
        }

        public class Waypoint
        {
            [Theory]
            [InlineData(true, false, true, WaypointStep.Pick)]
            [InlineData(true, false, false, WaypointStep.Forget)]
            [InlineData(true, true, true, WaypointStep.Leave)]
            [InlineData(false, false, true, WaypointStep.Leave)]
            public void Picks_only_for_a_noted_chase_with_no_waypoint(
                bool chased,
                bool waypoint,
                bool location,
                WaypointStep step)
            {
                var result = JoinerAiRules.Waypoint(chased, waypoint, location);

                result.ShouldBe(step);
            }
        }

        public class NearestWaypoint
        {
            static WaypointSpot Spot(float d) =>
                new WaypointSpot { Exists = true, SqrDistance = d };

            [Fact]
            public void Picks_the_nearest()
            {
                var spots = new List<WaypointSpot>
                {
                    Spot(900f),
                    Spot(100f),
                    Spot(400f),
                };

                JoinerAiRules.NearestWaypoint(spots).ShouldBe(1);
            }

            [Fact]
            public void Picks_the_first_on_a_tie()
            {
                var spots = new List<WaypointSpot> { Spot(100f), Spot(100f) };

                JoinerAiRules.NearestWaypoint(spots).ShouldBe(0);
            }

            [Fact]
            public void Skips_a_missing_waypoint()
            {
                var spots = new List<WaypointSpot>
                {
                    new WaypointSpot { SqrDistance = 1f },
                    Spot(100f),
                };

                JoinerAiRules.NearestWaypoint(spots).ShouldBe(1);
            }

            [Fact]
            public void Picks_none_of_none()
            {
                var spots = new List<WaypointSpot>();

                JoinerAiRules.NearestWaypoint(spots).ShouldBe(-1);
            }
        }

        public class BrotherWeak
        {
            [Fact]
            public void Switches_below_twenty_health()
            {
                JoinerAiRules.BrotherWeak(Brother).ShouldBeTrue();
            }

            [Fact]
            public void Stays_at_exactly_twenty_health()
            {
                var brother = Brother with { HealthPercent = 20 };

                JoinerAiRules.BrotherWeak(brother).ShouldBeFalse();
            }

            [Fact]
            public void Goes_by_the_host_s_max_health()
            {
                var brother = Brother with
                {
                    HealthPercent = 30,
                    HostMaxHealth = 50f,
                };

                JoinerAiRules.BrotherWeak(brother).ShouldBeTrue();
            }

            [Fact]
            public void Takes_a_max_of_one_hundred_with_no_host()
            {
                var brother = Brother with
                {
                    HasHost = false,
                    HostMaxHealth = 1000f,
                };

                JoinerAiRules.BrotherWeak(brother).ShouldBeTrue();
            }

            [Fact]
            public void Stays_with_fewer_than_three_attacks()
            {
                var brother = Brother with { Attacks = 2 };

                JoinerAiRules.BrotherWeak(brother).ShouldBeFalse();
            }
        }
    }
}
