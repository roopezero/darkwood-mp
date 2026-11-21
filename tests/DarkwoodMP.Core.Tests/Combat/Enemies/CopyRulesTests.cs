using DarkwoodMP.Combat.Enemies;

namespace DarkwoodMP.Tests.Combat.Enemies
{
    public class CopyRulesTests
    {
        /// The host's dog under save id 120, which our save has too.
        static readonly SavedFacts SavedDog = new SavedFacts
        {
            SaveId = 120,
            Limit = 1000000,
            Found = true,
            HasCharacter = true,
            Kind = "dog",
            HostKind = "dog",
        };

        /// A copy we show, the host stopping sending it.
        static readonly DespawnFacts Shown = new DespawnFacts
        {
            Copy = true,
            Exists = true,
        };

        /// A corpse we keep, gone from the host's world.
        static readonly DespawnFacts Kept = new DespawnFacts
        {
            Corpse = true,
            Exists = true,
        };

        /// A sound the host's living enemy made.
        static readonly SoundFacts Bark = new SoundFacts
        {
            Copy = true,
            Sounds = true,
        };

        public class Limits
        {
            [Fact]
            public void Pins_the_predicted_blood_time()
            {
                var limit = CopyRules.PredictedBlood;

                limit.ShouldBe(1f);
            }

            [Fact]
            public void Pins_the_blood_splats()
            {
                var splats = new[] { CopyRules.WaterBlood, CopyRules.Blood };

                splats.ShouldBe(new[]
                {
                    "FX/Bloodsplats/Shotsplat",
                    "FX/Bloodsplats/Shotsplat_stay",
                });
            }
        }

        public class FromSave
        {
            [Fact]
            public void Uses_our_copy_from_the_save()
            {
                var copy = CopyRules.FromSave(SavedDog);

                copy.ShouldBe(SavedCopy.Use);
            }

            [Theory]
            [InlineData(0)]
            [InlineData(-5)]
            public void Takes_no_id_below_one(int id)
            {
                var saved = SavedDog with { SaveId = id };

                var copy = CopyRules.FromSave(saved);

                copy.ShouldBe(SavedCopy.None);
            }

            [Fact]
            public void Takes_an_id_just_under_the_limit()
            {
                var saved = SavedDog with { SaveId = 999999 };

                var copy = CopyRules.FromSave(saved);

                copy.ShouldBe(SavedCopy.Use);
            }

            [Fact]
            public void Takes_no_id_at_the_limit()
            {
                var saved = SavedDog with { SaveId = 1000000 };

                var copy = CopyRules.FromSave(saved);

                copy.ShouldBe(SavedCopy.None);
            }

            [Fact]
            public void Finds_none_not_in_the_save()
            {
                var saved = SavedDog with { Found = false };

                var copy = CopyRules.FromSave(saved);

                copy.ShouldBe(SavedCopy.None);
            }

            [Fact]
            public void Finds_none_that_is_not_a_character()
            {
                var saved = SavedDog with { HasCharacter = false };

                var copy = CopyRules.FromSave(saved);

                copy.ShouldBe(SavedCopy.None);
            }

            [Fact]
            public void Leaves_one_another_id_drives()
            {
                var saved = SavedDog with { Driven = true };

                var copy = CopyRules.FromSave(saved);

                copy.ShouldBe(SavedCopy.Driven);
            }

            [Fact]
            public void Leaves_one_of_another_kind()
            {
                var saved = SavedDog with { Kind = "savage" };

                var copy = CopyRules.FromSave(saved);

                copy.ShouldBe(SavedCopy.OtherKind);
            }

            [Fact]
            public void Says_driven_before_another_kind()
            {
                var saved = SavedDog with { Driven = true, Kind = "savage" };

                var copy = CopyRules.FromSave(saved);

                copy.ShouldBe(SavedCopy.Driven);
            }
        }

        public class Prefabs
        {
            [Fact]
            public void Tries_the_prefab_then_the_kind_under_characters()
            {
                var paths = CopyRules.Prefabs("Characters/dog_village", "dog");

                paths.ShouldBe(new[]
                {
                    "Prefabs/Characters/dog_village",
                    "Prefabs/Characters/dog",
                });
            }
        }

        public class Despawn
        {
            [Fact]
            public void Releases_a_copy_the_host_stopped_sending()
            {
                var step = CopyRules.Despawn(Shown);

                step.ShouldBe(DespawnStep.Release);
            }

            [Fact]
            public void Keeps_a_copy_as_a_corpse()
            {
                var d = Shown with { KeepCorpse = true };

                var step = CopyRules.Despawn(d);

                step.ShouldBe(DespawnStep.KeepCorpse);
            }

            [Fact]
            public void Only_forgets_a_copy_whose_character_is_gone()
            {
                var d = Shown with { Exists = false, KeepCorpse = true };

                var step = CopyRules.Despawn(d);

                step.ShouldBe(DespawnStep.ForgetCopy);
            }

            [Fact]
            public void Destroys_a_kept_corpse_the_host_has_no_more()
            {
                var step = CopyRules.Despawn(Kept);

                step.ShouldBe(DespawnStep.DestroyCorpse);
            }

            [Fact]
            public void Only_forgets_a_kept_corpse_whose_character_is_gone()
            {
                var d = Kept with { Exists = false };

                var step = CopyRules.Despawn(d);

                step.ShouldBe(DespawnStep.ForgetCorpse);
            }

            [Fact]
            public void Ignores_keeping_a_corpse_it_keeps_already()
            {
                var d = Kept with { KeepCorpse = true };

                var step = CopyRules.Despawn(d);

                step.ShouldBe(DespawnStep.Ignore);
            }

            [Fact]
            public void Ignores_an_id_it_never_had()
            {
                var d = new DespawnFacts { Exists = true };

                var step = CopyRules.Despawn(d);

                step.ShouldBe(DespawnStep.Ignore);
            }
        }

        public class Release
        {
            [Theory]
            [InlineData(true, ReleaseStep.Destroy)]
            [InlineData(false, ReleaseStep.Hide)]
            public void Destroys_one_made_here_and_hides_ours(
                bool madeHere,
                ReleaseStep release)
            {
                var step = CopyRules.Release(madeHere);

                step.ShouldBe(release);
            }
        }

        public class Sound
        {
            [Fact]
            public void Plays_a_sound_at_the_copy()
            {
                var step = CopyRules.Sound(Bark);

                step.ShouldBe(SoundStep.Play);
            }

            [Fact]
            public void Plays_one_of_its_loops_as_its_loop()
            {
                var s = Bark with { IsLoop = true };

                var step = CopyRules.Sound(s);

                step.ShouldBe(SoundStep.Loop);
            }

            [Fact]
            public void Stops_its_loop_for_an_empty_sound()
            {
                var s = Bark with { Empty = true };

                var step = CopyRules.Sound(s);

                step.ShouldBe(SoundStep.StopLoop);
            }

            [Fact]
            public void Ignores_an_empty_sound_with_no_character_sounds()
            {
                var s = Bark with { Empty = true, Sounds = false };

                var step = CopyRules.Sound(s);

                step.ShouldBe(SoundStep.Ignore);
            }

            [Fact]
            public void Plays_a_sound_with_no_character_sounds()
            {
                var s = Bark with { Sounds = false };

                var step = CopyRules.Sound(s);

                step.ShouldBe(SoundStep.Play);
            }

            [Fact]
            public void Ignores_a_sound_for_no_copy()
            {
                var s = Bark with { Copy = false };

                var step = CopyRules.Sound(s);

                step.ShouldBe(SoundStep.Ignore);
            }

            [Fact]
            public void Ignores_a_late_sound_once_it_is_dead()
            {
                var s = Bark with { Dead = true, IsLoop = true };

                var step = CopyRules.Sound(s);

                step.ShouldBe(SoundStep.Ignore);
            }

            [Fact]
            public void Plays_the_death_cry_once_it_is_dead()
            {
                var s = Bark with { Dead = true, DeathCry = true };

                var step = CopyRules.Sound(s);

                step.ShouldBe(SoundStep.Play);
            }

            [Fact]
            public void Ignores_any_sound_of_a_dead_one_with_no_sounds()
            {
                var s = Bark with
                {
                    Dead = true,
                    DeathCry = true,
                    Sounds = false,
                };

                var step = CopyRules.Sound(s);

                step.ShouldBe(SoundStep.Ignore);
            }
        }

        public class ShowsBlood
        {
            [Theory]
            [InlineData(false, 0f, true)]
            [InlineData(true, 0.99f, false)]
            [InlineData(true, 1f, true)]
            public void Shows_the_host_blood_unless_ours_just_showed(
                bool predicted,
                float since,
                bool shows)
            {
                var result = CopyRules.ShowsBlood(
                    predicted,
                    new Seconds(since));

                result.ShouldBe(shows);
            }
        }

        public class BloodFor
        {
            [Theory]
            [InlineData(true, "FX/Bloodsplats/Shotsplat")]
            [InlineData(false, "FX/Bloodsplats/Shotsplat_stay")]
            public void Picks_the_splat_for_water(bool water, string splat)
            {
                var result = CopyRules.BloodFor(water);

                result.ShouldBe(splat);
            }
        }

        public class ShowsBar
        {
            [Theory]
            [InlineData(0f, false)]
            [InlineData(0.5f, true)]
            public void Shows_the_bar_for_a_hit_that_did_damage(
                float damage,
                bool shows)
            {
                var result = CopyRules.ShowsBar(damage);

                result.ShouldBe(shows);
            }
        }

        public class Armor
        {
            [Theory]
            [InlineData(true, false, false, ArmorStep.Add)]
            [InlineData(true, true, true, ArmorStep.None)]
            [InlineData(false, true, true, ArmorStep.Die)]
            [InlineData(false, true, false, ArmorStep.Destroy)]
            [InlineData(false, false, false, ArmorStep.None)]
            public void Matches_the_host_armour(
                bool on,
                bool has,
                bool started,
                ArmorStep armor)
            {
                var step = CopyRules.Armor(on, has, started);

                step.ShouldBe(armor);
            }
        }
    }
}
