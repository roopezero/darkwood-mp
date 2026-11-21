using System;
using System.Collections.Generic;
using UnityEngine;

namespace DarkwoodMP.Menus
{
    /// <summary>
    /// The player's HUD under the pause menu in a world. The menu shows the
    /// world dimmed behind it (MenuEntry), and without this the HUD would show
    /// through as well: the backpack, the hotbar, the crafting window, an open
    /// container's windows, the bars, the clock, the upgrade menu, the progress
    /// bar and the messages over the player. As the menu opens, the ones that
    /// are on are switched off and noted; as it goes, exactly those are
    /// switched back on (a container's window only if that container is still
    /// open: in co-op the world goes on behind the menu). Not UI.showPlayerUI,
    /// which would show the bars during a cutscene. The windows are
    /// deactivated, as the game's UI.hidePlayerUI does; the progress bar, the
    /// messages and the clock only have their renderers off, so their timers go
    /// on. Playing alone too: the world shows through there as well.
    /// </summary>
    public sealed class PauseHud
    {
        private sealed class Off
        {
            public GameObject Go;
            public Func<bool> Still;
        }

        private readonly List<Off> _off = new List<Off>();
        private readonly List<Renderer> _renderers =
            new List<Renderer>();
        private bool _hidden;

        public void Hide()
        {
            if (_hidden)
            {
                return;
            }

            var p = Player.Instance;
            var ui = Singleton<UI>.Instance;
            if (p == null || ui == null)
            {
                return;
            }

            _hidden = true;
            Window(p.Inventory, null);
            Window(p.Hotbar, null);
            Window(p.Crafting, null);
            var c1 = p.openedItemInventory;
            var c2 = p.openedItemInventory2;
            Window(
                c1,
                () => p.openedItemInventory == c1
                    || p.openedItemInventory2 == c1);
            Window(
                c2,
                () => p.openedItemInventory == c2
                    || p.openedItemInventory2 == c2);
            var up = ui.upgradeItemMenu;
            if (up != null && up.opened)
            {
                Deactivate(up.gameObject, () => up.opened);
            }

            Deactivate(ui.bars, null);
            if (ui.clock != null)
            {
                Renderers(ui.clock.gameObject, self: true);
            }

            if (p.progressBar != null)
            {
                Renderers(p.progressBar.gameObject, self: false);
            }

            foreach (var go in p.attachedGameObjects)
            {
                if (go != null)
                {
                    Renderers(go, self: false);
                }
            }

            Plugin.Log.LogInfo(
                $"[pause hud] hidden: {_off.Count} windows, {_renderers.Count} "
                    + "renderers");
        }

        public void Restore()
        {
            if (!_hidden)
            {
                return;
            }

            _hidden = false;
            int back = 0;
            foreach (var o in _off)
            {
                if (o.Go == null || o.Go.activeSelf)
                {
                    continue;
                }

                bool still;
                try
                {
                    still = o.Still == null || o.Still();
                }
                catch
                {
                    still = false;
                }

                if (still)
                {
                    o.Go.SetActive(true);
                    back++;
                }
            }

            foreach (var r in _renderers)
            {
                if (r != null)
                {
                    r.enabled = true;
                }
            }

            Plugin.Log.LogInfo(
                $"[pause hud] restored: {back} of {_off.Count} windows, "
                    + $"{_renderers.Count} renderers");
            _off.Clear();
            _renderers.Clear();
        }

        private void Window(Inventory inv, Func<bool> still)
        {
            if (inv != null)
            {
                Deactivate(inv.thisUI, still);
            }
        }

        private void Deactivate(GameObject go, Func<bool> still)
        {
            if (go == null || !go.activeSelf || _off.Exists(o => o.Go == go))
            {
                return;
            }

            go.SetActive(false);
            _off.Add(new Off { Go = go, Still = still });
        }

        private void Renderers(GameObject go, bool self)
        {
            Renderer[] rs;
            if (self)
            {
                rs = new[] { go.GetComponent<Renderer>() };
            }
            else
            {
                rs = go.GetComponentsInChildren<Renderer>(false);
            }

            foreach (var r in rs)
            {
                if (r != null && r.enabled)
                {
                    r.enabled = false;
                    _renderers.Add(r);
                }
            }
        }

        /// <summary>
        /// For the tests: the parts of the HUD shown now, by name ("none" if
        /// none).
        /// </summary>
        public string Shown()
        {
            var p = Player.Instance;
            var ui = Singleton<UI>.Instance;
            if (p == null || ui == null)
            {
                return "no player";
            }

            var on = new List<string>();
            void Go(string name, GameObject go)
            {
                if (go != null && go.activeInHierarchy)
                {
                    on.Add(name);
                }
            }

            GameObject backpack = null;
            if (p.Inventory != null)
            {
                backpack = p.Inventory.thisUI;
            }

            Go("backpack", backpack);
            GameObject hotbar = null;
            if (p.Hotbar != null)
            {
                hotbar = p.Hotbar.thisUI;
            }

            Go("hotbar", hotbar);
            GameObject crafting = null;
            if (p.Crafting != null)
            {
                crafting = p.Crafting.thisUI;
            }

            Go("crafting", crafting);
            GameObject container = null;
            if (p.openedItemInventory != null)
            {
                container = p.openedItemInventory.thisUI;
            }

            Go("container", container);
            GameObject secondContainer = null;
            if (p.openedItemInventory2 != null)
            {
                secondContainer = p.openedItemInventory2.thisUI;
            }

            Go("container2", secondContainer);
            if (ui.upgradeItemMenu != null && ui.upgradeItemMenu.opened)
            {
                Go("upgrade", ui.upgradeItemMenu.gameObject);
            }

            Go("bars", ui.bars);
            Renderer cr = null;
            if (ui.clock != null)
            {
                cr = ui.clock.GetComponent<Renderer>();
            }

            if (cr != null && cr.enabled && cr.gameObject.activeInHierarchy)
            {
                on.Add("clock");
            }

            if (on.Count == 0)
            {
                return "none";
            }

            return string.Join(" ", on);
        }
    }
}
