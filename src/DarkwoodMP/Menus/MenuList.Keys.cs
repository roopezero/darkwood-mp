using System;
using System.Collections.Generic;
using DarkwoodMP.World;
using UnityEngine;

namespace DarkwoodMP.Menus
{
    /// MenuList: choosing and running an entry with the keys or a
    /// controller.
    public sealed partial class MenuList
    {
        // ---- keys and the controller ----------------------------------------

        private void Keys()
        {
            int dir = 0;
            if (Input.GetKeyDown(KeyCode.UpArrow)
                || Input.GetKeyDown(KeyCode.W))
            {
                dir = -1;
            }

            if (Input.GetKeyDown(KeyCode.DownArrow)
                || Input.GetKeyDown(KeyCode.S))
            {
                dir = 1;
            }

            bool run = Input.GetKeyDown(KeyCode.Return)
                || Input.GetKeyDown(KeyCode.KeypadEnter);
            try
            {
                InputScript instance = Singleton<InputScript>.Instance;
                Rewired.Player pad = null;
                if (instance != null)
                {
                    pad = instance.rewiredPlayer;
                }

                if (pad != null)
                {
                    float v = pad.GetAxisRaw("Dpad Vertical");
                    if (Mathf.Abs(v) > 0.5f && Time.unscaledTime >= _nextPad)
                    {
                        if (v > 0)
                        {
                            dir = -1;
                        }
                        else
                        {
                            dir = 1;
                        }

                        _nextPad = Time.unscaledTime + 0.25f;
                    }
                    else if (Mathf.Abs(v) <= 0.5f)
                    {
                        _nextPad = 0f;
                    }

                    if (pad.GetButtonDown("A"))
                    {
                        run = true;
                    }
                }
            }
            catch
            {
            }

            if (dir != 0 && _entries.Count > 0)
            {
                if (_selected < 0)
                {
                    if (dir > 0)
                    {
                        _selected = 0;
                    }
                    else
                    {
                        _selected = _entries.Count - 1;
                    }
                }
                else
                {
                    _selected =
                        (_selected + dir + _entries.Count) % _entries.Count;
                }
            }

            if (run && _selected >= 0 && _selected < _entries.Count)
            {
                Fire(_entries[_selected]);
            }
        }

        private void Fire(Entry e)
        {
            Plugin.Log.LogInfo($"[menu] {e.Label}");
            _pending = e.Run;
        }
    }
}
