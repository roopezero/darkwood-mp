using DarkwoodMP.World.Saves;

namespace DarkwoodMP.Tests.World.Saves
{
    public class RedirectRulesTests
    {
        const string Saves =
            "/home/u/.config/unity3d/Acid Wizard Studio/Darkwood/1_4Save";
        const string Cache = "/g/BepInEx/cache/DarkwoodMP/worlds";
        const string Copy = Cache + "/ab12";
        const string Sandbox = "/work/sandbox";
        const string Snapshot = "/g/BepInEx/cache/DarkwoodMP/host-snapshot";

        public class SaveFolder
        {
            [Fact]
            public void Leaves_the_game_its_own_without_a_redirect()
            {
                var dir = RedirectRules.SaveFolder(null, null);

                dir.ShouldBeNull();
            }

            [Fact]
            public void Gives_the_redirects_folder()
            {
                var dir = RedirectRules.SaveFolder(null, Copy);

                dir.ShouldBe(Copy);
            }

            [Fact]
            public void Gives_the_snapshots_folder_over_the_redirect()
            {
                var dir = RedirectRules.SaveFolder(Snapshot, Sandbox);

                dir.ShouldBe(Snapshot);
            }

            [Fact]
            public void Gives_the_snapshots_folder_without_a_redirect()
            {
                var dir = RedirectRules.SaveFolder(Snapshot, null);

                dir.ShouldBe(Snapshot);
            }

            [Fact]
            public void Gives_an_empty_temporary_folder_as_it_is()
            {
                var dir = RedirectRules.SaveFolder("", Sandbox);

                dir.ShouldBe("");
            }
        }

        public class EndsAt
        {
            [Theory]
            [InlineData(true, "Darkwood", true, true)]
            [InlineData(false, "Darkwood", true, false)]
            [InlineData(true, "darkwood", true, false)]
            [InlineData(true, "Darkwood", false, false)]
            public void Ends_at_a_single_load_of_the_menu_after_a_world(
                bool started,
                string scene,
                bool single,
                bool ends)
            {
                var result = RedirectRules.EndsAt(started, scene, single);

                result.ShouldBe(ends);
            }
        }

        public class Works
        {
            [Fact]
            public void Passes_a_save_inside_the_folder()
            {
                var works = RedirectRules.Works(
                    Copy + "/prof1/sav.dat",
                    Copy,
                    false);

                works.ShouldBeTrue();
            }

            [Fact]
            public void Stops_a_save_into_the_real_saves()
            {
                var works = RedirectRules.Works(
                    Saves + "/prof1/sav.dat",
                    Sandbox,
                    false);

                works.ShouldBeFalse();
            }

            [Fact]
            public void Stops_without_a_save_path()
            {
                var works = RedirectRules.Works(null, Sandbox, false);

                works.ShouldBeFalse();
            }
        }

        public class PlayingHostCopy
        {
            [Theory]
            [InlineData(Copy, true)]
            [InlineData(Sandbox, false)]
            [InlineData(null, false)]
            public void Tells_a_redirect_into_the_worlds_cache(
                string redirect,
                bool copy)
            {
                var result = RedirectRules.PlayingHostCopy(
                    redirect,
                    Cache,
                    false);

                result.ShouldBe(copy);
            }
        }

        public class BookFile
        {
            [Fact]
            public void Keeps_the_book_in_config_without_a_redirect()
            {
                var file = RedirectRules.BookFile("/g/config", null, true);

                file.ShouldBe("/g/config/DarkwoodMP.saves.json");
            }

            [Fact]
            public void Keeps_no_book_in_a_host_copy()
            {
                var file = RedirectRules.BookFile("/g/config", Copy, true);

                file.ShouldBeNull();
            }

            [Fact]
            public void Keeps_no_book_when_a_copy_isnt_known()
            {
                var file = RedirectRules.BookFile("/g/config", Sandbox, null);

                file.ShouldBeNull();
            }

            [Fact]
            public void Keeps_the_book_beside_redirected_saves()
            {
                var file = RedirectRules.BookFile("/g/config", Sandbox, false);

                file.ShouldBe(Sandbox + "/darkwoodmp-saves.json");
            }
        }

        public class NewGameForgets
        {
            [Theory]
            [InlineData(true, false, true)]
            [InlineData(true, true, false)]
            [InlineData(false, false, false)]
            public void Forgets_for_a_slot_not_playing(
                bool profile,
                bool active,
                bool forgets)
            {
                var result = RedirectRules.NewGameForgets(profile, active);

                result.ShouldBe(forgets);
            }
        }

        public class NewGameDeletes
        {
            [Theory]
            [InlineData(true, false, true)]
            [InlineData(true, null, true)]
            [InlineData(true, true, false)]
            [InlineData(false, false, false)]
            public void Deletes_unless_a_host_copy(
                bool saves,
                bool? copy,
                bool deletes)
            {
                var result = RedirectRules.NewGameDeletes(saves, copy);

                result.ShouldBe(deletes);
            }
        }

        public class SlotDeleteForgets
        {
            [Theory]
            [InlineData(true, false, true)]
            [InlineData(true, null, true)]
            [InlineData(true, true, false)]
            [InlineData(false, false, false)]
            public void Forgets_unless_a_host_copy(
                bool profile,
                bool? copy,
                bool forgets)
            {
                var result = RedirectRules.SlotDeleteForgets(profile, copy);

                result.ShouldBe(forgets);
            }
        }
    }
}
