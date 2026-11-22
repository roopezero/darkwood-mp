using System;
using System.Collections.Generic;
using DarkwoodMP.World;
using UnityEngine;

namespace DarkwoodMP.Menus
{
    /// <summary>
    /// Hooks for the mod's own menu windows, so the main menu and the pause
    /// menu (MenuList) can open them and know when one is up.
    ///
    /// - <see cref="OpenStartGame"/>: the main menu's Start Game. Until it's
    ///   set, Start Game opens the game's own profiles screen (continue a
    ///   profile or start a new one).
    /// - <see cref="OpenOptions"/>: Options in both menus. Until it's set, the
    ///   game's own options screen.
    /// - <see cref="Register"/>: a window says how to tell it's open. While any
    ///   is (or the co-op panel, or a ConfirmDialog), the menu's list takes no
    ///   clicks or keys, the game's own menu buttons and its Escape are off,
    ///   and the system mouse cursor is shown.
    /// - <see cref="Draw"/>: called from the mod's OnGUI with MenuSkin's skin
    ///   set, after the menu's list and the co-op panel and before a
    ///   ConfirmDialog (which stays on top). Draw the window's UiWindow there.
    /// </summary>
    public static class MenuWindows
    {
        private static MenuWindowList Windows =>
            Plugin.Instance.Host.Windows;

        public static Action OpenStartGame
        {
            get => Windows.OpenStartGame;
            set => Windows.OpenStartGame = value;
        }

        public static Action OpenOptions
        {
            get => Windows.OpenOptions;
            set => Windows.OpenOptions = value;
        }

        public static event Action Draw
        {
            add => Windows.Draw += value;
            remove => Windows.Draw -= value;
        }

        public static void Register(Func<bool> isOpen)
        {
            Windows.Register(isOpen);
        }

        /// <summary>
        /// One of the mod's windows is up over the menus (a registered one, the
        /// co-op panel or a ConfirmDialog).
        /// </summary>
        public static bool AnyOpen => Windows.AnyOpen;

        internal static void DrawAll()
        {
            Windows.DrawAll();
        }
    }
}
