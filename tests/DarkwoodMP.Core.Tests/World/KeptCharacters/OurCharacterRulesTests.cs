using DarkwoodMP.World.KeptCharacters;

namespace DarkwoodMP.Tests.World.KeptCharacters
{
    public class OurCharacterRulesTests
    {
        /// A client in the host's world, ready at 100 s, waiting for its
        /// character at 101 s.
        static readonly OurFacts Waiting = new OurFacts
        {
            Client = true,
            InWorld = true,
            ReadyAt = new Seconds(100f),
            Now = new Seconds(101f),
        };

        /// A CharacterLoad from the host for the scene we asked in.
        static readonly LoadFacts Answer = new LoadFacts
        {
            FromHost = true,
            Client = true,
            Asked = true,
            AskedScene = 7,
            Scene = 7,
            InWorld = true,
        };

        static readonly Vec3 Host = new Vec3(500f, 0f, 300f);

        /// Our own kept character, on the map, with the host's spot.
        static readonly StandFacts Own = new StandFacts
        {
            Kind = CharacterKind.Own,
            Spawn = Host,
            OnMap = true,
        };

        static readonly Vec2[] Chunks =
        {
            new Vec2(0f, 0f),
            new Vec2(1000f, 0f),
        };

        public class Placing
        {
            [Fact]
            public void Holds_back_a_player_waiting_for_its_character()
            {
                OurCharacterRules.Placing(Waiting).ShouldBeTrue();
            }

            [Fact]
            public void Pins_the_wait()
            {
                OurCharacterRules.PlaceWait.Value.ShouldBe(6f);
            }

            [Theory]
            [InlineData(105.9f, true)]
            [InlineData(106f, false)]
            public void Holds_back_until_the_wait_is_over(float now, bool held)
            {
                var placing = OurCharacterRules.Placing(Waiting with
                {
                    Now = new Seconds(now),
                });

                placing.ShouldBe(held);
            }

            [Fact]
            public void Holds_back_while_the_world_isnt_ready()
            {
                var placing = OurCharacterRules.Placing(Waiting with
                {
                    ReadyAt = new Seconds(-1f),
                    Now = new Seconds(900f),
                });

                placing.ShouldBeTrue();
            }

            [Fact]
            public void Lets_go_once_the_character_came()
            {
                var placing = OurCharacterRules.Placing(Waiting with
                {
                    Have = true,
                });

                placing.ShouldBeFalse();
            }

            [Fact]
            public void Lets_go_off_client_mode()
            {
                var placing = OurCharacterRules.Placing(Waiting with
                {
                    Client = false,
                });

                placing.ShouldBeFalse();
            }

            [Fact]
            public void Lets_go_out_of_a_world()
            {
                var placing = OurCharacterRules.Placing(Waiting with
                {
                    InWorld = false,
                });

                placing.ShouldBeFalse();
            }
        }

        public class Sends
        {
            static readonly OurFacts Playing = Waiting with { Have = true };

            [Fact]
            public void Sends_a_character_we_have()
            {
                OurCharacterRules.Sends(Playing).ShouldBeTrue();
            }

            [Fact]
            public void Pins_the_interval()
            {
                OurCharacterRules.SaveInterval.Value.ShouldBe(15f);
            }

            [Fact]
            public void Sends_nothing_after_the_quits_save()
            {
                var sends = OurCharacterRules.Sends(Playing with
                {
                    QuitSent = true,
                });

                sends.ShouldBeFalse();
            }

            [Fact]
            public void Sends_nothing_before_it_came()
            {
                OurCharacterRules.Sends(Waiting).ShouldBeFalse();
            }

            [Fact]
            public void Sends_nothing_in_the_prologue()
            {
                var sends = OurCharacterRules.Sends(Playing with
                {
                    Prologue = true,
                });

                sends.ShouldBeFalse();
            }

            [Fact]
            public void Sends_nothing_off_client_mode()
            {
                var sends = OurCharacterRules.Sends(Playing with
                {
                    Client = false,
                });

                sends.ShouldBeFalse();
            }

            [Fact]
            public void Sends_nothing_out_of_a_world()
            {
                var sends = OurCharacterRules.Sends(Playing with
                {
                    InWorld = false,
                });

                sends.ShouldBeFalse();
            }
        }

        public class Sent
        {
            [Fact]
            public void Sends_the_bags_put_away_in_a_dream()
            {
                var sent = OurCharacterRules.Sent(new SentFacts
                {
                    Dreaming = true,
                    Copied = true,
                });

                sent.ShouldBe(SentState.BeforeDream);
            }

            [Fact]
            public void Sends_the_player_as_it_is_out_of_a_dream()
            {
                var sent = OurCharacterRules.Sent(new SentFacts
                {
                    Copied = true,
                });

                sent.ShouldBe(SentState.Current);
            }

            [Fact]
            public void Sends_the_player_as_it_is_when_nothing_was_put_away()
            {
                var sent = OurCharacterRules.Sent(new SentFacts
                {
                    Dreaming = true,
                });

                sent.ShouldBe(SentState.Current);
            }
        }

        public class Take
        {
            [Fact]
            public void Takes_the_answer_for_this_world()
            {
                OurCharacterRules.Take(Answer).ShouldBe(LoadStep.Take);
            }

            [Fact]
            public void Ignores_one_not_from_the_host()
            {
                var step = OurCharacterRules.Take(Answer with
                {
                    FromHost = false,
                    Asked = false,
                });

                step.ShouldBe(LoadStep.Ignore);
            }

            [Fact]
            public void Ignores_one_off_client_mode()
            {
                var step = OurCharacterRules.Take(Answer with
                {
                    Client = false,
                });

                step.ShouldBe(LoadStep.Ignore);
            }

            [Fact]
            public void Asks_again_for_a_world_we_left()
            {
                var step = OurCharacterRules.Take(Answer with { Scene = 8 });

                step.ShouldBe(LoadStep.AskAgain);
            }

            [Fact]
            public void Asks_again_when_we_didnt_ask()
            {
                var step = OurCharacterRules.Take(Answer with
                {
                    Asked = false,
                });

                step.ShouldBe(LoadStep.AskAgain);
            }

            [Fact]
            public void Asks_again_out_of_a_world()
            {
                var step = OurCharacterRules.Take(Answer with
                {
                    InWorld = false,
                });

                step.ShouldBe(LoadStep.AskAgain);
            }
        }

        public class Applies
        {
            [Theory]
            [InlineData(CharacterKind.Own, true)]
            [InlineData(CharacterKind.OwnBesideHost, true)]
            [InlineData(CharacterKind.New, false)]
            [InlineData(CharacterKind.HostCopy, false)]
            [InlineData(CharacterKind.Dead, false)]
            public void Applies_our_own_record(CharacterKind kind, bool does)
            {
                OurCharacterRules.Applies(kind).ShouldBe(does);
            }
        }

        public class OffMap
        {
            [Fact]
            public void Moves_our_own_off_the_map()
            {
                var off = OurCharacterRules.OffMap(Own with { OnMap = false });

                off.ShouldBeTrue();
            }

            [Fact]
            public void Keeps_our_own_on_the_map()
            {
                OurCharacterRules.OffMap(Own).ShouldBeFalse();
            }

            [Fact]
            public void Keeps_our_own_with_a_zero_spawn()
            {
                var off = OurCharacterRules.OffMap(Own with
                {
                    OnMap = false,
                    Spawn = new Vec3(0f, 0f, 0f),
                });

                off.ShouldBeFalse();
            }

            [Fact]
            public void Keeps_another_chapters_out_of_it()
            {
                var off = OurCharacterRules.OffMap(Own with
                {
                    Kind = CharacterKind.OwnBesideHost,
                    OnMap = false,
                });

                off.ShouldBeFalse();
            }
        }

        public class Stand
        {
            [Fact]
            public void Keeps_our_own_where_it_was_saved()
            {
                OurCharacterRules.Stand(Own).ShouldBe(StandStep.Stay);
            }

            [Fact]
            public void Puts_our_own_off_the_map_at_the_spawn()
            {
                var step = OurCharacterRules.Stand(Own with { OnMap = false });

                step.ShouldBe(StandStep.AtSpawn);
            }

            [Fact]
            public void Puts_another_chapters_at_the_spawn()
            {
                var step = OurCharacterRules.Stand(Own with
                {
                    Kind = CharacterKind.OwnBesideHost,
                });

                step.ShouldBe(StandStep.AtSpawn);
            }

            [Fact]
            public void Keeps_another_chapters_with_a_zero_spawn()
            {
                var step = OurCharacterRules.Stand(Own with
                {
                    Kind = CharacterKind.OwnBesideHost,
                    Spawn = new Vec3(0f, 0f, 0f),
                });

                step.ShouldBe(StandStep.Stay);
            }

            [Fact]
            public void Makes_a_new_one_fresh()
            {
                var step = OurCharacterRules.Stand(Own with
                {
                    Kind = CharacterKind.New,
                });

                step.ShouldBe(StandStep.Fresh);
            }

            [Fact]
            public void Makes_a_new_one_fresh_with_a_zero_spawn()
            {
                var step = OurCharacterRules.Stand(Own with
                {
                    Kind = CharacterKind.New,
                    Spawn = new Vec3(0f, 0f, 0f),
                });

                step.ShouldBe(StandStep.Fresh);
            }

            [Fact]
            public void Puts_a_copy_at_the_spawn()
            {
                var step = OurCharacterRules.Stand(Own with
                {
                    Kind = CharacterKind.HostCopy,
                });

                step.ShouldBe(StandStep.AtSpawn);
            }

            [Fact]
            public void Keeps_a_copy_with_a_zero_spawn()
            {
                var step = OurCharacterRules.Stand(Own with
                {
                    Kind = CharacterKind.HostCopy,
                    Spawn = new Vec3(0f, 0f, 0f),
                });

                step.ShouldBe(StandStep.Stay);
            }

            [Fact]
            public void Doesnt_make_a_new_one_fresh_in_the_prologue()
            {
                var step = OurCharacterRules.Stand(Own with
                {
                    Kind = CharacterKind.New,
                    Prologue = true,
                });

                step.ShouldBe(StandStep.AtSpawn);
            }

            [Fact]
            public void Keeps_a_new_one_in_the_prologue_with_a_zero_spawn()
            {
                var step = OurCharacterRules.Stand(Own with
                {
                    Kind = CharacterKind.New,
                    Prologue = true,
                    Spawn = new Vec3(0f, 0f, 0f),
                });

                step.ShouldBe(StandStep.Stay);
            }

            [Fact]
            public void Moves_our_own_off_the_map_in_the_prologue_too()
            {
                var step = OurCharacterRules.Stand(Own with
                {
                    Prologue = true,
                    OnMap = false,
                });

                step.ShouldBe(StandStep.AtSpawn);
            }
        }

        public class Note
        {
            [Theory]
            [InlineData(CharacterKind.Own, false, LoadNote.Own)]
            [InlineData(
                CharacterKind.OwnBesideHost,
                false,
                LoadNote.LastChapter)]
            [InlineData(CharacterKind.New, false, LoadNote.New)]
            [InlineData(CharacterKind.HostCopy, false, LoadNote.HostCopy)]
            [InlineData(CharacterKind.New, true, LoadNote.Prologue)]
            [InlineData(CharacterKind.HostCopy, true, LoadNote.Prologue)]
            [InlineData(CharacterKind.Own, true, LoadNote.Own)]
            public void Says_which_character_came(
                CharacterKind kind,
                bool prologue,
                LoadNote note)
            {
                var said = OurCharacterRules.Note(Own with
                {
                    Kind = kind,
                    Prologue = prologue,
                });

                said.ShouldBe(note);
            }
        }

        public class IsZero
        {
            [Theory]
            [InlineData(0f, true)]
            [InlineData(0.000005f, true)]
            [InlineData(0.00002f, false)]
            [InlineData(1f, false)]
            public void Compares_as_unity_does(float x, bool zero)
            {
                OurCharacterRules.IsZero(new Vec3(x, 0f, 0f)).ShouldBe(zero);
            }
        }

        public class NearestChunk
        {
            [Fact]
            public void Takes_the_larger_of_x_and_z()
            {
                var d = OurCharacterRules.NearestChunk(
                    Chunks,
                    100f,
                    new Vec2(30f, 40f));

                d.ShouldBe(40f);
            }

            [Fact]
            public void Takes_the_nearest_chunk()
            {
                var d = OurCharacterRules.NearestChunk(
                    Chunks,
                    100f,
                    new Vec2(900f, 10f));

                d.ShouldBe(100f);
            }

            [Fact]
            public void Doesnt_know_without_chunks()
            {
                var d = OurCharacterRules.NearestChunk(
                    new Vec2[0],
                    100f,
                    new Vec2(5000f, 0f));

                d.ShouldBe(0f);
            }

            [Fact]
            public void Doesnt_know_without_a_size()
            {
                var d = OurCharacterRules.NearestChunk(
                    Chunks,
                    0f,
                    new Vec2(5000f, 0f));

                d.ShouldBe(0f);
            }
        }

        public class OnMap
        {
            [Theory]
            [InlineData(60f, true)]
            [InlineData(60.01f, false)]
            public void Counts_within_six_tenths_of_a_chunk(float x, bool on)
            {
                var result = OurCharacterRules.OnMap(
                    Chunks,
                    100f,
                    new Vec2(x, 0f));

                result.ShouldBe(on);
            }

            [Fact]
            public void Counts_on_the_map_when_it_cant_be_told()
            {
                var result = OurCharacterRules.OnMap(
                    null,
                    0f,
                    new Vec2(9000f, 0f));

                result.ShouldBeTrue();
            }

            [Fact]
            public void Counts_off_the_map_with_a_negative_size()
            {
                var result = OurCharacterRules.OnMap(
                    Chunks,
                    -1f,
                    new Vec2(0f, 0f));

                result.ShouldBeFalse();
            }
        }

        public class Upgrades
        {
            [Theory]
            [InlineData(3, 1, 2, 0)]
            [InlineData(1, 3, 0, 2)]
            [InlineData(2, 2, 0, 0)]
            public void Applies_only_the_difference(
                int wanted,
                int have,
                int add,
                int takeBack)
            {
                OurCharacterRules.UpgradesToAdd(wanted, have).ShouldBe(add);
                OurCharacterRules.UpgradesToTakeBack(wanted, have)
                    .ShouldBe(takeBack);
            }

            [Fact]
            public void Pins_the_cost_of_one()
            {
                OurCharacterRules.UpgradeCost.ShouldBe(25f);
            }
        }

        public class Health
        {
            [Theory]
            [InlineData(80f, 100f, 80f)]
            [InlineData(150f, 100f, 100f)]
            public void Caps_health_at_max(float kept, float max, float health)
            {
                OurCharacterRules.Health(kept, max).ShouldBe(health);
            }
        }

        public class SlotChange
        {
            [Theory]
            [InlineData(8, 10, 2)]
            [InlineData(10, 8, -2)]
            [InlineData(8, 0, 0)]
            [InlineData(8, -1, 0)]
            public void Matches_the_records_slots(int have, int count, int by)
            {
                OurCharacterRules.SlotChange(have, count).ShouldBe(by);
            }
        }

        public class Moves
        {
            [Theory]
            [InlineData(0.99f, false)]
            [InlineData(1f, true)]
            public void Moves_one_unit_or_more(float x, bool moves)
            {
                var result = OurCharacterRules.Moves(
                    new Vec3(0f, 0f, 0f),
                    new Vec3(x, 0f, 0f));

                result.ShouldBe(moves);
            }
        }
    }
}
