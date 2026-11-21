using System.Collections.Generic;
using DarkwoodMP.World.KeptCharacters;

namespace DarkwoodMP.Tests.World.KeptCharacters
{
    public class CharacterRulesTests
    {
        /// A joiner with a record from the host's own chapter.
        static readonly KindFacts Returning = new KindFacts
        {
            Kept = true,
            Chapter = 2,
            HostChapter = 2,
            NewCharacters = "fresh",
        };

        public class Takes
        {
            [Theory]
            [InlineData(true, true, true)]
            [InlineData(false, true, false)]
            [InlineData(true, false, false)]
            public void Takes_a_keyed_peer_on_the_host(
                bool host,
                bool keyed,
                bool takes)
            {
                CharacterRules.Takes(host, keyed).ShouldBe(takes);
            }
        }

        public class KindOf
        {
            [Fact]
            public void Gives_a_returning_joiner_their_own()
            {
                CharacterRules.KindOf(Returning).ShouldBe(CharacterKind.Own);
            }

            [Fact]
            public void Gives_a_dead_one_over_another_chapter()
            {
                var kind = CharacterRules.KindOf(Returning with
                {
                    Dead = true,
                    Chapter = 1,
                });

                kind.ShouldBe(CharacterKind.Dead);
            }

            [Theory]
            [InlineData(1)]
            [InlineData(3)]
            public void Puts_another_chapters_beside_the_host(int chapter)
            {
                var kind = CharacterRules.KindOf(Returning with
                {
                    Chapter = chapter,
                });

                kind.ShouldBe(CharacterKind.OwnBesideHost);
            }

            [Fact]
            public void Gives_a_record_of_chapter_0_as_own()
            {
                var kind = CharacterRules.KindOf(Returning with
                {
                    Chapter = 0,
                });

                kind.ShouldBe(CharacterKind.Own);
            }

            [Fact]
            public void Gives_a_record_as_own_when_the_host_has_no_chapter()
            {
                var kind = CharacterRules.KindOf(Returning with
                {
                    HostChapter = 0,
                });

                kind.ShouldBe(CharacterKind.Own);
            }

            [Fact]
            public void Gives_their_own_over_a_copy()
            {
                var kind = CharacterRules.KindOf(Returning with
                {
                    NewCharacters = "copy",
                });

                kind.ShouldBe(CharacterKind.Own);
            }

            [Fact]
            public void Gives_someone_new_a_copy_when_set()
            {
                var kind = CharacterRules.KindOf(new KindFacts
                {
                    NewCharacters = "copy",
                });

                kind.ShouldBe(CharacterKind.HostCopy);
            }

            [Theory]
            [InlineData("fresh")]
            [InlineData("Copy")]
            [InlineData(null)]
            public void Gives_someone_new_a_new_one_otherwise(string setting)
            {
                var kind = CharacterRules.KindOf(new KindFacts
                {
                    NewCharacters = setting,
                });

                kind.ShouldBe(CharacterKind.New);
            }

            [Fact]
            public void Ignores_dead_without_a_record()
            {
                var kind = CharacterRules.KindOf(new KindFacts { Dead = true });

                kind.ShouldBe(CharacterKind.New);
            }

            [Fact]
            public void Pins_the_kinds_bytes()
            {
                ((byte)CharacterKind.New).ShouldBe((byte)0);
                ((byte)CharacterKind.Own).ShouldBe((byte)1);
                ((byte)CharacterKind.HostCopy).ShouldBe((byte)2);
                ((byte)CharacterKind.OwnBesideHost).ShouldBe((byte)3);
                ((byte)CharacterKind.Dead).ShouldBe((byte)4);
            }
        }

        public class ChapterOf
        {
            [Theory]
            [InlineData(3, 5, 3)]
            [InlineData(1, 5, 1)]
            [InlineData(0, 5, 5)]
            [InlineData(-1, 5, 5)]
            public void Stamps_the_sent_chapter_or_the_hosts(
                int sent,
                int host,
                int stamped)
            {
                CharacterRules.ChapterOf(sent, host).ShouldBe(stamped);
            }
        }

        public class Keeps
        {
            [Theory]
            [InlineData("{}", "{}", true)]
            [InlineData("", "{}", false)]
            [InlineData("{}", "", false)]
            public void Keeps_a_player_and_an_inventory(
                string player,
                string inventory,
                bool keeps)
            {
                CharacterRules.Keeps(player, inventory).ShouldBe(keeps);
            }
        }

        public class Writes
        {
            [Theory]
            [InlineData(false, false, true)]
            [InlineData(true, false, false)]
            [InlineData(false, true, false)]
            public void Writes_on_the_hosts_own_save_only(
                bool snapshotting,
                bool client,
                bool writes)
            {
                CharacterRules.Writes(snapshotting, client).ShouldBe(writes);
            }
        }

        public class HasToWrite
        {
            [Theory]
            [InlineData(1, true, true)]
            [InlineData(0, true, false)]
            [InlineData(1, false, false)]
            public void Writes_records_into_a_folder(
                int records,
                bool folder,
                bool writes)
            {
                CharacterRules.HasToWrite(records, folder).ShouldBe(writes);
            }
        }

        public class Rereads
        {
            [Theory]
            [InlineData("/s/prof1/p", "/s/prof2/p", true)]
            [InlineData("/s/prof1/p", null, true)]
            [InlineData("/s/prof1/p", "/s/prof1/p", false)]
            [InlineData(null, "/s/prof1/p", false)]
            public void Rereads_a_new_folder(
                string folder,
                string loaded,
                bool rereads)
            {
                CharacterRules.Rereads(folder, loaded).ShouldBe(rereads);
            }
        }

        public class CountOf
        {
            static readonly HashSet<string> Traps =
                new HashSet<string> { "trap", "mine" };

            [Fact]
            public void Counts_the_amounts_of_the_types()
            {
                var bag = new[]
                {
                    new CarriedItem { Type = "trap", Amount = 3 },
                    new CarriedItem { Type = "mine", Amount = 2 },
                    new CarriedItem { Type = "nail", Amount = 9 },
                };

                CharacterRules.CountOf(bag, Traps).ShouldBe(5);
            }

            [Theory]
            [InlineData(0)]
            [InlineData(-4)]
            public void Counts_an_item_of_no_amount_as_one(int amount)
            {
                var bag = new[]
                {
                    new CarriedItem { Type = "trap", Amount = amount },
                };

                CharacterRules.CountOf(bag, Traps).ShouldBe(1);
            }

            [Fact]
            public void Skips_an_item_without_a_type()
            {
                var bag = new[] { new CarriedItem { Amount = 3 } };

                CharacterRules.CountOf(bag, Traps).ShouldBe(0);
            }

            [Fact]
            public void Counts_no_bag_as_none()
            {
                CharacterRules.CountOf(null, Traps).ShouldBe(0);
            }
        }

        public class Carries
        {
            [Fact]
            public void Goes_below_zero()
            {
                CharacterRules.Carries(1, 0, 3).ShouldBe(-2);
            }
        }

        public class Electrician
        {
            [Fact]
            public void Finds_the_skill()
            {
                var skills = new[] { null, "lockpick", "electrician" };

                CharacterRules.Electrician(skills).ShouldBe(true);
            }

            [Fact]
            public void Tells_a_character_without_it()
            {
                var skills = new[] { "lockpick" };

                CharacterRules.Electrician(skills).ShouldBe(false);
            }

            [Fact]
            public void Doesnt_know_without_a_skill_list()
            {
                CharacterRules.Electrician(null).ShouldBeNull();
            }
        }
    }
}
