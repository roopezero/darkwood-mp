using System;
using System.Collections.Generic;
using UnityEngine;

namespace DarkwoodMP.Menus
{
    /// <summary>
    /// The mod's part in the game's main menu and pause menu, besides their
    /// entries (MenuList draws those): the mod's version under the game's, the
    /// layout of the logo and the versions (MenuLayout), the options pages'
    /// words in the game's font (MenuWords), and the pause menu over the world.
    ///
    /// The pause menu doesn't hide the world: instead of the menu's painted
    /// backdrop the world shows through, dimmed by the menu's own black
    /// overlay. Alone the world is paused behind it as before; in co-op it goes
    /// on (World.Pause.CoopPause) and the menu's music doesn't start.
    /// </summary>
    public sealed class MenuEntry
    {
        /// <summary>Call every frame while a menu may be up.</summary>
        public void Update()
        {
            var mm = Singleton<MainMenu>.Instance;
            if (mm == null || mm.Menu0 == null || !mm.Menu0.activeInHierarchy)
            {
                return;
            }

            Plugin.Instance.Host.MenuWords.Update(mm);
            // The mod's version under the game's, both in the bottom right
            // corner (MenuLayout).
            var v = mm.CurrentVersion;
            if (v != null && !string.IsNullOrEmpty(v.text)
                && !v.text.Contains(PluginInfo.Name))
            {
                v.text += $"\n{PluginInfo.Name} {PluginInfo.Version}";
                v.Commit();
            }

            Plugin.Instance.Host.MenuLayout.Apply(mm);
        }

        // ---- the pause menu in co-op ----------------------------------------

        private GameObject _hiddenBackdrop;
        private bool _dimmed;

        internal void PauseMenuOpened(MainMenu mm)
        {
            if (!World.WorldShare.PauseMenuInWorld)
            {
                return;
            }

            // The world stays drawn behind the menu, playing alone too (the
            // game turns its camera off).
            var cam = Singleton<CamMain>.Instance;
            if (cam != null && !cam.gameObject.activeSelf)
            {
                cam.gameObject.SetActive(true);
            }

            var backdrop = mm.transform.Find("Background");
            if (backdrop != null && backdrop.gameObject.activeSelf)
            {
                backdrop.gameObject.SetActive(false);
                _hiddenBackdrop = backdrop.gameObject;
            }

            if (mm.blackScreen != null && !mm.blackScreen.activeSelf)
            {
                mm.blackScreen.SetActive(true);
                _dimmed = true;
            }

            // the HUD would show through with the world
            Plugin.Instance.Host.PauseHud.Hide();
            // In co-op the game goes on, with its own sound; alone it's paused
            // and the menu's music plays as before.
            CoopSession coop = Coop.Current;
            bool together = coop != null && coop.Pause.InCoop;
            if (together && mm.menuMusic != null)
            {
                mm.menuMusic.Stop(0.3f);
                mm.menuMusic = null;
            }
        }

        internal void PauseMenuClosed(MainMenu mm)
        {
            Plugin.Instance.Host.PauseHud.Restore();
            if (_hiddenBackdrop != null)
            {
                _hiddenBackdrop.SetActive(true);
                _hiddenBackdrop = null;
            }

            if (_dimmed && mm.blackScreen != null)
            {
                mm.blackScreen.SetActive(false);
            }

            _dimmed = false;
        }
    }
}
