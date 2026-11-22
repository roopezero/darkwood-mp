using System;
using System.Collections.Generic;

namespace DarkwoodMP.Menus
{
    /// <summary>
    /// What MenuWindows keeps: the windows' hooks, how to tell each is open
    /// and their draw calls. One lives as long as the game
    /// (ModHost.Windows).
    /// </summary>
    internal sealed class MenuWindowList
    {
        public Action OpenStartGame;
        public Action OpenOptions;
        public event Action Draw;

        private readonly List<Func<bool>> _open = new List<Func<bool>>();

        public void Register(Func<bool> isOpen)
        {
            if (isOpen != null)
            {
                _open.Add(isOpen);
            }
        }

        /// <summary>
        /// One of the mod's windows is up over the menus (a registered one, the
        /// co-op panel or a ConfirmDialog).
        /// </summary>
        public bool AnyOpen
        {
            get
            {
                var plugin = Plugin.Instance;
                if (plugin != null
                    && (plugin.Host.Confirm.Open || plugin.PanelOpen))
                {
                    return true;
                }

                foreach (var f in _open)
                {
                    try
                    {
                        if (f())
                        {
                            return true;
                        }
                    }
                    catch
                    {
                    }
                }

                return false;
            }
        }

        public void DrawAll()
        {
            if (Draw == null)
            {
                return;
            }

            foreach (Action d in Draw.GetInvocationList())
            {
                try
                {
                    d();
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning(
                        $"A menu window failed to draw: {e.Message}");
                }
            }
        }
    }
}
