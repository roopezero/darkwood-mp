using DarkwoodMP.World.Nights;

namespace DarkwoodMP.Tests.World.Nights
{
    public class NightObjectRulesTests
    {
        /// The host in a world it shares.
        static readonly NightHostFacts Sharing = new NightHostFacts
        {
            HasSession = true,
            IsHost = true,
            Replaying = false,
            InWorld = true,
            Sharing = true,
        };

        /// A tagged spawn step made an object that isn't a character.
        static readonly AddedFacts StepMade = new AddedFacts
        {
            Made = true,
            Records = true,
            LocationEvent = false,
            InGroup = false,
            CopyHasEvents = false,
            HasStep = true,
            SpawnStep = true,
            TargetIsPrefab = true,
            Tagged = true,
            Character = false,
            HasNpc = false,
        };

        /// A night event's copy of one of its GameEvents.
        static readonly AddedFacts EventCopy = StepMade with
        {
            LocationEvent = true,
            InGroup = true,
            CopyHasEvents = true,
            HasStep = false,
        };

        /// An armed trap, sent armed, while sharing.
        static readonly LiveFacts Unchanged = new LiveFacts
        {
            Alive = true,
            Sending = true,
            Sent = NightObjectRules.Armed,
            Now = NightObjectRules.Armed,
        };

        /// Due exactly now, the trap off and the host's state armed.
        static readonly RearmFacts DueNow = new RearmFacts
        {
            Now = new Seconds(20f),
            At = new Seconds(20f),
            HasTrap = true,
            Active = false,
            Armed = NightObjectRules.Armed,
        };

        public class Limits
        {
            [Fact]
            public void Pins_the_message_kinds()
            {
                var kinds = new[]
                {
                    NightObjectRules.Made,
                    NightObjectRules.Gone,
                    NightObjectRules.MadeBefore,
                };

                kinds.ShouldBe(new byte[] { 1, 2, 3 });
            }

            [Fact]
            public void Pins_the_trap_states()
            {
                var states = new[]
                {
                    NightObjectRules.NoTrap,
                    NightObjectRules.Armed,
                    NightObjectRules.Unarmed,
                };

                states.ShouldBe(new byte[] { 255, 1, 0 });
            }

            [Fact]
            public void Pins_the_check_and_the_rearm_delay()
            {
                var limits = new[]
                {
                    NightObjectRules.CheckEvery,
                    NightObjectRules.RearmAfter,
                };

                limits.ShouldBe(new[] { 1f, 1f });
            }
        }

        public class Records
        {
            [Fact]
            public void Records_while_hosting()
            {
                NightObjectRules.Records(Sharing).ShouldBeTrue();
            }

            [Fact]
            public void Records_with_nobody_in_yet()
            {
                var host = Sharing with { Sharing = false };

                NightObjectRules.Records(host).ShouldBeTrue();
            }

            [Fact]
            public void Leaves_out_a_replay()
            {
                var host = Sharing with { Replaying = true };

                NightObjectRules.Records(host).ShouldBeFalse();
            }

            [Fact]
            public void Leaves_out_a_client()
            {
                var host = Sharing with { IsHost = false };

                NightObjectRules.Records(host).ShouldBeFalse();
            }

            [Fact]
            public void Leaves_out_a_game_with_no_world()
            {
                var host = Sharing with { InWorld = false };

                NightObjectRules.Records(host).ShouldBeFalse();
            }

            [Fact]
            public void Leaves_out_a_game_with_no_session()
            {
                var host = Sharing with { HasSession = false };

                NightObjectRules.Records(host).ShouldBeFalse();
            }
        }

        public class Sends
        {
            [Fact]
            public void Sends_while_sharing()
            {
                NightObjectRules.Sends(Sharing).ShouldBeTrue();
            }

            [Fact]
            public void Holds_back_with_nobody_in()
            {
                var host = Sharing with { Sharing = false };

                NightObjectRules.Sends(host).ShouldBeFalse();
            }

            [Fact]
            public void Holds_back_on_a_client()
            {
                var host = Sharing with { IsHost = false };

                NightObjectRules.Sends(host).ShouldBeFalse();
            }
        }

        public class Added
        {
            [Fact]
            public void Records_what_a_tagged_spawn_step_made()
            {
                NightObjectRules.Added(StepMade).ShouldBe(AddedStep.Record);
            }

            [Fact]
            public void Tags_a_night_events_copy()
            {
                NightObjectRules.Added(EventCopy).ShouldBe(AddedStep.Tag);
            }

            [Fact]
            public void Looks_at_the_step_for_a_copy_with_no_events()
            {
                var made = EventCopy with
                {
                    CopyHasEvents = false,
                    HasStep = true,
                };

                NightObjectRules.Added(made).ShouldBe(AddedStep.Record);
            }

            [Fact]
            public void Records_a_night_npc()
            {
                var made = StepMade with { Character = true, HasNpc = true };

                NightObjectRules.Added(made).ShouldBe(AddedStep.Record);
            }

            [Fact]
            public void Leaves_an_enemy_to_net_enemies()
            {
                var made = StepMade with { Character = true };

                NightObjectRules.Added(made).ShouldBe(AddedStep.Nothing);
            }

            public static TheoryData<AddedFacts> Ignored =>
                new TheoryData<AddedFacts>
                {
                    StepMade with { Made = false },
                    StepMade with { Records = false },
                    EventCopy with { Records = false },
                    StepMade with { HasStep = false },
                    StepMade with { SpawnStep = false },
                    StepMade with { TargetIsPrefab = false },
                    StepMade with { Tagged = false },
                    EventCopy with { InGroup = false },
                    EventCopy with { LocationEvent = false },
                };

            [Theory]
            [MemberData(nameof(Ignored))]
            public void Does_nothing_with_anything_else(AddedFacts made)
            {
                NightObjectRules.Added(made).ShouldBe(AddedStep.Nothing);
            }
        }

        public class ArmedOf
        {
            [Theory]
            [InlineData(false, true, 255)]
            [InlineData(true, true, 1)]
            [InlineData(true, false, 0)]
            public void Reads_a_traps_state(bool trap, bool active, byte state)
            {
                NightObjectRules.ArmedOf(trap, active).ShouldBe(state);
            }
        }

        public class CheckDue
        {
            static readonly Seconds Next = new Seconds(10f);

            [Fact]
            public void Checks_exactly_when_due()
            {
                var now = new Seconds(10f);

                NightObjectRules.CheckDue(1, now, Next).ShouldBeTrue();
            }

            [Fact]
            public void Waits_just_before()
            {
                var now = new Seconds(9.99f);

                NightObjectRules.CheckDue(1, now, Next).ShouldBeFalse();
            }

            [Fact]
            public void Skips_with_nothing_recorded()
            {
                var now = new Seconds(50f);

                NightObjectRules.CheckDue(0, now, Next).ShouldBeFalse();
            }

            [Fact]
            public void Plans_the_next_check_a_second_later()
            {
                var next = NightObjectRules.NextCheck(new Seconds(10f));

                next.Value.ShouldBe(11f);
            }
        }

        public class Check
        {
            [Fact]
            public void Keeps_an_unchanged_object()
            {
                NightObjectRules.Check(Unchanged).ShouldBe(LiveStep.Keep);
            }

            [Fact]
            public void Resends_a_trap_that_changed()
            {
                var live = Unchanged with { Now = NightObjectRules.Unarmed };

                NightObjectRules.Check(live).ShouldBe(LiveStep.Resend);
            }

            [Fact]
            public void Keeps_one_sent_with_no_trap()
            {
                var live = Unchanged with
                {
                    Sent = NightObjectRules.NoTrap,
                    Now = NightObjectRules.Armed,
                };

                NightObjectRules.Check(live).ShouldBe(LiveStep.Keep);
            }

            [Fact]
            public void Keeps_a_changed_trap_with_nobody_in()
            {
                var live = Unchanged with
                {
                    Sending = false,
                    Now = NightObjectRules.Unarmed,
                };

                NightObjectRules.Check(live).ShouldBe(LiveStep.Keep);
            }

            [Fact]
            public void Sends_a_gone_object_while_sharing()
            {
                var live = Unchanged with { Alive = false };

                NightObjectRules.Check(live).ShouldBe(LiveStep.SendGone);
            }

            [Fact]
            public void Drops_a_gone_object_with_nobody_in()
            {
                var live = Unchanged with { Alive = false, Sending = false };

                NightObjectRules.Check(live).ShouldBe(LiveStep.Drop);
            }
        }

        public class AppliesNow
        {
            [Theory]
            [InlineData(true, true, true)]
            [InlineData(false, true, false)]
            [InlineData(true, false, false)]
            public void Waits_for_a_ready_world(
                bool ready,
                bool client,
                bool now)
            {
                NightObjectRules.AppliesNow(ready, client).ShouldBe(now);
            }
        }

        public class Apply
        {
            [Theory]
            [InlineData(2, true, true, ApplyStep.Destroy)]
            [InlineData(2, false, false, ApplyStep.Forget)]
            [InlineData(1, true, false, ApplyStep.ArmOnly)]
            [InlineData(3, true, true, ApplyStep.ArmOnly)]
            [InlineData(1, false, false, ApplyStep.NoPrefab)]
            [InlineData(3, false, false, ApplyStep.NoPrefab)]
            [InlineData(3, false, true, ApplyStep.MatchSaved)]
            [InlineData(1, false, true, ApplyStep.Make)]
            public void Applies_the_hosts_object(
                byte kind,
                bool have,
                bool prefab,
                ApplyStep step)
            {
                NightObjectRules.Apply(kind, have, prefab).ShouldBe(step);
            }
        }

        public class NearestSaved
        {
            static readonly Vec3 Origin = new Vec3(0f, 0f, 0f);

            const string Name = "mushroom";

            static SavedObject Mushroom(float x, float y, float z) =>
                new SavedObject
                {
                    Name = Name,
                    Spawned = false,
                    Pos = new Vec3(x, y, z),
                };

            [Fact]
            public void Finds_one_just_inside_the_slack()
            {
                var saved = new[] { Mushroom(30f, 0f, 39.99f) };

                var i = NightObjectRules.NearestSaved(saved, Name, Origin);

                i.ShouldBe(0);
            }

            [Fact]
            public void Leaves_out_one_exactly_at_the_slack()
            {
                var saved = new[] { Mushroom(30f, 0f, 40f) };

                var i = NightObjectRules.NearestSaved(saved, Name, Origin);

                i.ShouldBe(-1);
            }

            [Fact]
            public void Counts_the_height_too()
            {
                var saved = new[] { Mushroom(0f, 50f, 0f) };

                var i = NightObjectRules.NearestSaved(saved, Name, Origin);

                i.ShouldBe(-1);
            }

            [Fact]
            public void Picks_the_nearest_and_the_first_of_ties()
            {
                var saved = new[]
                {
                    Mushroom(20f, 0f, 0f),
                    Mushroom(10f, 0f, 0f),
                    Mushroom(-10f, 0f, 0f),
                };

                var i = NightObjectRules.NearestSaved(saved, Name, Origin);

                i.ShouldBe(1);
            }

            [Fact]
            public void Leaves_out_another_name()
            {
                var saved = new[] { Mushroom(1f, 0f, 0f) };

                var i = NightObjectRules.NearestSaved(saved, "barrel", Origin);

                i.ShouldBe(-1);
            }

            [Fact]
            public void Leaves_out_one_tied_to_an_id()
            {
                var tied = Mushroom(1f, 0f, 0f) with { Spawned = true };
                var saved = new[] { tied };

                var i = NightObjectRules.NearestSaved(saved, Name, Origin);

                i.ShouldBe(-1);
            }
        }

        public class Arms
        {
            [Theory]
            [InlineData(255, false)]
            [InlineData(1, true)]
            [InlineData(0, true)]
            public void Arms_any_state_but_no_trap(byte armed, bool arms)
            {
                NightObjectRules.Arms(armed).ShouldBe(arms);
            }

            [Fact]
            public void Arms_again_a_second_later()
            {
                var at = NightObjectRules.RearmAt(new Seconds(5f));

                at.Value.ShouldBe(6f);
            }
        }

        public class Rearm
        {
            [Fact]
            public void Sets_the_trap_exactly_when_due()
            {
                NightObjectRules.Rearm(DueNow).ShouldBe(RearmStep.Set);
            }

            [Fact]
            public void Waits_just_before()
            {
                var rearm = DueNow with { Now = new Seconds(19.99f) };

                NightObjectRules.Rearm(rearm).ShouldBe(RearmStep.Wait);
            }

            [Fact]
            public void Drops_a_trap_right_already()
            {
                var rearm = DueNow with { Active = true };

                NightObjectRules.Rearm(rearm).ShouldBe(RearmStep.Drop);
            }

            [Fact]
            public void Disarms_an_active_trap_the_host_has_off()
            {
                var rearm = DueNow with
                {
                    Active = true,
                    Armed = NightObjectRules.Unarmed,
                };

                NightObjectRules.Rearm(rearm).ShouldBe(RearmStep.Set);
            }

            [Fact]
            public void Drops_an_object_gone()
            {
                var rearm = DueNow with { HasTrap = false };

                NightObjectRules.Rearm(rearm).ShouldBe(RearmStep.Drop);
            }
        }
    }
}
