using System;
using System.Collections.Generic;
using UnityEngine;
using RW = Rewired;

namespace DarkwoodMP.Menus
{
    internal sealed partial class OptionsWindow
    {
        // ---- controls -------------------------------------------------------

        private sealed class Binding
        {
            public RW.InputAction Action;
            public RW.AxisRange Range;
            public string Label;
            // keyboard, mouse, controller; null: none there
            public readonly string[] Keys = new string[3];
            // the ActionElementMap shown in each column
            public readonly int[] MapIds = { -1, -1, -1 };
            // the game lists this action in that column
            public readonly bool[] Allowed = new bool[3];
        }

        private static readonly string[] Columns =
            { "Keyboard", "Mouse", "Controller" };
        private static readonly RW.ControllerType[] ColumnTypes =
        {
            RW.ControllerType.Keyboard,
            RW.ControllerType.Mouse,
            RW.ControllerType.Joystick
        };

        private List<Binding> _rows;
        private Vector2 _listScroll;
        private string _status;
        private bool _statusWarn;

        private RW.InputMapper _mapper;
        private Binding _mapRow;
        private int _mapCol;
        private bool _startWhenReleased;
        private RW.InputMapper.ConflictFoundEventData _conflict;
        private int _escTaken;

        /// <summary>
        /// The player's map for a kind of controller, as
        /// ControlsMenu.GetControllerMap.
        /// </summary>
        private RW.ControllerMap Map(RW.ControllerType type)
        {
            RW.Player p = null;
            if (Singleton<InputScript>.Instance != null)
            {
                p = Singleton<InputScript>.Instance.rewiredPlayer;
            }

            if (p == null)
            {
                return null;
            }

            int id = 0;
            if (type == RW.ControllerType.Joystick
                && Singleton<Globals>.Instance != null
                && Singleton<Globals>.Instance.currentJoystick != null)
            {
                id = Singleton<Globals>.Instance.currentJoystick.id;
            }

            return p.controllers.maps.GetMap(type, id, "Default", "Default");
        }

        /// <summary>
        /// The rows of the game's controls page (ControlsMenu.populate and
        /// CreateUIRow), with the same columns left out.
        /// </summary>
        private void BuildRows()
        {
            _rows = new List<Binding>();
            if (!RW.ReInput.isReady)
            {
                return;
            }

            var maps = new RW.ControllerMap[3];
            for (int c = 0; c < 3; c++)
            {
                maps[c] = Map(ColumnTypes[c]);
            }

            foreach (var a in RW.ReInput.mapping
                .UserAssignableActionsInCategory(0, true))
            {
                if (a.type == RW.InputActionType.Axis)
                {
                    AddRow(maps, a, RW.AxisRange.Full, a.descriptiveName);
                    string label;
                    if (!string.IsNullOrEmpty(a.positiveDescriptiveName))
                    {
                        label = a.positiveDescriptiveName;
                    }
                    else
                    {
                        label = a.descriptiveName + " +";
                    }

                    AddRow(maps, a, RW.AxisRange.Positive, label);
                    string negativeLabel;
                    if (!string.IsNullOrEmpty(a.negativeDescriptiveName))
                    {
                        negativeLabel = a.negativeDescriptiveName;
                    }
                    else
                    {
                        negativeLabel = a.descriptiveName + " -";
                    }

                    AddRow(maps, a, RW.AxisRange.Negative, negativeLabel);
                }
                else if (a.type == RW.InputActionType.Button)
                {
                    AddRow(maps, a, RW.AxisRange.Positive, a.descriptiveName);
                }
            }
        }

        private void AddRow(
            RW.ControllerMap[] maps, RW.InputAction a, RW.AxisRange range,
            string label)
        {
            if (a.descriptiveName == "Attack"
                || a.descriptiveName == "Context Menu"
                || a.descriptiveName == "RT")
            {
                return;
            }

            string name;
            if (range == RW.AxisRange.Full)
            {
                name = a.descriptiveName;
            }
            else if (range == RW.AxisRange.Positive)
            {
                name = a.positiveDescriptiveName;
            }
            else
            {
                name = a.negativeDescriptiveName;
            }

            var row = new Binding { Action = a, Range = range, Label = label };
            for (int c = 0; c < 3; c++)
            {
                row.Allowed[c] = !Skipped(c, name);
                if (!row.Allowed[c] || maps[c] == null)
                {
                    continue;
                }

                foreach (var em in maps[c].ElementMapsWithAction(a.id))
                {
                    if (!em.ShowInField(range))
                    {
                        continue;
                    }

                    row.Keys[c] = em.elementIdentifierName;
                    row.MapIds[c] = em.id;
                    break;
                }
            }

            _rows.Add(row);
        }

        private bool Skipped(int column, string name)
        {
            switch (column)
            {
                case 0:
                    return name == "Move Horizontal"
                        || name == "Move Vertical" || name == "Far Look"
                        || name == "Player Menu" || name == "Effects Cycle";
                case 1:
                    return name == "Move Horizontal"
                        || name == "Move Vertical" || name == "Move Left"
                        || name == "Move Right" || name == "Far Look" ||
                           name == "Player Menu" || name == "Effects Cycle"
                        || name == "Move Up" || name == "Move Down";
                default:
                    return name == "Move Left" || name == "Move Right"
                        || name == "Move Up" || name == "Move Down"
                        || name == "Run";
            }
        }

        private void DrawControls(Flow f)
        {
            if (_rows == null)
            {
                BuildRows();
            }

            var c = f.Row(Ui.RowH, 0, 0);
            bool rel = GameSettings.GetInt("RelativeControls") == 1;
            if (Ui.Check(c[0], rel, "Relative controls") != rel)
            {
                int v = 1;
                if (rel)
                {
                    v = 0;
                }

                SetSetting("RelativeControls", v);
                if (Player.Instance != null)
                {
                    Player.Instance.relativeControls = !rel;
                }
            }

            bool wheel = GameSettings.GetInt("MouseScrollHotbar") == 1;
            if (Ui.Check(c[1], wheel, "Mouse wheel scrolls the hotbar")
                != wheel)
            {
                int wheelSetting = 1;
                if (wheel)
                {
                    wheelSetting = 0;
                }

                SetSetting("MouseScrollHotbar", wheelSetting);
            }

            c = f.Row(Ui.RowH, 0, 90, 110);
            bool noPad = GameSettings.GetInt("DisableControllerMode") == 1;
            if (Ui.Check(c[0], noPad, "Turn the controller off") != noPad)
            {
                int padSetting = 1;
                if (noPad)
                {
                    padSetting = 0;
                }

                SetSetting("DisableControllerMode", padSetting);
                RW.ControllerMap pad = null;
                if (Menu != null)
                {
                    pad = Menu.ControlsMenu.gamepadMap;
                }

                if (pad != null)
                {
                    pad.enabled = noPad; // on again when it was off
                }
            }

            Ui.Label(c[1], "Aim assist");
            Choice(c[2], "aim", AimAssist, Mathf.Clamp(
                GameSettings.GetInt("AimAssist"), 0, 2), i =>
            {
                SetSetting("AimAssist", i);
                if (Menu != null)
                {
                    Menu.ControlsMenu.aimAssistMode = i;
                }
            });

            f.Space(4);

            // The list fills the panel above the status line.
            float statusH = PixelFont.Line + 4 * S;
            var panel = new Rect(
                f.Area.x, f.Y, f.Width,
                Mathf.Max(
                    60 * S,
                    f.Area.height - (f.Y - f.Area.y) - statusH - f.Gap));
            GUI.Box(panel, GUIContent.none, MenuSkin.Sunken);
            var inner = new Rect(
                panel.x + 2 * S, panel.y + 2 * S, panel.width - 4 * S,
                panel.height - 4 * S);
            float headH = PixelFont.Line + 6 * S,
                scrollW = 10 * S,
                rowH = PixelFont.Line + 6 * S;
            float[] widths = { 0, 120, 120, 120 };
            var head = Ui.Split(
                new Rect(inner.x, inner.y, inner.width - scrollW, headH),
                widths);
            string[] heads = { "Action", Columns[0], Columns[1], Columns[2] };
            for (int i = 0; i < 4; i++)
            {
                GUI.Box(
                    new Rect(
                        head[i].x, head[i].y, head[i].width + 4 * S,
                        head[i].height),
                    GUIContent.none, MenuSkin.Button);
                Ui.Label(
                    new Rect(
                        head[i].x + 4 * S, head[i].y, head[i].width - 4 * S,
                        head[i].height),
                    heads[i]);
            }

            var listArea = new Rect(
                inner.x, inner.y + headH, inner.width, inner.height - headH);
            var view = new Rect(
                0, 0, listArea.width - scrollW,
                Mathf.Max(listArea.height, _rows.Count * rowH));
            _listScroll = GUI.BeginScrollView(
                listArea, _listScroll, view, false,
                _rows.Count * rowH > listArea.height, GUIStyle.none,
                GUI.skin.verticalScrollbar);
            if (_rows.Count == 0)
            {
                Ui.Label(
                    new Rect(6 * S, 4 * S, view.width - 12 * S, rowH),
                    "The game's controls aren't loaded yet.", MenuSkin.Dim);
            }

            var rc = Ui.Split(new Rect(0, 0, view.width, rowH), widths);
            var maps = new RW.ControllerMap[3];
            for (int col = 0; col < 3; col++)
            {
                maps[col] = Map(ColumnTypes[col]);
            }

            for (int i = 0; i < _rows.Count; i++)
            {
                var b = _rows[i];
                float y = i * rowH;
                bool mappingThis = _mapRow == b;
                if (mappingThis)
                {
                    GUI.Box(
                        new Rect(0, y, view.width, rowH), GUIContent.none,
                        MenuSkin.Selected);
                }

                var labelBox =
                    new Rect(rc[0].x + 4 * S, y, rc[0].width - 4 * S, rowH);
                string fittedLabel =
                    PixelFont.Fit(b.Label, rc[0].width - 6 * S);
                Color labelColor;
                if (mappingThis)
                {
                    labelColor = MenuSkin.Bright;
                }
                else
                {
                    labelColor = MenuSkin.Text;
                }

                Ui.Label(labelBox, fittedLabel, labelColor);
                for (int col = 0; col < 3; col++)
                {
                    var cell = new Rect(
                        rc[col + 1].x, y + S, rc[col + 1].width, rowH - 2 * S);
                    if (!b.Allowed[col])
                    {
                        continue;
                    }

                    if (col == 2 || maps[col] == null)
                    {
                        // The game doesn't let you change the controller's
                        // buttons: shown only.
                        string key = b.Keys[col];
                        if (key == null)
                        {
                            key = "-";
                        }

                        Ui.Label(
                            new Rect(
                                cell.x + 4 * S, cell.y, cell.width - 4 * S,
                                cell.height),
                            PixelFont.Fit(key, cell.width - 6 * S),
                            MenuSkin.Dim);
                        continue;
                    }

                    bool active = mappingThis && _mapCol == col;
                    string text;
                    if (active)
                    {
                        if (col == 0)
                        {
                            text = "Press a key";
                        }
                        else
                        {
                            text = "Press a button";
                        }
                    }
                    else
                    {
                        text = b.Keys[col];
                        if (text == null)
                        {
                            text = "-";
                        }
                    }

                    if (Ui.Button(cell, PixelFont.Fit(text, cell.width
                                - 6 * S)) && _mapRow == null)
                    {
                        BeginMapping(b, col);
                    }
                }
            }

            GUI.EndScrollView();
            f.Y = panel.yMax + f.Gap;
            string status = _status;
            if (_mapRow != null)
            {
                float left = 0f;
                if (_mapper != null
                    && _mapper.status == RW.InputMapper.Status.Listening)
                {
                    left = _mapper.timeRemaining;
                }

                string hint = "Click the new mouse button";
                if (_mapCol == 0)
                {
                    hint = "Press the new key";
                }

                string timer = "";
                if (left > 0)
                {
                    timer = $" ({Mathf.CeilToInt(left)} s)";
                }

                status = $"{hint} for {_mapRow.Label}{timer}. Escape cancels.";
            }

            if (status == null)
            {
                status =
                    "Click a key or mouse button to change it. Controller "
                        + "buttons can't be changed.";
            }

            var statusBox = new Rect(f.Area.x + 2 * S, f.Y, f.Width, statusH);
            string fittedStatus = PixelFont.Fit(status, f.Width - 4 * S);
            Color statusColor;
            if (_statusWarn && _mapRow == null)
            {
                statusColor = MenuSkin.Warn;
            }
            else
            {
                statusColor = MenuSkin.Dim;
            }

            Ui.Label(statusBox, fittedStatus, statusColor);
            f.Y += statusH;
        }
    }
}
