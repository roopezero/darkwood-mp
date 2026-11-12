using System.Collections.Generic;
using UnityEngine;

namespace DarkwoodMP
{
    /// <summary>
    /// The game's cursor is a sprite in its own UI (UI.cursor, with its children: the plus, the action text), so the
    /// mod's windows, drawn with IMGUI after everything else, cover it. While one of them is open, that sprite is
    /// hidden and the system cursor (which Plugin.ShowSystemCursor shows then) takes the game's own cursor picture,
    /// the one the game uses when its hardware cursor setting is on, so the cursor looks the same and stays on top.
    /// The sprite's renderers are hidden each frame after the game's code, which turns them on again as it plays the
    /// cursor's animations; the ones hidden are turned back on when the last window closes.
    /// </summary>
    public static class GameCursor
    {
        private static readonly List<Renderer> _hidden = new List<Renderer>();
        private static bool _active;

        public static void LateUpdate(bool windowsOpen)
        {
            var ui = Singleton<UI>.Instance;
            var cursor = ui != null ? ui.cursor : null;
            if (windowsOpen && cursor != null)
            {
                foreach (var r in cursor.GetComponentsInChildren<Renderer>())
                    if (r.enabled) { r.enabled = false; if (!_hidden.Contains(r)) _hidden.Add(r); }
                if (!_active)
                {
                    _active = true;
                    if (cursor.idle != null) UnityEngine.Cursor.SetCursor(cursor.idle, new Vector2(32f, 32f), CursorMode.Auto);
                    bool hw = Singleton<Globals>.Instance != null && Singleton<Globals>.Instance.useHardwareCursor;
                    Plugin.Log.LogInfo($"[cursor] a window is open: the game's cursor sprite hidden ({_hidden.Count} parts), the system cursor shows its picture ({(cursor.idle != null ? cursor.idle.width + "x" + cursor.idle.height : "none")}); hardware cursor setting {hw}");
                }
            }
            else if (_active)
            {
                _active = false;
                foreach (var r in _hidden) if (r != null) r.enabled = true;
                _hidden.Clear();
                // With the hardware cursor setting on, the game's picture is the system cursor's anyway; otherwise the default again.
                bool hw = Singleton<Globals>.Instance != null && Singleton<Globals>.Instance.useHardwareCursor;
                if (!hw) UnityEngine.Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
                Plugin.Log.LogInfo("[cursor] the windows closed: the game's cursor sprite is back");
            }
        }
    }
}
