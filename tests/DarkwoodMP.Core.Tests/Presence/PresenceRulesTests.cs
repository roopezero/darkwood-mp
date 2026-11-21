using DarkwoodMP.Presence;

namespace DarkwoodMP.Tests.Presence
{
    public class PresenceRulesTests
    {
        /// Hosting in chapter 2 on day 5, in the forest.
        static readonly PresenceFacts Hosting = new PresenceFacts
        {
            Version = "0.22.0",
            InWorld = true,
            Day = 5,
            Chapter = 2,
            Biome = "Silent Forest",
            InSession = true,
            IsHost = true,
            Players = 3,
            MaxPlayers = 5,
        };

        static readonly PresenceFacts Client = Hosting with
        {
            IsHost = false,
            HostName = "Ann",
        };

        /// On the map, in the meadow.
        static readonly WhereFacts Meadow = new WhereFacts
        {
            HasPlayer = true,
            HasBiome = true,
            Kind = BiomeKind.Meadow,
        };

        public class Limits
        {
            [Fact]
            public void Pins_the_chapters_named()
            {
                var chapters = new[]
                {
                    PresenceRules.FirstChapter,
                    PresenceRules.LastChapter,
                };

                chapters.ShouldBe(new[] { 1, 10 });
            }

            [Fact]
            public void Pins_steams_length_limit()
            {
                var length = PresenceRules.MaxLength;

                length.ShouldBe(256);
            }

            [Fact]
            public void Pins_the_timers()
            {
                var every = new[]
                {
                    PresenceRules.LabelEvery,
                    PresenceRules.PresenceEvery,
                };

                every.ShouldBe(new[] { 2f, 5f });
            }

            [Fact]
            public void Pins_the_biome_values_of_the_game()
            {
                var values = new[]
                {
                    (int)BiomeKind.Forest,
                    (int)BiomeKind.ForestDense,
                    (int)BiomeKind.ForestMutated,
                    (int)BiomeKind.Swamp,
                    (int)BiomeKind.Meadow,
                    (int)BiomeKind.Empty,
                };

                values.ShouldBe(new[] { 0, 10, 20, 100, 200, 300 });
            }
        }

        public class Text
        {
            [Fact]
            public void Says_a_host_hosts()
            {
                var text = PresenceRules.Text(Hosting);

                text.ShouldBe(
                    "DarkwoodMP 0.22.0 · Chapter 2, day 5 · Silent Forest"
                        + " · Hosting, 3/5 players");
            }

            [Fact]
            public void Says_the_main_menu_outside_a_world()
            {
                var game = Hosting with { InWorld = false };

                var text = PresenceRules.Text(game);

                text.ShouldBe("DarkwoodMP 0.22.0 · In the main menu");
            }

            [Fact]
            public void Says_the_day_alone_without_a_chapter()
            {
                var game = Hosting with { Chapter = 0 };

                var text = PresenceRules.Text(game);

                text.ShouldBe(
                    "DarkwoodMP 0.22.0 · Day 5 · Silent Forest"
                        + " · Hosting, 3/5 players");
            }

            [Theory]
            [InlineData(1, "Chapter 1, day 5")]
            [InlineData(10, "Chapter 10, day 5")]
            [InlineData(11, "Chapter 10, day 5")]
            public void Clamps_the_chapter_to_the_last(int chapter, string part)
            {
                var game = Hosting with { Chapter = chapter, Biome = "" };

                var text = PresenceRules.Text(game);

                text.ShouldBe(
                    "DarkwoodMP 0.22.0 · " + part + " · Hosting, 3/5 players");
            }

            [Fact]
            public void Leaves_out_an_empty_biome()
            {
                var game = Hosting with { Biome = "" };

                var text = PresenceRules.Text(game);

                text.ShouldBe(
                    "DarkwoodMP 0.22.0 · Chapter 2, day 5"
                        + " · Hosting, 3/5 players");
            }

            [Fact]
            public void Says_playing_alone_outside_a_session()
            {
                var game = Hosting with { InSession = false };

                var text = PresenceRules.Text(game);

                text.ShouldBe(
                    "DarkwoodMP 0.22.0 · Chapter 2, day 5 · Silent Forest"
                        + " · Playing alone");
            }

            [Fact]
            public void Names_the_host_for_a_client()
            {
                var text = PresenceRules.Text(Client);

                text.ShouldEndWith(" · In Ann's game, 3/5 players");
            }

            [Theory]
            [InlineData(null)]
            [InlineData("")]
            public void Says_co_op_when_the_host_has_no_name(string name)
            {
                var game = Client with { HostName = name };

                var text = PresenceRules.Text(game);

                text.ShouldEndWith(" · In co-op, 3/5 players");
            }
        }

        public class Fallback
        {
            [Fact]
            public void Names_the_version_alone()
            {
                var text = PresenceRules.Fallback("0.22.0");

                text.ShouldBe("DarkwoodMP 0.22.0");
            }
        }

        public class Cut
        {
            [Fact]
            public void Keeps_a_text_exactly_at_the_limit()
            {
                var text = new string('a', 256);

                var cut = PresenceRules.Cut(text);

                cut.Length.ShouldBe(256);
            }

            [Fact]
            public void Cuts_a_text_just_past_the_limit()
            {
                var text = new string('a', 257);

                var cut = PresenceRules.Cut(text);

                cut.Length.ShouldBe(256);
            }
        }

        public class SteamPlayers
        {
            [Theory]
            [InlineData(true, 3, 3)]
            [InlineData(false, 3, 1)]
            public void Tells_steam_one_player_alone(
                bool session,
                int players,
                int told)
            {
                var count = PresenceRules.SteamPlayers(session, players);

                count.ShouldBe(told);
            }
        }

        public class Group
        {
            [Theory]
            [InlineData(true, 109775241L, "109775241")]
            [InlineData(false, 109775241L, "")]
            public void Names_the_lobby_only_in_one(
                bool inLobby,
                long lobby,
                string group)
            {
                var value = PresenceRules.Group(inLobby, (ulong)lobby);

                value.ShouldBe(group);
            }
        }

        public class GroupSize
        {
            [Theory]
            [InlineData(true, 3, "3")]
            [InlineData(false, 3, "")]
            public void Counts_the_players_only_in_a_lobby(
                bool inLobby,
                int players,
                string size)
            {
                var value = PresenceRules.GroupSize(inLobby, players);

                value.ShouldBe(size);
            }
        }

        public class WorldLabel
        {
            [Theory]
            [InlineData(true, 5, 2, "Day 5, chapter 2")]
            [InlineData(true, 5, 11, "Day 5, chapter 11")]
            [InlineData(true, 5, 0, "Day 5")]
            [InlineData(false, 5, 2, "")]
            public void Names_the_day_and_an_unclamped_chapter(
                bool inWorld,
                int day,
                int chapter,
                string label)
            {
                var text = PresenceRules.WorldLabel(inWorld, day, chapter);

                text.ShouldBe(label);
            }
        }

        public class InBiome
        {
            [Fact]
            public void Finds_a_player_on_the_map()
            {
                var found = PresenceRules.InBiome(Meadow);

                found.ShouldBeTrue();
            }

            [Fact]
            public void Finds_none_without_a_player()
            {
                var where = Meadow with { HasPlayer = false };

                var found = PresenceRules.InBiome(where);

                found.ShouldBeFalse();
            }

            [Fact]
            public void Finds_none_in_a_separate_location()
            {
                var where = Meadow with { Outside = true };

                var found = PresenceRules.InBiome(where);

                found.ShouldBeFalse();
            }

            [Fact]
            public void Finds_none_without_a_biome()
            {
                var where = Meadow with { HasBiome = false };

                var found = PresenceRules.InBiome(where);

                found.ShouldBeFalse();
            }

            [Fact]
            public void Finds_none_on_an_empty_biome()
            {
                var where = Meadow with { Kind = BiomeKind.Empty };

                var found = PresenceRules.InBiome(where);

                found.ShouldBeFalse();
            }
        }

        public class BiomeKey
        {
            [Theory]
            [InlineData(BiomeKind.Forest, "biome_forest")]
            [InlineData(BiomeKind.ForestDense, "biome_forest")]
            [InlineData(BiomeKind.ForestMutated, "biome_forest_mutated")]
            [InlineData(BiomeKind.Swamp, "biome_swamp")]
            [InlineData(BiomeKind.Meadow, "biome_meadow")]
            [InlineData(BiomeKind.Empty, "biome_empty")]
            [InlineData((BiomeKind)7, "biome_7")]
            public void Keys_the_biome_as_the_games_sheet(
                BiomeKind kind,
                string key)
            {
                var text = PresenceRules.BiomeKey(kind);

                text.ShouldBe(key);
            }
        }

        public class BiomeName
        {
            [Fact]
            public void Takes_the_name_from_the_games_sheet()
            {
                var name = PresenceRules.BiomeName(BiomeKind.Swamp, "Bog");

                name.ShouldBe("Bog");
            }

            [Theory]
            [InlineData(BiomeKind.Meadow, "", "Dry Meadow")]
            [InlineData(BiomeKind.Forest, null, "Silent Forest")]
            [InlineData(BiomeKind.ForestDense, "", "Silent Forest")]
            [InlineData(BiomeKind.Swamp, "biome_swamp", "Swamp")]
            [InlineData(BiomeKind.ForestMutated, "", "")]
            public void Falls_back_on_english_when_the_sheet_fails(
                BiomeKind kind,
                string sheet,
                string name)
            {
                var text = PresenceRules.BiomeName(kind, sheet);

                text.ShouldBe(name);
            }

            [Fact]
            public void Takes_dense_forests_sheet_key_as_a_failure()
            {
                var name = PresenceRules.BiomeName(
                    BiomeKind.ForestDense,
                    "biome_forest");

                name.ShouldBe("Silent Forest");
            }
        }

        public class LabelDue
        {
            [Theory]
            [InlineData(true, 4f, 4f, true)]
            [InlineData(true, 3.99f, 4f, false)]
            [InlineData(false, 9f, 4f, false)]
            public void Labels_a_host_once_due(
                bool host,
                float now,
                float next,
                bool due)
            {
                var result = PresenceRules.LabelDue(
                    host,
                    new Seconds(now),
                    new Seconds(next));

                result.ShouldBe(due);
            }
        }

        public class NextLabel
        {
            [Fact]
            public void Labels_again_two_seconds_on()
            {
                var next = PresenceRules.NextLabel(new Seconds(3f));

                next.Value.ShouldBe(5f);
            }
        }

        public class PresenceDue
        {
            static readonly PresenceTimerFacts Due = new PresenceTimerFacts
            {
                SteamUp = true,
                Now = new Seconds(5f),
                Next = new Seconds(5f),
            };

            [Fact]
            public void Works_it_out_with_steam_once_due()
            {
                var result = PresenceRules.PresenceDue(Due);

                result.ShouldBeTrue();
            }

            [Fact]
            public void Waits_until_due()
            {
                var timer = Due with { Now = new Seconds(4.99f) };

                var result = PresenceRules.PresenceDue(timer);

                result.ShouldBeFalse();
            }

            [Fact]
            public void Works_it_out_for_the_test_without_steam()
            {
                var timer = Due with { SteamUp = false, Logged = true };

                var result = PresenceRules.PresenceDue(timer);

                result.ShouldBeTrue();
            }

            [Fact]
            public void Skips_it_without_steam_or_the_test()
            {
                var timer = Due with { SteamUp = false };

                var result = PresenceRules.PresenceDue(timer);

                result.ShouldBeFalse();
            }
        }

        public class NextPresence
        {
            [Fact]
            public void Works_it_out_again_five_seconds_on()
            {
                var next = PresenceRules.NextPresence(new Seconds(3f));

                next.Value.ShouldBe(8f);
            }
        }
    }
}
