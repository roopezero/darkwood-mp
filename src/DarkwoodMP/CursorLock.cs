using DarkwoodMP.World;
using UnityEngine;

namespace DarkwoodMP
{
    /// <summary>
    /// The game confines the mouse to its window from the start (Core.Start, MainMenu, Controller on a screen
    /// change) and never lets it go, so on a second monitor the mouse can't leave the main menu either. The
    /// mouse is confined only while playing: in a world, with no menu (the main or pause menu, the co-op panel)
    /// open and nothing loading. Set each frame after the game's own code, which sets it back now and then.
    /// </summary>
    public static class CursorLock
    {
        public static void LateUpdate(bool panelOpen)
        {
            bool playing = WorldShare.InWorld && !Core.mainMenu && !Core.loadingGame && !panelOpen;
            var want = playing ? CursorLockMode.Confined : CursorLockMode.None;
            if (UnityEngine.Cursor.lockState != want) UnityEngine.Cursor.lockState = want;
        }
    }
}
