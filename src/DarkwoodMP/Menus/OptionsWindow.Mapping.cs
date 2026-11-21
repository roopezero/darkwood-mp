using System;
using System.Collections.Generic;
using UnityEngine;
using RW = Rewired;

namespace DarkwoodMP.Menus
{
    internal sealed partial class OptionsWindow
    {
        /// <summary>
        /// Start listening for a new key (once the mouse button that chose it
        /// is let go, so the click isn't taken).
        /// </summary>
        private void BeginMapping(Binding b, int col)
        {
            _mapRow = b;
            _mapCol = col;
            _startWhenReleased = true;
            _status = null;
            _statusWarn = false;
            SetAssigning(true);
        }

        /// <summary>
        /// The game's ControlsMenu.assigningMapping: while it's set the game's
        /// Escape and controller switch leave the keys alone. It's let go a few
        /// frames after the mapping ends, as the game does.
        /// </summary>
        private void SetAssigning(bool on)
        {
            ControlsMenu cm = null;
            if (Menu != null)
            {
                cm = Menu.ControlsMenu;
            }

            if (cm == null)
            {
                return;
            }

            if (on)
            {
                cm.assigningMapping = true;
            }
            else if (Singleton<Controller>.Instance != null)
            {
                Singleton<Controller>.Instance.waitFramesAndRun(() =>
                {
                    if (_mapRow == null)
                    {
                        cm.assigningMapping = false;
                    }
                }, 5);
            }
            else
            {
                cm.assigningMapping = false;
            }
        }

        private void EnsureMapper()
        {
            if (_mapper != null)
            {
                return;
            }

            _mapper = new RW.InputMapper();
            var o = _mapper.options;
            // As ControlsMenu.OnEnableIM sets them.
            o.ignoreMouseXAxis = true;
            o.ignoreMouseYAxis = true;
            o.allowKeyboardModifierKeyAsPrimary = true;
            o.allowKeyboardKeysWithModifiers = false;
            // Escape cancels; it's never taken as a key.
            o.isElementAllowedCallback = info => !(info.controllerType
                        == RW.ControllerType.Keyboard
                    && info.keyboardKey == KeyCode.Escape);
            _mapper.InputMappedEvent += OnMapped;
            _mapper.ConflictFoundEvent += d => _conflict = d;
            _mapper.TimedOutEvent += _ =>
            {
                Plugin.Log.LogInfo("[options] the mapper timed out");
                StopMapping(
                    "No key was pressed: nothing changed.", fromMapper: true);
            };

            _mapper.CanceledEvent += d =>
            {
                Plugin.Log.LogInfo(
                    $"[options] the mapper cancelled: {d.message}");
                StopMapping(null, fromMapper: true);
            };

            _mapper.ErrorEvent += d =>
            {
                Plugin.Log.LogInfo($"[options] the mapper failed: {d.message}");
                StopMapping(
                    $"Couldn't change it: {d.message}", warn: true,
                    fromMapper: true);
            };

            _mapper.StoppedEvent += _ => StopMapping(null, fromMapper: true);
        }

        private void UpdateMapping()
        {
            if (_mapRow == null)
            {
                return;
            }

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                _escTaken = Time.frameCount;
                StopMapping("Cancelled: nothing changed.");
                return;
            }

            if (_startWhenReleased && !Input.GetMouseButton(0)
                && !Input.GetMouseButton(1) && !Input.GetMouseButton(2))
            {
                _startWhenReleased = false;
                EnsureMapper();
                var map = Map(ColumnTypes[_mapCol]);
                if (map == null)
                {
                    StopMapping("That controller isn't there.", warn: true);
                    return;
                }

                var mapper = _mapper;
                var context = new RW.InputMapper.Context
                {
                    actionId = _mapRow.Action.id,
                    controllerMap = map,
                    actionRange = _mapRow.Range,
                };

                RW.ActionElementMap existing = null;
                if (_mapRow.MapIds[_mapCol] >= 0)
                {
                    existing = map.GetElementMap(_mapRow.MapIds[_mapCol]);
                }

                context.actionElementMapToReplace = existing;
                bool started = mapper.Start(context);
                string text = "didn't start";
                if (started)
                {
                    text = "started";
                }

                Plugin.Log.LogInfo(
                    "[options] listening for "
                        + $"{Columns[_mapCol].ToLowerInvariant()} input for "
                        + $"{_mapRow.Label}: {text}");
                if (!started)
                {
                    StopMapping("Couldn't listen for a key.", warn: true);
                }
            }

            // A key another action has: taken from it, unless that one can't be
            // changed (ControlsMenu.Update).
            if (_conflict != null && _mapper != null
                && _mapper.status == RW.InputMapper.Status.AwaitingResponse)
            {
                var data = _conflict;
                _conflict = null;
                string other = "", key = "";
                bool fixedOne = false;
                foreach (var c in data.conflicts)
                {
                    if (!c.isUserAssignable)
                    {
                        fixedOne = true;
                    }

                    if (other == "")
                    {
                        if (c.action != null)
                        {
                            other = c.action.descriptiveName;
                        }
                        else
                        {
                            other = "another action";
                        }

                        key = c.elementDisplayName;
                    }
                }

                if (fixedOne)
                {
                    data.responseCallback(
                        RW.InputMapper.ConflictResponse.Cancel);
                    StopMapping(
                        $"{key} is the game's own for {other}: nothing "
                            + "changed.",
                        warn: true);
                }
                else
                {
                    _status = $"{key} was taken from {other}.";
                    _statusWarn = true;
                    data.responseCallback(
                        RW.InputMapper.ConflictResponse.Replace);
                }
            }
        }

        private void OnMapped(RW.InputMapper.InputMappedEventData d)
        {
            string label = "";
            if (_mapRow != null)
            {
                label = _mapRow.Label;
            }

            string key = "?";
            if (d.actionElementMap != null)
            {
                key = d.actionElementMap.elementIdentifierName;
            }

            if (_status == null || !_statusWarn)
            {
                _status = $"{label}: {key}.";
                _statusWarn = false;
            }
            else
            {
                _status = $"{label}: {key}. " + _status;
            }

            Plugin.Log.LogInfo($"[options] {label} is now {key}");
            SaveControls();
            _mapRow = null;
            SetAssigning(false);
        }

        private bool _stopping;

        /// <summary>
        /// End the mapping. From one of the mapper's own events
        /// (<paramref name="fromMapper"/>) it has already stopped, so it isn't
        /// told to again: Stop raises the mapper's events, which call this, and
        /// _stopping keeps it from running again inside them.
        /// </summary>
        private void StopMapping(
            string status, bool warn = false, bool fromMapper = false)
        {
            if (_stopping)
            {
                return;
            }

            _stopping = true;
            try
            {
                bool was = _mapRow != null;
                _mapRow = null;
                _startWhenReleased = false;
                _conflict = null;
                if (status != null)
                {
                    _status = status;
                    _statusWarn = warn;
                }

                if (!fromMapper && _mapper != null
                    && _mapper.status != RW.InputMapper.Status.Idle)
                {
                    _mapper.Stop();
                }

                if (was)
                {
                    SetAssigning(false);
                }
            }
            finally
            {
                _stopping = false;
            }
        }

        /// <summary>
        /// The key maps into the game's store (ControlsMenu.Save), and the
        /// game's own copies of the maps refreshed.
        /// </summary>
        private void SaveControls()
        {
            RW.Interfaces.IUserDataStore userDataStore =
                RW.ReInput.userDataStore;
            if (userDataStore != null)
            {
                userDataStore.Save();
            }

            if (Menu != null && Menu.ControlsMenu != null)
            {
                Menu.ControlsMenu.loadMaps();
            }

            _rows = null;
        }

        /// <summary>
        /// The game's "revert controls": its default key maps, and the switches
        /// as a first start sets them.
        /// </summary>
        private void ResetControls()
        {
            StopMapping(null);
            ControlsMenu cm = null;
            if (Menu != null)
            {
                cm = Menu.ControlsMenu;
            }

            if (cm != null)
            {
                cm.revertControls();
            }
            else
            {
                var p = Singleton<InputScript>.Instance.rewiredPlayer;
                p.controllers.maps.LoadDefaultMaps(RW.ControllerType.Joystick);
                p.controllers.maps.LoadDefaultMaps(RW.ControllerType.Keyboard);
                p.controllers.maps.LoadDefaultMaps(RW.ControllerType.Mouse);
                RW.Interfaces.IUserDataStore userDataStore =
                    RW.ReInput.userDataStore;
                if (userDataStore != null)
                {
                    userDataStore.Save();
                }

                GameSettings.SetInt("RelativeControls", 0);
                GameSettings.SetInt("MouseScrollHotbar", 1);
                GameSettings.SetInt("DisableControllerMode", 0);
            }

            if (Player.Instance != null)
            {
                Player.Instance.relativeControls = false;
            }

            GameSettings.Save();
            _rows = null;
            _status = "The keys are back as the game has them.";
            _statusWarn = false;
            Plugin.Log.LogInfo("[options] controls reset to the defaults");
        }
    }
}
