using UnityEngine;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Whether the overlays the mod draws over the world (name tags above other
    /// players, their names on the map, the spectator line) must be hidden now:
    /// the screen shows something else than the world being played.
    ///
    /// Hidden under the pause menu in a world, the mod's own menu windows,
    /// cutscenes (Controller.playingCutscene, Core.HideUI), the video overlay,
    /// the loading screen, the chapter text, a black screen faded in, entering
    /// a dream, the epilogue, and the game's full-screen menus (dialogue,
    /// journal, a note, the skills and leveling menus, the construction menu,
    /// the padlock). Not hidden while just the backpack or a container is open
    /// (or an item's context menu in it): the world is still in view then. The
    /// map isn't here: it has its own names (MapMarkers.DrawNames).
    /// </summary>
    public static class UiState
    {
        /// <summary>The pause menu over the world, or one of the mod's menu
        /// windows (the co-op panel, Options, a question): something is drawn
        /// over the whole screen.</summary>
        public static bool MenuOver =>
            WorldShare.PauseMenuInWorld || MenuWindows.AnyOpen;

        /// <summary>
        /// World overlays (name tags) must not be drawn now. See the class.
        /// </summary>
        public static bool WorldOverlaysHidden => Reason != null;

        /// <summary>
        /// Why world overlays are hidden now, or null when they're drawn (for
        /// the logs and tests).
        /// </summary>
        public static string Reason
        {
            get
            {
                if (WorldShare.PauseMenuInWorld)
                {
                    return "pause menu";
                }

                if (MenuWindows.AnyOpen)
                {
                    return "mod menu";
                }

                if (Core.loadingGame)
                {
                    return "loading";
                }

                // Core.HideUI is a static the game never clears on a new world:
                // after the epilogue or the credits (hideVisibleUI) it stays
                // true into the next save loaded in the same session, with the
                // HUD back on. So it counts only while hideVisibleUI's bars are
                // actually off.
                if (Core.HideUI && BarsOff())
                {
                    return "UI hidden";
                }

                if (Core.EnteringDream)
                {
                    return "entering a dream";
                }

                var ctl = Singleton<Controller>.Instance;
                if (ctl != null && ctl.playingCutscene)
                {
                    return "cutscene";
                }

                var wg = Singleton<WorldGenerator>.Instance;
                if (wg != null && wg.playingIntro)
                {
                    return "intro";
                }

                var p = Player.Instance;
                if (p != null)
                {
                    if (p.inEpilogue)
                    {
                        return "epilogue";
                    }

                    if (InFullMenu(p))
                    {
                        return "game menu";
                    }
                }

                var ui = Singleton<UI>.Instance;
                if (ui != null)
                {
                    if (ui.videoOverlay != null
                        && ui.videoOverlay.activeInHierarchy)
                    {
                        return "video";
                    }

                    if (ui.loadingScreen != null
                        && ui.loadingScreen.activeInHierarchy)
                    {
                        return "loading screen";
                    }

                    if (ui.chapterText != null
                        && ui.chapterText.gameObject.activeInHierarchy
                        && ui.chapterText.color.a > 0.05f)
                    {
                        return "chapter text";
                    }

                    if (Black(ui.blackScreen) || Black(ui.blackScreenTop))
                    {
                        return "black screen";
                    }
                }

                return null;
            }
        }

        private static bool BarsOff()
        {
            var ui = Singleton<UI>.Instance;
            return ui == null || ui.bars == null || !ui.bars.activeSelf;
        }

        /// <summary>
        /// Player.inMenu without the item context menu (that one is over the
        /// backpack, with the world in view).
        /// </summary>
        private static bool InFullMenu(Player p)
        {
            var map = Singleton<Map>.Instance;
            var cm = Singleton<ConstructionMenu>.Instance;
            var ui = Singleton<UI>.Instance;
            if (map == null || cm == null || ui == null)
            {
                return false;
            }

            return p.inDialogue || cm.open || p.inLevelingMenu || map.opened
                || (ui.journal != null
                    && (ui.journal.opened
                        || (ui.journal.note != null
                            && ui.journal.note.gameObject.activeInHierarchy)))
                || (ui.activeSkillsMenu != null && ui.activeSkillsMenu.opened)
                || ui.padlockOpened || ui.permadeathBox != null
                || (ui.controllerMenu != null && ui.controllerMenu.opened);
        }

        /// <summary>A full-screen fade that is mostly faded in.</summary>
        private static bool Black(GameObject screen)
        {
            if (screen == null || !screen.activeInHierarchy)
            {
                return false;
            }

            var s = screen.GetComponent<tk2dBaseSprite>();
            return s != null && s.color.a > 0.5f;
        }
    }
}
