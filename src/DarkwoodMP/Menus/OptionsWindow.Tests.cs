using System;
using System.Collections.Generic;
using UnityEngine;
using RW = Rewired;

namespace DarkwoodMP.Menus
{
    internal sealed partial class OptionsWindow
    {
        // ---- for the tests --------------------------------------------------

        internal void TestTab(int t)
        {
            SwitchTab((Tab)t);
        }

        internal string[] TestTabNames => TabNames;
        /// <summary>
        /// Open a dropdown as a click on it does ("res", "lang", ...), the next
        /// time it's drawn.
        /// </summary>
        internal void TestOpenDrop(string key)
        {
            _testDropKey = key;
        }

        private string _testDropKey;
        internal bool TestMapping => _mapRow != null;
        internal string TestMapperStatus
        {
            get
            {
                if (_mapper == null)
                {
                    return "none";
                }

                return _mapper.status.ToString();
            }
        }

        /// <summary>
        /// The keyboard rows (label, key) as the window lists them.
        /// </summary>
        internal
            List<(string label, string key, int actionId, int range)>
            TestRows()
        {
            BuildRows();
            var l = new List<(string, string, int, int)>();
            foreach (var b in _rows)
            {
                if (b.Allowed[0])
                {
                    l.Add((b.Label, b.Keys[0], b.Action.id, (int)b.Range));
                }
            }

            return l;
        }

        private Binding KeyboardRow(string label) =>
            _rows.Find(r => r.Label == label && r.Allowed[0]);

        /// <summary>
        /// Start listening for a key for the keyboard row with this label, as a
        /// click on it does.
        /// </summary>
        internal bool TestBeginMapping(string label)
        {
            if (_rows == null)
            {
                BuildRows();
            }

            var b = KeyboardRow(label);
            if (b == null)
            {
                return false;
            }

            BeginMapping(b, 0);
            return true;
        }

        internal void TestEscape()
        {
            _escTaken = Time.frameCount;
            StopMapping("Cancelled: nothing changed.");
        }

        /// <summary>
        /// Put <paramref name="key"/> on the keyboard row with this label as a
        /// finished mapping does (the element map replaced in the player's
        /// keyboard map, then saved), for a test that can't press a real key.
        /// </summary>
        internal bool TestAssign(string label, KeyCode key)
        {
            if (_rows == null)
            {
                BuildRows();
            }

            var b = KeyboardRow(label);
            var map = Map(RW.ControllerType.Keyboard);
            if (b == null || map == null)
            {
                return false;
            }

            var ident =
                RW.ReInput.controllers.Keyboard.GetElementIdentifierByKeyCode(
                    key);
            if (ident == null)
            {
                return false;
            }

            RW.Pole pole = RW.Pole.Positive;
            if (b.Range == RW.AxisRange.Negative)
            {
                pole = RW.Pole.Negative;
            }

            bool ok = map.ReplaceOrCreateElementMap(
                new RW.ElementAssignment(
                    RW.ControllerType.Keyboard, RW.ControllerElementType.Button,
                    ident.id, b.Range,
                    key, RW.ModifierKeyFlags.None, b.Action.id, pole, false,
                    b.MapIds[0]));
            SaveControls();
            return ok;
        }

        /// <summary>
        /// The key on the keyboard row with this label (None: none).
        /// </summary>
        internal KeyCode TestKeyOf(string label)
        {
            BuildRows();
            var b = KeyboardRow(label);
            var map = Map(RW.ControllerType.Keyboard);
            RW.ActionElementMap em = null;
            if (b != null && map != null && b.MapIds[0] >= 0)
            {
                em = map.GetElementMap(b.MapIds[0]);
            }

            if (em != null)
            {
                return em.keyCode;
            }

            return KeyCode.None;
        }

        /// <summary>Some action in the keyboard map has this key.</summary>
        internal bool TestKeyUsed(KeyCode key)
        {
            var map = Map(RW.ControllerType.Keyboard);
            if (map == null)
            {
                return true;
            }

            foreach (var em in map.AllMaps)
            {
                if (em.keyCode == key)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// The game's stored key maps read back into the player's maps
        /// (ControlsMenu.Load).
        /// </summary>
        internal void TestReloadControls()
        {
            RW.Interfaces.IUserDataStore userDataStore =
                RW.ReInput.userDataStore;
            if (userDataStore != null)
            {
                userDataStore.Load();
            }

            if (Menu != null && Menu.ControlsMenu != null)
            {
                Menu.ControlsMenu.loadMaps();
            }

            _rows = null;
        }
    }
}
