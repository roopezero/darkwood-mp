using DarkwoodMP.World.Dreaming;

namespace DarkwoodMP.Tests.World.Dreaming
{
    public partial class DreamRulesTests
    {
        /// The game is in a dream.
        static readonly DreamFacts Dreaming = new DreamFacts
        {
            HasDreams = true,
            Dreaming = true,
        };

        /// The game is awake.
        static readonly DreamFacts Awake = new DreamFacts { HasDreams = true };

        /// A dream is shared and not ended, in a session.
        static readonly DreamShareFacts Shared = new DreamShareFacts
        {
            HasSession = true,
            Shared = true,
            OutcomeAllowed = true,
            SameDream = true,
        };

        static Seconds T(float s) => new Seconds(s);

        public class Limits
        {
            [Fact]
            public void Pins_the_times()
            {
                DreamRules.GiveUpAfter.Value.ShouldBe(60f);
                DreamRules.StoveWait.Value.ShouldBe(0.7f);
                DreamRules.InputsGrace.Value.ShouldBe(6f);
                DreamRules.InputsGiveUp.Value.ShouldBe(40f);
            }
        }

        public class InDream
        {
            public static TheoryData<DreamFacts> In =>
                new TheoryData<DreamFacts>
                {
                    Dreaming,
                    Awake with { Prepared = true },
                    Awake with { Switching = true },
                    Awake with { EnteringDream = true },
                };

            [Theory]
            [MemberData(nameof(In))]
            public void Is_in_a_dream_or_on_its_way(DreamFacts dream)
            {
                var inDream = DreamRules.InDream(dream);

                inDream.ShouldBeTrue();
            }

            [Fact]
            public void Isnt_in_one_awake()
            {
                var inDream = DreamRules.InDream(Awake);

                inDream.ShouldBeFalse();
            }

            [Fact]
            public void Isnt_in_one_without_Dreams()
            {
                var inDream = DreamRules.InDream(
                    new DreamFacts { EnteringDream = true });

                inDream.ShouldBeFalse();
            }
        }

        public class ClientDreamOff
        {
            [Theory]
            [InlineData(true, true, true, true)]
            [InlineData(false, true, true, false)]
            [InlineData(true, false, true, false)]
            [InlineData(true, true, false, false)]
            public void Turns_dreams_off_for_a_client_playing_together(
                bool dreams,
                bool client,
                bool together,
                bool off)
            {
                var dreamOff = DreamRules.ClientDreamOff(
                    dreams,
                    client,
                    together);

                dreamOff.ShouldBe(off);
            }
        }

        public class DrawsEarly
        {
            static readonly DreamFacts Entering = Awake with
            {
                WantToDream = true,
                EnteringDream = true,
            };

            static readonly DreamShareFacts Host = new DreamShareFacts
            {
                HasSession = true,
                IsHost = true,
                Together = true,
            };

            [Fact]
            public void Draws_the_hosts_level_dream_at_once()
            {
                var draws = DreamRules.DrawsEarly(Entering, Host);

                draws.ShouldBeTrue();
            }

            public static TheoryData<DreamFacts, DreamShareFacts> Not =>
                new TheoryData<DreamFacts, DreamShareFacts>
                {
                    { Entering, Host with { HasSession = false } },
                    { Entering, Host with { IsHost = false } },
                    { Entering, Host with { Together = false } },
                    { Entering with { HasDreams = false }, Host },
                    { Entering with { WantToDream = false }, Host },
                    { Entering with { EnteringDream = false }, Host },
                };

            [Theory]
            [MemberData(nameof(Not))]
            public void Leaves_the_draw_to_the_game_otherwise(
                DreamFacts dream,
                DreamShareFacts share)
            {
                var draws = DreamRules.DrawsEarly(dream, share);

                draws.ShouldBeFalse();
            }
        }

        public class PresetChosen
        {
            static readonly DreamShareFacts Marked = new DreamShareFacts
            {
                HasSession = true,
                IsHost = true,
                Together = true,
                Marked = true,
                HasPreset = true,
            };

            [Fact]
            public void Shares_the_marked_dream()
            {
                var step = DreamRules.PresetChosen(Awake, Marked);

                step.ShouldBe(PresetStep.Share);
            }

            [Fact]
            public void Keeps_the_mark_while_the_game_loads()
            {
                var step = DreamRules.PresetChosen(
                    Awake with { LoadingGame = true },
                    Marked with { HasPreset = false });

                step.ShouldBe(PresetStep.Keep);
            }

            [Fact]
            public void Does_nothing_without_the_mark()
            {
                var step = DreamRules.PresetChosen(
                    Awake,
                    Marked with { Marked = false });

                step.ShouldBe(PresetStep.Keep);
            }

            public static TheoryData<DreamShareFacts> Cleared =>
                new TheoryData<DreamShareFacts>
                {
                    Marked with { HasPreset = false },
                    Marked with { HasSession = false },
                    Marked with { IsHost = false },
                    Marked with { Together = false },
                };

            [Theory]
            [MemberData(nameof(Cleared))]
            public void Clears_the_mark_without_sending(DreamShareFacts share)
            {
                var step = DreamRules.PresetChosen(Awake, share);

                step.ShouldBe(PresetStep.Clear);
            }
        }

        public class Ending
        {
            [Fact]
            public void Broadcasts_the_hosts_end()
            {
                var send = DreamRules.Ending(
                    Dreaming,
                    Shared with { IsHost = true });

                send.ShouldBe(EndSend.Broadcast);
            }

            [Fact]
            public void Sends_a_clients_end_to_the_host()
            {
                var send = DreamRules.Ending(
                    Dreaming,
                    Shared with { ClientMode = true });

                send.ShouldBe(EndSend.ToHost);
            }

            [Fact]
            public void Marks_it_ended_with_nothing_sent_otherwise()
            {
                var send = DreamRules.Ending(Dreaming, Shared);

                send.ShouldBe(EndSend.Mark);
            }

            public static TheoryData<DreamFacts, DreamShareFacts> Not =>
                new TheoryData<DreamFacts, DreamShareFacts>
                {
                    { Dreaming, Shared with { Shared = false, IsHost = true } },
                    { Dreaming with { HasDreams = false }, Shared },
                    { Awake, Shared with { IsHost = true } },
                    { Dreaming, Shared with { Ended = true, IsHost = true } },
                    {
                        Dreaming,
                        Shared with { Replaying = true, IsHost = true }
                    },
                    { Dreaming, Shared with { HasSession = false } },
                };

            [Theory]
            [MemberData(nameof(Not))]
            public void Does_nothing_unless_the_shared_dream_ends_here(
                DreamFacts dream,
                DreamShareFacts share)
            {
                var send = DreamRules.Ending(dream, share);

                send.ShouldBe(EndSend.None);
            }
        }

        public class HostEnd
        {
            static readonly DreamShareFacts Host =
                Shared with { IsHost = true };

            [Fact]
            public void Relays_and_ends_the_shared_dream()
            {
                var step = DreamRules.HostEnd(Dreaming, Host);

                step.ShouldBe(HostEndStep.RelayAndEnd);
            }

            [Fact]
            public void Doesnt_pass_on_another_dreams_end()
            {
                var step = DreamRules.HostEnd(
                    Dreaming,
                    Host with { SameDream = false });

                step.ShouldBe(HostEndStep.NotPassedOn);
            }

            public static TheoryData<DreamFacts, DreamShareFacts> Dropped =>
                new TheoryData<DreamFacts, DreamShareFacts>
                {
                    { Dreaming, Host with { Shared = false } },
                    { Dreaming with { HasDreams = false }, Host },
                    { Awake, Host },
                    { Dreaming, Host with { Ended = true } },
                    { Dreaming, Host with { OutcomeAllowed = false } },
                    {
                        Dreaming,
                        Host with { OutcomeAllowed = false, SameDream = false }
                    },
                };

            [Theory]
            [MemberData(nameof(Dropped))]
            public void Drops_an_end_it_cant_take(
                DreamFacts dream,
                DreamShareFacts share)
            {
                var step = DreamRules.HostEnd(dream, share);

                step.ShouldBe(HostEndStep.Drop);
            }
        }

        public class ClientEnd
        {
            static readonly DreamShareFacts FromHost = Shared with
            {
                FromHost = true,
                SameDream = false,
            };

            [Fact]
            public void Ends_ours_without_looking_at_the_name()
            {
                var step = DreamRules.ClientEnd(Dreaming, FromHost);

                step.ShouldBe(ClientEndStep.End);
            }

            [Fact]
            public void Keeps_the_end_while_on_the_way_in()
            {
                var step = DreamRules.ClientEnd(
                    Awake with { Prepared = true },
                    FromHost);

                step.ShouldBe(ClientEndStep.KeepPending);
            }

            [Fact]
            public void Keeps_the_end_with_a_start_pending()
            {
                var step = DreamRules.ClientEnd(
                    Awake,
                    FromHost with { PendingStart = true });

                step.ShouldBe(ClientEndStep.KeepPending);
            }

            public static TheoryData<DreamFacts, DreamShareFacts> Dropped =>
                new TheoryData<DreamFacts, DreamShareFacts>
                {
                    { Dreaming, FromHost with { FromHost = false } },
                    { Dreaming, FromHost with { Shared = false } },
                    { Dreaming with { HasDreams = false }, FromHost },
                    { Awake, FromHost },
                    { Dreaming, FromHost with { Ended = true } },
                };

            [Theory]
            [MemberData(nameof(Dropped))]
            public void Drops_an_end_it_cant_take(
                DreamFacts dream,
                DreamShareFacts share)
            {
                var step = DreamRules.ClientEnd(dream, share);

                step.ShouldBe(ClientEndStep.Drop);
            }
        }

        public class Start
        {
            static readonly DreamShareFacts FromHost = new DreamShareFacts
            {
                FromHost = true,
                ClientMode = true,
            };

            [Fact]
            public void Keeps_the_hosts_dream_as_pending()
            {
                var step = DreamRules.Start(Awake, FromHost);

                step.ShouldBe(StartStep.Pending);
            }

            [Fact]
            public void Says_we_are_in_a_dream_already()
            {
                var step = DreamRules.Start(Dreaming, FromHost);

                step.ShouldBe(StartStep.AlreadyIn);
            }

            [Fact]
            public void Adopts_the_dream_we_went_into_first()
            {
                var share = FromHost with { SameDream = true };

                var step = DreamRules.Start(Dreaming, share);

                step.ShouldBe(StartStep.Adopt);
            }

            public static TheoryData<DreamFacts, DreamShareFacts> Dropped =>
                new TheoryData<DreamFacts, DreamShareFacts>
                {
                    { Awake, FromHost with { FromHost = false } },
                    { Awake, FromHost with { ClientMode = false } },
                    { new DreamFacts(), FromHost },
                };

            [Theory]
            [MemberData(nameof(Dropped))]
            public void Drops_a_start_it_cant_take(
                DreamFacts dream,
                DreamShareFacts share)
            {
                var step = DreamRules.Start(dream, share);

                step.ShouldBe(StartStep.Drop);
            }
        }

        public class StartBy
        {
            [Fact]
            public void Gives_up_a_minute_later()
            {
                var by = DreamRules.StartBy(T(10f));

                by.Value.ShouldBe(70f);
            }
        }

        public class OnPendingEnd
        {
            [Fact]
            public void Ends_ours_once_the_dream_has_begun()
            {
                var step = DreamRules.OnPendingEnd(Dreaming, true, false);

                step.ShouldBe(PendingEnd.End);
            }

            [Fact]
            public void Drops_it_when_ours_had_ended()
            {
                var step = DreamRules.OnPendingEnd(Dreaming, true, true);

                step.ShouldBe(PendingEnd.Drop);
            }

            public static TheoryData<DreamFacts, bool> Kept =>
                new TheoryData<DreamFacts, bool>
                {
                    { Dreaming, false },
                    { Awake, true },
                    { Dreaming with { Switching = true }, true },
                    { Dreaming with { HasDreams = false }, true },
                };

            [Theory]
            [MemberData(nameof(Kept))]
            public void Keeps_it_until_the_dream_has_begun(
                DreamFacts dream,
                bool pending)
            {
                var step = DreamRules.OnPendingEnd(dream, pending, false);

                step.ShouldBe(PendingEnd.Keep);
            }
        }
    }
}
