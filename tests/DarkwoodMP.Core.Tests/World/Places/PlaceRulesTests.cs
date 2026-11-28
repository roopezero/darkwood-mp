using DarkwoodMP.World.Places;

namespace DarkwoodMP.Tests.World.Places
{
    public class PlaceRulesTests
    {
        /// Our player in the location whose grid is "Bunker".
        static readonly WhereFacts InBunker = new WhereFacts
        {
            Dreaming = false,
            InLocation = true,
            Grid = "Bunker",
        };

        /// Both of us away, in the same location.
        static readonly SameFacts BothInBunker = new SameFacts
        {
            Away = true,
            Place = "Bunker",
            MeAway = true,
            DreamShared = false,
            Current = "Bunker",
        };

        /// A client in a location the host placed.
        static readonly HereFacts Placed = new HereFacts
        {
            Dreaming = false,
            SharedDream = false,
            DreamGivenUp = false,
            InLocation = true,
            LocationGivenUp = false,
        };

        /// Dreaming the shared dream "Dream1".
        static readonly DreamFacts SharedOne = new DreamFacts
        {
            Dreaming = true,
            HasLocation = true,
            Shared = "Dream1",
            HasPreset = true,
            Preset = "Dream1",
        };

        public class Limits
        {
            [Fact]
            public void Pins_the_names_and_the_location_reach()
            {
                PlaceRules.DreamPrefix.ShouldBe("dream:");
                PlaceRules.Unknown.ShouldBe("?");
                PlaceRules.LocationReach.ShouldBe(15000f);
            }
        }

        public class Current
        {
            [Fact]
            public void Names_the_locations_grid()
            {
                PlaceRules.Current(InBunker).ShouldBe("Bunker");
            }

            [Fact]
            public void Names_nothing_in_the_shared_world()
            {
                var where = InBunker with { InLocation = false };

                PlaceRules.Current(where).ShouldBe("");
            }

            [Fact]
            public void Prefixes_a_dream()
            {
                var where = InBunker with { Dreaming = true, Grid = "Dream1" };

                PlaceRules.Current(where).ShouldBe("dream:Dream1");
            }

            [Fact]
            public void Prefixes_a_dream_out_of_a_location()
            {
                var where = InBunker with
                {
                    Dreaming = true,
                    InLocation = false,
                    Grid = "Dream1",
                };

                PlaceRules.Current(where).ShouldBe("dream:Dream1");
            }

            [Fact]
            public void Names_a_location_with_no_grid_unknown()
            {
                var where = InBunker with { Grid = null };

                PlaceRules.Current(where).ShouldBe("?");
            }

            [Fact]
            public void Names_a_dream_with_no_grid_unknown()
            {
                var where = InBunker with { Dreaming = true, Grid = null };

                PlaceRules.Current(where).ShouldBe("dream:?");
            }
        }

        public class SamePlace
        {
            [Fact]
            public void Finds_two_players_in_one_location()
            {
                PlaceRules.SamePlace(BothInBunker).ShouldBeTrue();
            }

            [Fact]
            public void Finds_two_players_in_the_world()
            {
                var same = BothInBunker with { Away = false, MeAway = false };

                PlaceRules.SamePlace(same).ShouldBeTrue();
            }

            [Fact]
            public void Parts_one_away_from_one_in_the_world()
            {
                var same = BothInBunker with { MeAway = false };

                PlaceRules.SamePlace(same).ShouldBeFalse();
            }

            [Fact]
            public void Parts_two_locations()
            {
                var same = BothInBunker with { Current = "Church" };

                PlaceRules.SamePlace(same).ShouldBeFalse();
            }

            [Theory]
            [InlineData("")]
            [InlineData(null)]
            [InlineData("?")]
            public void Parts_a_place_unknown(string place)
            {
                var same = BothInBunker with { Place = place, Current = place };

                PlaceRules.SamePlace(same).ShouldBeFalse();
            }

            [Fact]
            public void Parts_someone_elses_own_dream()
            {
                var same = BothInBunker with
                {
                    Place = "dream:Dream1",
                    Current = "dream:Dream1",
                };

                PlaceRules.SamePlace(same).ShouldBeFalse();
            }

            [Fact]
            public void Finds_two_players_in_the_shared_dream()
            {
                var same = BothInBunker with
                {
                    Place = "dream:Dream1",
                    Current = "dream:Dream1",
                    DreamShared = true,
                };

                PlaceRules.SamePlace(same).ShouldBeTrue();
            }
        }

        public class HostRunsHere
        {
            [Fact]
            public void Runs_a_location_it_placed()
            {
                PlaceRules.HostRunsHere(Placed).ShouldBeTrue();
            }

            [Fact]
            public void Leaves_us_a_location_we_gave_up_on()
            {
                var here = Placed with { LocationGivenUp = true };

                PlaceRules.HostRunsHere(here).ShouldBeFalse();
            }

            [Fact]
            public void Runs_the_shared_world()
            {
                var here = Placed with
                {
                    InLocation = false,
                    LocationGivenUp = true,
                };

                PlaceRules.HostRunsHere(here).ShouldBeTrue();
            }

            [Fact]
            public void Runs_the_shared_dream()
            {
                var here = Placed with { Dreaming = true, SharedDream = true };

                PlaceRules.HostRunsHere(here).ShouldBeTrue();
            }

            [Fact]
            public void Leaves_us_our_own_dream()
            {
                var here = Placed with { Dreaming = true };

                PlaceRules.HostRunsHere(here).ShouldBeFalse();
            }

            [Fact]
            public void Leaves_us_a_shared_dream_we_gave_up_on()
            {
                var here = Placed with
                {
                    Dreaming = true,
                    SharedDream = true,
                    DreamGivenUp = true,
                };

                PlaceRules.HostRunsHere(here).ShouldBeFalse();
            }
        }

        public class Kind
        {
            [Theory]
            [InlineData(null, PlaceKind.None)]
            [InlineData("", PlaceKind.None)]
            [InlineData("?", PlaceKind.None)]
            [InlineData("dream:Dream1", PlaceKind.Dream)]
            [InlineData("Bunker", PlaceKind.Location)]
            public void Reads_what_a_place_names(string place, PlaceKind kind)
            {
                PlaceRules.Kind(place).ShouldBe(kind);
            }
        }

        public class RunsDream
        {
            [Theory]
            [InlineData(true, "dream:Dream1", "dream:Dream1", true)]
            [InlineData(false, "dream:Dream1", "dream:Dream1", false)]
            [InlineData(true, "dream:Dream1", "dream:Dream2", false)]
            public void Runs_the_shared_dream_we_dream_too(
                bool shared,
                string place,
                string ours,
                bool runs)
            {
                PlaceRules.RunsDream(shared, place, ours).ShouldBe(runs);
            }
        }

        public class Matches
        {
            [Theory]
            [InlineData("Bunker_1", "Bunker", "Bunker_1", true)]
            [InlineData("Bunker_1", "Bunker", "Bunker", true)]
            [InlineData("Bunker_1", "Bunker", "Church", false)]
            public void Matches_by_key_or_true_name(
                string key,
                string trueKey,
                string place,
                bool matches)
            {
                PlaceRules.Matches(key, trueKey, place).ShouldBe(matches);
            }
        }

        public class InReach
        {
            static readonly Vec3 Location = new Vec3(100000f, 0f, 0f);

            [Fact]
            public void Finds_a_joiner_just_inside()
            {
                var joiner = new Vec3(100000f, 0f, 14999f);

                PlaceRules.InReach(joiner, Location).ShouldBeTrue();
            }

            [Fact]
            public void Leaves_out_a_joiner_exactly_at_the_reach()
            {
                var joiner = new Vec3(109000f, 0f, 12000f);

                PlaceRules.InReach(joiner, Location).ShouldBeFalse();
            }

            [Fact]
            public void Leaves_out_the_height()
            {
                var joiner = new Vec3(100000f, 90000f, 0f);

                PlaceRules.InReach(joiner, Location).ShouldBeTrue();
            }
        }

        public class IsSharedDream
        {
            [Fact]
            public void Finds_the_shared_dream()
            {
                PlaceRules.IsSharedDream(SharedOne).ShouldBeTrue();
            }

            [Fact]
            public void Leaves_out_another_dream()
            {
                var dream = SharedOne with { Preset = "Dream2" };

                PlaceRules.IsSharedDream(dream).ShouldBeFalse();
            }

            [Fact]
            public void Leaves_out_a_dream_with_no_preset()
            {
                var dream = SharedOne with { HasPreset = false };

                PlaceRules.IsSharedDream(dream).ShouldBeFalse();
            }

            [Fact]
            public void Leaves_out_a_dream_with_none_shared()
            {
                var dream = SharedOne with { Shared = null, Preset = null };

                PlaceRules.IsSharedDream(dream).ShouldBeFalse();
            }

            [Fact]
            public void Leaves_out_a_dream_with_no_location()
            {
                var dream = SharedOne with { HasLocation = false };

                PlaceRules.IsSharedDream(dream).ShouldBeFalse();
            }

            [Fact]
            public void Leaves_out_a_player_awake()
            {
                var dream = SharedOne with { Dreaming = false };

                PlaceRules.IsSharedDream(dream).ShouldBeFalse();
            }
        }

        public class IsLaterPart
        {
            /// In the epilogue's flat, its first part's name kept as shared.
            static readonly DreamFacts InFlat = SharedOne with
            {
                Epilogue = true,
                Preset = "Dream3",
            };

            [Fact]
            public void Finds_a_part_after_the_first()
            {
                PlaceRules.IsLaterPart(InFlat).ShouldBeTrue();
            }

            [Fact]
            public void Leaves_out_the_first_part()
            {
                var dream = InFlat with { Preset = "Dream1" };

                PlaceRules.IsLaterPart(dream).ShouldBeFalse();
            }

            [Fact]
            public void Leaves_out_a_shared_dream_that_is_not_the_epilogue()
            {
                var dream = InFlat with { Epilogue = false };

                PlaceRules.IsLaterPart(dream).ShouldBeFalse();
            }

            [Fact]
            public void Leaves_out_a_player_awake()
            {
                var dream = InFlat with { Dreaming = false };

                PlaceRules.IsLaterPart(dream).ShouldBeFalse();
            }

            [Fact]
            public void Leaves_out_a_dream_with_no_location()
            {
                var dream = InFlat with { HasLocation = false };

                PlaceRules.IsLaterPart(dream).ShouldBeFalse();
            }

            [Fact]
            public void Leaves_out_a_dream_with_no_preset()
            {
                var dream = InFlat with { HasPreset = false };

                PlaceRules.IsLaterPart(dream).ShouldBeFalse();
            }

            [Fact]
            public void Leaves_out_a_dream_with_none_shared()
            {
                var dream = InFlat with { Shared = null };

                PlaceRules.IsLaterPart(dream).ShouldBeFalse();
            }
        }

        public class OursNow
        {
            [Theory]
            [InlineData(true, "Bunker", "Bunker", true)]
            [InlineData(false, "Bunker", "Bunker", false)]
            [InlineData(true, "Bunker", "Church", false)]
            public void Finds_our_player_in_it_by_true_name(
                bool inside,
                string ours,
                string theirs,
                bool now)
            {
                PlaceRules.OursNow(inside, ours, theirs).ShouldBe(now);
            }
        }
    }
}
