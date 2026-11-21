using DarkwoodMP.World;

namespace DarkwoodMP.Tests.World
{
    public partial class ObjRefRulesTests
    {
        const int Hash = 0x123456;

        static readonly Vec2 Pos = new Vec2(10f, 20f);

        /// An object no table knows, which goes by its name and position.
        static readonly NamingFacts Plain = new NamingFacts
        {
            Exists = true,
            NameHash = Hash,
            Pos = Pos,
            SendLimit = 1000,
        };

        static readonly ObjName PlainName =
            new ObjName(ObjKind.Placed, ObjRefRules.PlacedKey(Hash, Pos));

        static float RoundedX(float x) =>
            ObjRefRules.Unpack(
                ObjRefRules.PlacedKey(Hash, new Vec2(x, 0f))).Spot.X;

        public class Limits
        {
            [Fact]
            public void Pins_the_placed_limits()
            {
                var limits = new[]
                {
                    ObjRefRules.PlacedSlack,
                    ObjRefRules.PlacedRescan,
                    ObjRefRules.NeverScanned,
                    ObjRefRules.OnSpot,
                };

                limits.ShouldBe(new[] { 50f, 0.25f, -999f, 1f });
            }
        }

        public class Of
        {
            [Fact]
            public void Names_by_name_and_position_last()
            {
                var name = ObjRefRules.Of(Plain);

                name.ShouldBe(PlainName);
            }

            [Fact]
            public void Names_nothing_that_is_gone()
            {
                var obj = Plain with { Exists = false, Spawned = true };

                var name = ObjRefRules.Of(obj);

                name.ShouldBe(default(ObjName));
            }

            [Fact]
            public void Names_a_spawned_object_by_its_id_first()
            {
                var obj = Plain with
                {
                    Spawned = true,
                    SpawnedId = 5,
                    Enemy = true,
                    SaveId = 6,
                    Shared = true,
                    Pinned = true,
                };

                var name = ObjRefRules.Of(obj);

                name.ShouldBe(new ObjName(ObjKind.Spawned, 5));
            }

            [Fact]
            public void Names_an_enemy_before_its_save_id()
            {
                var obj = Plain with { Enemy = true, EnemyId = 9, SaveId = 6 };

                var name = ObjRefRules.Of(obj);

                name.ShouldBe(new ObjName(ObjKind.Enemy, 9));
            }

            [Theory]
            [InlineData(1, true)]
            [InlineData(999, true)]
            [InlineData(1000, false)]
            [InlineData(0, false)]
            [InlineData(-4, false)]
            public void Names_by_save_id_only_from_1_to_below_the_limit(
                int id,
                bool saved)
            {
                var obj = Plain with { SaveId = id };

                var name = ObjRefRules.Of(obj);

                (name.Kind == ObjKind.Saved).ShouldBe(saved);
            }

            [Fact]
            public void Names_by_save_id_before_the_host_id()
            {
                var obj = Plain with { SaveId = 6, Shared = true };

                var name = ObjRefRules.Of(obj);

                name.ShouldBe(new ObjName(ObjKind.Saved, 6));
            }

            [Fact]
            public void Names_by_the_host_id_before_the_pinned_key()
            {
                var obj = Plain with
                {
                    Shared = true,
                    SharedId = 7,
                    Pinned = true,
                    PinnedKey = 8,
                };

                var name = ObjRefRules.Of(obj);

                name.ShouldBe(new ObjName(ObjKind.Spawned, 7));
            }

            [Fact]
            public void Names_by_the_pinned_key_before_where_it_stands()
            {
                var obj = Plain with { Pinned = true, PinnedKey = 8 };

                var name = ObjRefRules.Of(obj);

                name.ShouldBe(new ObjName(ObjKind.Placed, 8));
            }
        }

        public class PlacedKey
        {
            [Fact]
            public void Packs_the_name_then_x_then_z()
            {
                var key = ObjRefRules.PlacedKey(0xABCDEF, new Vec2(0f, 0f));

                key.ShouldBe(unchecked((long)0xABCDEF8000080000UL));
            }

            [Fact]
            public void Keeps_a_spot_below_0_within_its_20_bits()
            {
                var key = ObjRefRules.PlacedKey(0xABCDEE, new Vec2(-1f, -2f));

                key.ShouldBe(unchecked((long)0xABCDEE7FFFF7FFFEUL));
            }

            [Fact]
            public void Keeps_24_bits_of_the_name()
            {
                var key = ObjRefRules.PlacedKey(-1, Pos);

                key.ShouldBe(ObjRefRules.PlacedKey(0xFFFFFF, Pos));
            }

            [Theory]
            [InlineData(0.5f, 0f)]
            [InlineData(1.5f, 2f)]
            [InlineData(2.5f, 2f)]
            [InlineData(-0.5f, 0f)]
            [InlineData(-1.5f, -2f)]
            [InlineData(10.4f, 10f)]
            public void Rounds_halves_to_even(float x, float rounded)
            {
                var spot = RoundedX(x);

                spot.ShouldBe(rounded);
            }

            [Theory]
            [InlineData(524287f, 524287f)]
            [InlineData(524288f, -524288f)]
            [InlineData(-524288f, -524288f)]
            [InlineData(-524289f, 524287f)]
            public void Wraps_past_half_the_range(float x, float wrapped)
            {
                var spot = RoundedX(x);

                spot.ShouldBe(wrapped);
            }
        }

        public class NameKey
        {
            [Fact]
            public void Keeps_the_low_24_bits()
            {
                var key = ObjRefRules.NameKey(0x7ABCDEF1);

                key.ShouldBe(0xBCDEF1);
            }
        }

        public class Unpack
        {
            [Fact]
            public void Gives_back_the_name_and_the_spot()
            {
                var spot = ObjRefRules.Unpack(PlainName.Id);

                spot.Name.ShouldBe(Hash);
                spot.Spot.ShouldBe(Pos);
            }

            [Fact]
            public void Gives_back_a_name_with_its_top_bit_set()
            {
                var key = ObjRefRules.PlacedKey(0xFFFFFF, new Vec2(3f, -4f));

                var spot = ObjRefRules.Unpack(key);

                spot.Name.ShouldBe(0xFFFFFF);
                spot.Spot.ShouldBe(new Vec2(3f, -4f));
            }
        }

        public class Nearest
        {
            static readonly Vec2 Spot = new Vec2(0f, 0f);

            static PlacedMatch Of(params Vec2[] candidates) =>
                ObjRefRules.Nearest(Spot, candidates);

            [Fact]
            public void Finds_nothing_among_no_candidates()
            {
                var match = Of();

                match.Found.ShouldBeFalse();
            }

            [Fact]
            public void Takes_the_nearest()
            {
                var match = Of(
                    new Vec2(30f, 0f),
                    new Vec2(10f, 0f),
                    new Vec2(20f, 0f));

                match.Index.ShouldBe(1);
            }

            [Fact]
            public void Measures_from_the_named_spot()
            {
                var spot = new Vec2(100f, 200f);
                var candidates = new[]
                {
                    new Vec2(140f, 100f),
                    new Vec2(103f, 204f),
                };

                var match = ObjRefRules.Nearest(spot, candidates);

                match.Index.ShouldBe(1);
            }

            [Fact]
            public void Takes_a_candidate_exactly_at_the_slack()
            {
                var match = Of(new Vec2(50f, 0f));

                match.Found.ShouldBeTrue();
            }

            [Fact]
            public void Ignores_a_candidate_just_past_the_slack()
            {
                var match = Of(new Vec2(50.5f, 0f));

                match.Found.ShouldBeFalse();
            }

            [Fact]
            public void Takes_the_later_of_two_as_near()
            {
                var match = Of(new Vec2(10f, 0f), new Vec2(0f, -10f));

                match.Index.ShouldBe(1);
            }

            [Fact]
            public void Says_on_its_spot_within_one_unit()
            {
                var match = Of(new Vec2(1f, 0f));

                match.OnSpot.ShouldBeTrue();
            }

            [Fact]
            public void Says_off_its_spot_past_one_unit()
            {
                var match = Of(new Vec2(1.5f, 0f));

                match.OnSpot.ShouldBeFalse();
            }
        }

        public class ScanAgain
        {
            /// A table filled at 10 that gave an object on its spot with
            /// the component, asked again just past the rescan time.
            static readonly ScanFacts Usable = new ScanFacts
            {
                Found = true,
                OnSpot = true,
                HasComponent = true,
                Now = new Seconds(10.3f),
                ScannedAt = new Seconds(10f),
            };

            [Fact]
            public void Keeps_a_usable_find()
            {
                var again = ObjRefRules.ScanAgain(Usable);

                again.ShouldBeFalse();
            }

            [Theory]
            [InlineData(false, false, false)]
            [InlineData(true, false, true)]
            [InlineData(true, true, false)]
            public void Scans_again_for_a_find_that_is_not_usable(
                bool found,
                bool onSpot,
                bool hasComponent)
            {
                var scan = Usable with
                {
                    Found = found,
                    OnSpot = onSpot,
                    HasComponent = hasComponent,
                };

                var again = ObjRefRules.ScanAgain(scan);

                again.ShouldBeTrue();
            }

            [Fact]
            public void Waits_exactly_the_rescan_time()
            {
                var scan = Usable with
                {
                    Found = false,
                    Now = new Seconds(10.25f),
                };

                var again = ObjRefRules.ScanAgain(scan);

                again.ShouldBeFalse();
            }

            [Fact]
            public void Scans_a_table_never_filled()
            {
                var scan = new ScanFacts
                {
                    ScannedAt = new Seconds(ObjRefRules.NeverScanned),
                };

                var again = ObjRefRules.ScanAgain(scan);

                again.ShouldBeTrue();
            }
        }

        public class SourceOf
        {
            static readonly LookupFacts Saved = new LookupFacts
            {
                Kind = ObjKind.Saved,
                ResolveLimit = 1000,
            };

            [Theory]
            [InlineData(true, true, true, ObjSource.Spawned)]
            [InlineData(false, true, true, ObjSource.Shared)]
            [InlineData(false, false, true, ObjSource.Announced)]
            [InlineData(false, false, false, ObjSource.Nothing)]
            public void Looks_for_a_spawned_name_in_order(
                bool spawned,
                bool shared,
                bool announced,
                ObjSource source)
            {
                var name = new LookupFacts
                {
                    Kind = ObjKind.Spawned,
                    SpawnedAlive = spawned,
                    SharedAlive = shared,
                    Announced = announced,
                };

                var from = ObjRefRules.SourceOf(name);

                from.ShouldBe(source);
            }

            [Fact]
            public void Looks_for_an_enemy_in_the_enemies_table()
            {
                var name = new LookupFacts { Kind = ObjKind.Enemy };

                var from = ObjRefRules.SourceOf(name);

                from.ShouldBe(ObjSource.Enemy);
            }

            [Theory]
            [InlineData(999L, 1000, ObjSource.SaveId)]
            [InlineData(1000L, 1000, ObjSource.Unshared)]
            [InlineData(-5L, 1000, ObjSource.SaveId)]
            [InlineData(1L, 0, ObjSource.Unshared)]
            [InlineData(0L, 0, ObjSource.Unshared)]
            [InlineData(-5L, 0, ObjSource.SaveId)]
            public void Looks_up_a_save_id_only_below_the_limit(
                long id,
                int limit,
                ObjSource source)
            {
                var name = Saved with { Id = id, ResolveLimit = limit };

                var from = ObjRefRules.SourceOf(name);

                from.ShouldBe(source);
            }

            [Theory]
            [InlineData(true, ObjSource.Pinned)]
            [InlineData(false, ObjSource.Nearest)]
            public void Looks_for_a_placed_name_pinned_first(
                bool pinned,
                ObjSource source)
            {
                var name = new LookupFacts
                {
                    Kind = ObjKind.Placed,
                    PinnedAlive = pinned,
                };

                var from = ObjRefRules.SourceOf(name);

                from.ShouldBe(source);
            }

            [Theory]
            [InlineData((byte)0)]
            [InlineData((byte)9)]
            public void Finds_nothing_for_no_kind_or_an_unknown_one(byte kind)
            {
                var name = new LookupFacts { Kind = (ObjKind)kind };

                var from = ObjRefRules.SourceOf(name);

                from.ShouldBe(ObjSource.Nothing);
            }
        }

        public class EnemyIdOf
        {
            [Fact]
            public void Keeps_the_low_32_bits()
            {
                var id = ObjRefRules.EnemyIdOf((1L << 32) + 5);

                id.ShouldBe(5u);
            }
        }

        public class SaveIdOf
        {
            [Theory]
            [InlineData((1L << 32) + 7, 7)]
            [InlineData(-5L, -5)]
            public void Keeps_the_low_32_bits(long name, int id)
            {
                var saved = ObjRefRules.SaveIdOf(name);

                saved.ShouldBe(id);
            }
        }

        public class MayTie
        {
            static readonly TieFacts Free = new TieFacts
            {
                Id = 7,
                Exists = true,
            };

            [Fact]
            public void Ties_a_free_object_to_a_free_id()
            {
                var tied = ObjRefRules.MayTie(Free);

                tied.ShouldBeTrue();
            }

            [Fact]
            public void Refuses_an_object_that_is_gone()
            {
                var tied = ObjRefRules.MayTie(Free with { Exists = false });

                tied.ShouldBeFalse();
            }

            [Theory]
            [InlineData(false, false, true)]
            [InlineData(true, true, true)]
            [InlineData(true, false, false)]
            public void Refuses_an_id_tied_to_another_object(
                bool idTied,
                bool here,
                bool expected)
            {
                var tie = Free with { IdTied = idTied, IdTiedHere = here };

                var tied = ObjRefRules.MayTie(tie);

                tied.ShouldBe(expected);
            }

            [Theory]
            [InlineData(7L, true)]
            [InlineData(8L, false)]
            public void Refuses_an_object_tied_to_another_id(
                long id,
                bool expected)
            {
                var tie = Free with { ObjectTied = true, ObjectId = id };

                var tied = ObjRefRules.MayTie(tie);

                tied.ShouldBe(expected);
            }
        }

        public class TiesAnnounced
        {
            static readonly TieFacts Found = new TieFacts
            {
                Id = 7,
                Exists = true,
            };

            [Fact]
            public void Ties_what_was_found_untied()
            {
                var tied = ObjRefRules.TiesAnnounced(Found);

                tied.ShouldBeTrue();
            }

            [Fact]
            public void Leaves_what_is_tied_even_to_the_same_id()
            {
                var tie = Found with { ObjectTied = true, ObjectId = 7 };

                var tied = ObjRefRules.TiesAnnounced(tie);

                tied.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_what_was_not_found()
            {
                var tie = Found with { Exists = false };

                var tied = ObjRefRules.TiesAnnounced(tie);

                tied.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_what_the_id_is_tied_to_elsewhere()
            {
                var tie = Found with { IdTied = true };

                var tied = ObjRefRules.TiesAnnounced(tie);

                tied.ShouldBeFalse();
            }
        }
    }
}
