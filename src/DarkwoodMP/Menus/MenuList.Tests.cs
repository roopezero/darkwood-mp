using System;
using System.Collections.Generic;
using DarkwoodMP.World;
using UnityEngine;

namespace DarkwoodMP.Menus
{
    /// MenuList: what the tests read and click.
    public sealed partial class MenuList
    {
        // ---- for the tests --------------------------------------------------

        /// <summary>
        /// For the tests: the list, where each entry is and whether it would
        /// take a click now.
        /// </summary>
        public string Check()
        {
            var mm = Singleton<MainMenu>.Instance;
            var parts = new List<string>();
            foreach (var en in _entries)
            {
                parts.Add(
                    $"{en.Label} at {en.Rect.x:0},{en.Rect.y:0} "
                        + $"{en.Rect.width:0}x{en.Rect.height:0}");
            }

            string text = "main menu";
            if (_pause)
            {
                text = "pause menu";
            }

            return $"list shown {_shown} (alpha {_alpha:0.00}), {text}: "
                + $"{string.Join("; ", parts)}; takes clicks "
                + $"{!MenuWindows.AnyOpen}; " +
                   $"game entries hidden {GameEntriesHidden(mm)}; screen "
                + $"{Screen.width}x{Screen.height}";
        }

        private bool GameEntriesHidden(MainMenu mm)
        {
            if (mm == null)
            {
                return false;
            }

            foreach (var go in GameEntries(mm))
            {
                if (go == null || !go.activeInHierarchy)
                {
                    continue;
                }

                foreach (var r in go.GetComponentsInChildren<Renderer>())
                {
                    if (r.enabled)
                    {
                        return false;
                    }
                }

                foreach (var c in go.GetComponentsInChildren<Collider>())
                {
                    if (c.enabled)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// For the tests: click the entry with this word, as the mouse does.
        /// False if the list hasn't got it or isn't shown.
        /// </summary>
        public bool Click(string label)
        {
            if (!_shown || MenuWindows.AnyOpen)
            {
                return false;
            }

            foreach (var en in _entries)
            {
                if (string.Equals(
                    en.Label, label, StringComparison.OrdinalIgnoreCase))
                {
                    Fire(en);
                    return true;
                }
            }

            return false;
        }
    }
}
