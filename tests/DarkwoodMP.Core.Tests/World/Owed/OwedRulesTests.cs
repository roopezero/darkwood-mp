using DarkwoodMP.World.Owed;

namespace DarkwoodMP.Tests.World.Owed
{
    public class OwedRulesTests
    {
        /// The second trigger of an object with save id 42.
        static readonly TriggerNameFacts Saved = new TriggerNameFacts
        {
            Saved = true,
            SaveId = 42,
            Index = 1,
            Scene = "Forest",
            Name = "Hole",
            X = 1200.4f,
            Z = 830.6f,
        };

        /// The same trigger on an object with no save.
        static readonly TriggerNameFacts Placed = Saved with
        {
            Saved = false,
            SaveId = 0,
        };

        /// A joiner owed a trigger of the shared world.
        static readonly OwedFacts Joiner = new OwedFacts
        {
            Trigger = true,
            Active = true,
            HasObject = true,
        };

        /// The host, owed the trigger by its record.
        static readonly OwedFacts Host = new OwedFacts
        {
            Trigger = true,
            Active = true,
            Host = true,
            HostOwed = true,
        };

        /// The host saving its own game, with a record read for the same
        /// profile.
        static readonly RecordSaveFacts Saving = new RecordSaveFacts
        {
            Loaded = true,
            Profile = "saves/prof1",
            RecordProfile = "saves/prof1",
        };

        /// A record read for this profile and this world.
        static readonly RecordReadFacts Current = new RecordReadFacts
        {
            Loaded = true,
            Profile = "saves/prof1",
            RecordProfile = "saves/prof1",
            Scene = 5,
            OwedScene = 5,
        };

        public class Limits
        {
            [Fact]
            public void Pins_the_file_name()
            {
                OwedRules.FileName.ShouldBe("darkwoodmp_later.json");
            }
        }

        public class IdOf
        {
            [Fact]
            public void Names_a_saved_object_by_its_save_id()
            {
                OwedRules.IdOf(Saved).ShouldBe("s42#1");
            }

            [Fact]
            public void Names_an_unsaved_object_by_scene_name_and_place()
            {
                OwedRules.IdOf(Placed).ShouldBe("Forest/Hole@1200,831#1");
            }

            [Fact]
            public void Names_a_save_id_of_0_by_its_place()
            {
                var trigger = Saved with { SaveId = 0 };

                OwedRules.IdOf(trigger).ShouldBe("Forest/Hole@1200,831#1");
            }

            [Fact]
            public void Names_a_trigger_not_in_its_list_minus_one()
            {
                var trigger = Saved with { Index = -1 };

                OwedRules.IdOf(trigger).ShouldBe("s42#-1");
            }

            [Fact]
            public void Writes_a_negative_place_with_its_sign()
            {
                var trigger = Placed with { X = -12.6f, Z = -3f };

                OwedRules.IdOf(trigger).ShouldBe("Forest/Hole@-13,-3#1");
            }
        }

        public class Owed
        {
            [Fact]
            public void Owes_a_joiner_a_trigger_it_hasnt_had()
            {
                OwedRules.Owed(Joiner).ShouldBeTrue();
            }

            [Fact]
            public void Owes_a_joiner_nothing_it_had_here()
            {
                var owed = Joiner with { HadHere = true };

                OwedRules.Owed(owed).ShouldBeFalse();
            }

            [Fact]
            public void Owes_a_joiner_nothing_of_its_own_place()
            {
                var owed = Joiner with { OursAlone = true };

                OwedRules.Owed(owed).ShouldBeFalse();
            }

            [Fact]
            public void Owes_a_joiner_nothing_without_the_object()
            {
                var owed = Joiner with { HasObject = false };

                OwedRules.Owed(owed).ShouldBeFalse();
            }

            [Fact]
            public void Owes_the_host_what_its_record_says()
            {
                OwedRules.Owed(Host).ShouldBeTrue();
            }

            [Fact]
            public void Owes_the_host_nothing_its_record_doesnt()
            {
                var owed = Host with { HostOwed = false, HasObject = true };

                OwedRules.Owed(owed).ShouldBeFalse();
            }

            [Fact]
            public void Owes_nothing_without_an_active_session()
            {
                var owed = Joiner with { Active = false };

                OwedRules.Owed(owed).ShouldBeFalse();
            }

            [Fact]
            public void Owes_nothing_without_a_trigger()
            {
                var owed = Host with { Trigger = false };

                OwedRules.Owed(owed).ShouldBeFalse();
            }
        }

        public class UsesKey
        {
            [Theory]
            [InlineData("abc", true)]
            [InlineData("", false)]
            [InlineData(null, false)]
            public void Uses_a_key_that_isnt_empty(string key, bool uses)
            {
                OwedRules.UsesKey(key).ShouldBe(uses);
            }
        }

        public class Unkeyed
        {
            [Fact]
            public void Names_the_player_by_a_hash_sign_and_their_id()
            {
                OwedRules.Unkeyed(76561198000000000UL)
                    .ShouldBe("#76561198000000000");
            }
        }

        public class Writes
        {
            [Fact]
            public void Writes_the_hosts_record()
            {
                OwedRules.Writes(Saving).ShouldBeTrue();
            }

            [Fact]
            public void Skips_a_joiners_snapshot()
            {
                var save = Saving with { Snapshotting = true };

                OwedRules.Writes(save).ShouldBeFalse();
            }

            [Fact]
            public void Skips_a_client()
            {
                var save = Saving with { Client = true };

                OwedRules.Writes(save).ShouldBeFalse();
            }

            [Fact]
            public void Skips_a_record_never_read()
            {
                var save = Saving with { Loaded = false };

                OwedRules.Writes(save).ShouldBeFalse();
            }

            [Fact]
            public void Skips_without_a_profile()
            {
                var save = Saving with { Profile = null };

                OwedRules.Writes(save).ShouldBeFalse();
            }

            [Fact]
            public void Skips_without_a_profile_for_a_record_read_without()
            {
                var save = Saving with { Profile = null, RecordProfile = null };

                OwedRules.Writes(save).ShouldBeFalse();
            }

            [Fact]
            public void Skips_another_profile()
            {
                var save = Saving with { Profile = "saves/prof2" };

                OwedRules.Writes(save).ShouldBeFalse();
            }

            [Fact]
            public void Skips_an_empty_record()
            {
                var save = Saving with { Empty = true };

                OwedRules.Writes(save).ShouldBeFalse();
            }
        }

        public class Reads
        {
            [Fact]
            public void Reads_nothing_for_the_same_world()
            {
                OwedRules.Reads(Current).ShouldBe(RecordRead.None);
            }

            [Fact]
            public void Reads_it_whole_the_first_time()
            {
                var record = Current with { Loaded = false };

                OwedRules.Reads(record).ShouldBe(RecordRead.Whole);
            }

            [Fact]
            public void Reads_it_whole_for_another_profile()
            {
                var record = Current with { Profile = "saves/prof2" };

                OwedRules.Reads(record).ShouldBe(RecordRead.Whole);
            }

            [Fact]
            public void Reads_it_whole_when_the_profile_goes()
            {
                var record = Current with { Profile = null };

                OwedRules.Reads(record).ShouldBe(RecordRead.Whole);
            }

            [Fact]
            public void Reads_the_hosts_part_at_a_new_world()
            {
                var record = Current with { Scene = 6 };

                OwedRules.Reads(record).ShouldBe(RecordRead.HostOwed);
            }

            [Fact]
            public void Reads_it_whole_for_another_profile_and_world()
            {
                var record = Current with { Profile = null, Scene = 6 };

                OwedRules.Reads(record).ShouldBe(RecordRead.Whole);
            }
        }
    }
}
