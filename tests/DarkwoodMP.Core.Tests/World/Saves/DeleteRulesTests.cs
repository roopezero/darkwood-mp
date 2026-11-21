using DarkwoodMP.World.Saves;

namespace DarkwoodMP.Tests.World.Saves
{
    public class DeleteRulesTests
    {
        const string Home = "/home/u";
        const string Cache = "/g/BepInEx/cache/DarkwoodMP";
        const string Saves =
            "/home/u/.config/unity3d/Acid Wizard Studio/Darkwood/1_4Save";

        /// A cached world of a host's, on Linux, saves not redirected.
        static readonly DeleteFacts Cached = new DeleteFacts
        {
            Path = Cache + "/worlds/ab12",
            Home = Home,
            UserProfile = Home,
            CacheRoot = Cache,
        };

        /// The players' folder of slot 1 in the user's own saves.
        static readonly DeleteFacts Players = new DeleteFacts
        {
            Path = Saves,
            Home = Home,
            UserProfile = Home,
            CacheRoot = Cache,
            Slot = 1,
        };

        public class NeverDelete
        {
            [Theory]
            [InlineData(null)]
            [InlineData("")]
            [InlineData("   ")]
            public void Refuses_an_empty_path(string path)
            {
                var verdict = DeleteRules.NeverDelete(Cached with
                {
                    Path = path,
                });

                verdict.Reason.ShouldBe(Refusal.PathEmpty);
            }

            [Fact]
            public void Refuses_a_relative_path()
            {
                var verdict = DeleteRules.NeverDelete(Cached with
                {
                    Path = "worlds/ab12",
                });

                verdict.Reason.ShouldBe(Refusal.PathNotAbsolute);
            }

            [Fact]
            public void Refuses_an_invalid_path()
            {
                var verdict = DeleteRules.NeverDelete(Cached with
                {
                    Path = "/g/a\0b",
                });

                verdict.Reason.ShouldBe(Refusal.PathInvalid);
            }

            [Theory]
            [InlineData("/")]
            [InlineData("//")]
            public void Refuses_a_filesystem_root(string path)
            {
                var verdict = DeleteRules.NeverDelete(Cached with
                {
                    Path = path,
                });

                verdict.Reason.ShouldBe(Refusal.FilesystemRoot);
            }

            [Theory]
            [InlineData("/home/u")]
            [InlineData("/home/u/")]
            [InlineData("/home")]
            public void Refuses_the_home_folder_or_one_holding_it(string path)
            {
                var verdict = DeleteRules.NeverDelete(Cached with
                {
                    Path = path,
                });

                verdict.Reason.ShouldBe(Refusal.HomeFolder);
            }

            [Fact]
            public void Refuses_the_user_profile_folder_too()
            {
                var verdict = DeleteRules.NeverDelete(Cached with
                {
                    Path = "/users/p",
                    Home = null,
                    UserProfile = "/users/p",
                });

                verdict.Reason.ShouldBe(Refusal.HomeFolder);
            }

            [Fact]
            public void Passes_by_a_relative_home()
            {
                var verdict = DeleteRules.NeverDelete(Cached with
                {
                    Path = "/home",
                    Home = "u",
                    UserProfile = "",
                });

                verdict.Refused.ShouldBeFalse();
            }

            [Fact]
            public void Allows_a_folder_inside_home()
            {
                var verdict = DeleteRules.NeverDelete(Cached with
                {
                    Path = Saves,
                });

                verdict.Refused.ShouldBeFalse();
            }
        }

        public class MayDelete
        {
            [Fact]
            public void Allows_a_folder_inside_the_cache()
            {
                var verdict = DeleteRules.MayDelete(Cached);

                verdict.Refused.ShouldBeFalse();
            }

            [Fact]
            public void Refuses_the_cache_itself()
            {
                var verdict = DeleteRules.MayDelete(Cached with
                {
                    Path = Cache + "/",
                });

                verdict.Reason.ShouldBe(Refusal.OutsideModFolders);
            }

            [Fact]
            public void Refuses_a_folder_beside_the_cache_with_its_name()
            {
                var verdict = DeleteRules.MayDelete(Cached with
                {
                    Path = Cache + "-old/worlds",
                });

                verdict.Reason.ShouldBe(Refusal.OutsideModFolders);
            }

            [Fact]
            public void Refuses_a_cache_path_that_climbs_out()
            {
                var verdict = DeleteRules.MayDelete(Cached with
                {
                    Path = Cache + "/../../../home/u/x",
                });

                verdict.Reason.ShouldBe(Refusal.OutsideModFolders);
            }

            [Fact]
            public void Refuses_the_real_saves_without_a_redirect()
            {
                var verdict = DeleteRules.MayDelete(Cached with
                {
                    Path = Saves + "/prof1",
                });

                verdict.Reason.ShouldBe(Refusal.OutsideModFolders);
            }

            [Fact]
            public void Refuses_the_real_saves_with_a_redirect_elsewhere()
            {
                var verdict = DeleteRules.MayDelete(Cached with
                {
                    Path = Saves + "/prof1",
                    Redirect = "/work/sandbox",
                });

                verdict.Reason.ShouldBe(Refusal.OutsideModFolders);
            }

            [Fact]
            public void Allows_a_folder_inside_the_redirect()
            {
                var verdict = DeleteRules.MayDelete(Cached with
                {
                    Path = "/work/sandbox/testworld",
                    Redirect = "/work/sandbox",
                });

                verdict.Refused.ShouldBeFalse();
            }

            [Fact]
            public void Refuses_the_redirect_itself()
            {
                var verdict = DeleteRules.MayDelete(Cached with
                {
                    Path = "/work/sandbox",
                    Redirect = "/work/sandbox",
                });

                verdict.Reason.ShouldBe(Refusal.OutsideModFolders);
            }

            [Fact]
            public void Drops_a_redirect_that_holds_home()
            {
                var verdict = DeleteRules.MayDelete(Cached with
                {
                    Path = Saves + "/prof1",
                    Redirect = "/home",
                });

                verdict.Reason.ShouldBe(Refusal.OutsideModFolders);
            }

            [Fact]
            public void Puts_the_base_checks_first()
            {
                var verdict = DeleteRules.MayDelete(Cached with
                {
                    Path = "/home",
                });

                verdict.Reason.ShouldBe(Refusal.HomeFolder);
            }

            [Fact]
            public void Compares_with_case_on_linux()
            {
                var verdict = DeleteRules.MayDelete(Cached with
                {
                    Path = "/g/BepInEx/CACHE/DarkwoodMP/worlds",
                });

                verdict.Reason.ShouldBe(Refusal.OutsideModFolders);
            }

            [Fact]
            public void Compares_without_case_when_told()
            {
                var verdict = DeleteRules.MayDelete(Cached with
                {
                    Path = "/g/BepInEx/CACHE/DarkwoodMP/worlds",
                    IgnoreCase = true,
                });

                verdict.Refused.ShouldBeFalse();
            }
        }

        public class MayDeletePlayers
        {
            [Fact]
            public void Allows_a_slots_players_folder()
            {
                var verdict = DeleteRules.MayDeletePlayers(Players);

                verdict.Refused.ShouldBeFalse();
            }

            [Theory]
            [InlineData(1, false)]
            [InlineData(99, false)]
            [InlineData(0, true)]
            [InlineData(100, true)]
            public void Refuses_a_slot_outside_1_to_99(int slot, bool refused)
            {
                var verdict = DeleteRules.MayDeletePlayers(Players with
                {
                    Slot = slot,
                });

                verdict.Refused.ShouldBe(refused);
            }

            [Fact]
            public void Names_the_slot_it_refuses()
            {
                var verdict = DeleteRules.MayDeletePlayers(Players with
                {
                    Slot = 100,
                });

                verdict.ShouldBe(Verdict.Refuse(Refusal.NotASaveSlot, "100"));
            }

            [Fact]
            public void Puts_the_slot_before_the_save_folder()
            {
                var verdict = DeleteRules.MayDeletePlayers(Players with
                {
                    Path = "",
                    Slot = 0,
                });

                verdict.Reason.ShouldBe(Refusal.NotASaveSlot);
            }

            [Fact]
            public void Refuses_a_relative_save_folder()
            {
                var verdict = DeleteRules.MayDeletePlayers(Players with
                {
                    Path = "1_4Save",
                });

                verdict.Reason.ShouldBe(Refusal.PathNotAbsolute);
            }

            [Fact]
            public void Refuses_the_home_folder_as_the_save_folder()
            {
                var verdict = DeleteRules.MayDeletePlayers(Players with
                {
                    Path = Home,
                });

                verdict.Reason.ShouldBe(Refusal.HomeFolder);
            }

            [Fact]
            public void Names_the_players_folder_of_a_slot()
            {
                var dir = DeleteRules.PlayersOf("/s", 7);

                dir.ShouldBe("/s/prof7/darkwoodmp_players");
            }
        }

        public class SamePath
        {
            [Theory]
            [InlineData("/a/b", "/a/b/", true)]
            [InlineData("/a/b", "/a/./b", true)]
            [InlineData("/a/b", "/a/c", false)]
            [InlineData(null, "/a", false)]
            [InlineData("/a", null, false)]
            public void Compares_full_paths(string a, string b, bool same)
            {
                var result = DeleteRules.SamePath(a, b, false);

                result.ShouldBe(same);
            }
        }

        public class IsUnder
        {
            [Theory]
            [InlineData("/a/b/c", "/a/b", true)]
            [InlineData("/a/b", "/a/b", true)]
            [InlineData("/a/bc", "/a/b", false)]
            [InlineData("/a", "/a/b", false)]
            [InlineData(null, "/a", false)]
            [InlineData("/a", null, false)]
            public void Tells_a_path_inside_a_folder(
                string path,
                string dir,
                bool under)
            {
                var result = DeleteRules.IsUnder(path, dir, false);

                result.ShouldBe(under);
            }
        }
    }
}
