using System.Linq;
using DarkwoodMP.Net;
using DarkwoodMP.World;
using UnityEngine;

namespace DarkwoodMP.Menus
{
    public sealed partial class StartGameWindow
    {
        // ---- drawing --------------------------------------------------------

        private void OnGUI()
        {
            if (!_win.Open)
            {
                return;
            }

            var e = Event.current;
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape
                && !Confirm.Open)
            {
                e.Use();
                Close();
                return;
            }

            _win.OnGUI();
        }

        private static float S => PixelFont.Scale;

        private static string Difficulty(GameProfile.Difficulty d)
        {
            if (d == GameProfile.Difficulty.hard)
            {
                return "Hard";
            }

            if (d == GameProfile.Difficulty.nightmare)
            {
                return "Nightmare";
            }

            return "Normal";
        }

        private static string Summary(GameProfile p)
        {
            string s = $"Day {p.day}";
            if (p.chapter > 0)
            {
                s += $", chapter {Mathf.Clamp(p.chapter, 1, 10)}";
            }

            return s + ", " + Difficulty(p.difficulty).ToLowerInvariant();
        }

        /// <summary>One line for a save in the list.</summary>
        private static string Line(Slot s)
        {
            string name = SaveBook.NameOf(s.Id);
            string with;
            if (s.PlayedWith.Count == 0)
            {
                with = "single-player";
            }
            else
            {
                with = "with " + string.Join(", ", s.PlayedWith);
            }

            string nameTag = "";
            if (name != "")
            {
                nameTag = $": {name}";
            }

            return $"Slot {s.Id}" + nameTag
                + $"  -  {Summary(s.Profile)}  -  {with}";
        }

        private string NewGameLine()
        {
            if (FreeSlot != null)
            {
                return $"New Game  (in slot {FreeSlot.Id})";
            }

            return $"New Game  (every slot is in use: overwrite one)";
        }

        private void DrawBody(Flow f)
        {
            Ui.Heading(f, "Save");
            if (_problem != null)
            {
                Ui.Text(f, _problem, MenuSkin.Warn);
                if (Ui.Button(f, "Open the game's profiles screen"))
                {
                    Close();
                    Singleton<MainMenu>.Instance.displayProfilesMenu();
                }
            }

            DrawList(f);
            if (_choice > 0)
            {
                DrawSave(f, Chosen);
            }
            else
            {
                DrawNewSlot(f);
            }

            Ui.Heading(f, "New game");
            DrawNewGame(f, _choice == 0);

            Ui.Heading(f, "Multiplayer");
            var hr = f.Next(Ui.RowH);
            bool canHost =
                Plugin.Instance != null && !Plugin.Instance.Host.Session.Active;
            _host =
                Check(hr, _host && canHost, "Host a multiplayer game", canHost);
            if (!canHost)
            {
                Ui.Text(
                    f,
                    "You're in a multiplayer game now. Leave it first (Join "
                        + "Game, Multiplayer).",
                    MenuSkin.Dim);
            }

            DrawHost(f, _host);
        }

        /// <summary>
        /// The save list: the chosen line, which opens the list under it
        /// (pushing the rest down).
        /// </summary>
        private void DrawList(Flow f)
        {
            var r = f.Next(Ui.RowH);
            bool hover = r.Contains(Event.current.mousePosition);
            if (GUI.Button(r, GUIContent.none, MenuSkin.Sunken))
            {
                _listOpen = !_listOpen;
            }

            string text;
            if (_choice > 0 && Chosen != null)
            {
                text = Line(Chosen);
            }
            else
            {
                text = NewGameLine();
            }

            float pad = 6 * S, arrow = 14 * S;
            var textBox =
                new Rect(r.x + pad, r.y, r.width - 2 * pad - arrow, r.height);
            string fittedText = PixelFont.Fit(text, r.width - 2 * pad - arrow);
            Color textColor;
            if (hover)
            {
                textColor = MenuSkin.Bright;
            }
            else
            {
                textColor = MenuSkin.Text;
            }

            Ui.Label(textBox, fittedText, textColor);
            string caret = "v";
            if (_listOpen)
            {
                caret = "^";
            }

            Ui.Label(
                new Rect(r.xMax - pad - arrow, r.y, arrow, r.height), caret,
                MenuSkin.Bright, TextAnchor.MiddleCenter);
            if (!_listOpen)
            {
                return;
            }

            var rows = _slots.Where(s => !s.Free).Select(s => (s.Id, Line(s)))
                .ToList();
            rows.Add((0, NewGameLine()));
            float rowH = PixelFont.Line + 6 * S;
            var box = new Rect(
                r.x, f.Y - f.Gap + S, r.width, rows.Count * rowH + 4 * S);
            GUI.Box(box, GUIContent.none, MenuSkin.Sunken);
            for (int i = 0; i < rows.Count; i++)
            {
                var row = new Rect(
                    box.x + 2 * S, box.y + 2 * S + i * rowH, box.width - 4 * S,
                    rowH);
                bool over = row.Contains(Event.current.mousePosition);
                bool chosen = rows[i].Item1 == _choice;
                if (Event.current.type == EventType.Repaint && (over || chosen))
                {
                    MenuSkin.Selected.Draw(row, false, false, false, false);
                }

                if (GUI.Button(row, GUIContent.none, MenuSkin.Plain))
                {
                    Choose(rows[i].Item1);
                }

                var rowBox = new Rect(
                    row.x + pad - 2 * S, row.y, row.width - 2 * pad,
                    row.height);
                string fittedRow =
                    PixelFont.Fit(rows[i].Item2, row.width - 2 * pad);
                Color rowColor;
                if (over || chosen)
                {
                    rowColor = MenuSkin.Bright;
                }
                else
                {
                    rowColor = MenuSkin.Text;
                }

                Ui.Label(rowBox, fittedRow, rowColor);
            }

            f.Y = box.yMax + f.Gap;
        }

        private void DrawSave(Flow f, Slot s)
        {
            if (s == null)
            {
                return;
            }

            var p = s.Profile;
            var c = f.Row(Ui.RowH, 90, 0, 110);
            Ui.Label(c[0], "Name");
            _name = Ui.Field(c[1], _name, 40);
            if (Ui.Button(c[2], "Delete save..."))
            {
                Confirm.Ask(
                    "Delete save",
                    $"Delete the save in slot {s.Id} ({Summary(p)})? It can't "
                        + "be undone.",
                    "Delete", () => Delete(s.Id));
            }

            Info(f, "Played", $"{Summary(p)};  last saved {p.timeSaved}");
            string text;
            if (s.PlayedWith.Count == 0)
            {
                text = "nobody: single-player";
            }
            else
            {
                text = string.Join(", ", s.PlayedWith);
            }

            Info(f, "Played with", text);
            if (!p.isCompatible)
            {
                Ui.Text(
                    f,
                    "This save is from an older version of the game "
                        + $"({p.versionText()}). Open it from the game's own "
                        + "profiles screen, which explains what can be done "
                        + "with it.",
                    MenuSkin.Warn);
            }
        }

        private static void Info(Flow f, string label, string text)
        {
            var lines = PixelFont.Wrap(text, f.Width - 94 * S);
            for (int i = 0; i < lines.Count; i++)
            {
                var c = Ui.Split(
                    new Rect(f.Area.x, f.Y, f.Width, PixelFont.Line), 90, 0);
                if (i == 0)
                {
                    Ui.Label(c[0], label, MenuSkin.Dim);
                }

                Ui.Label(c[1], lines[i], MenuSkin.Bright);
                f.Y += PixelFont.Line + S;
            }

            f.Y += f.Gap - S;
        }

        private void DrawNewSlot(Flow f)
        {
            var c = f.Row(Ui.RowH, 90, 0);
            Ui.Label(c[0], "Name");
            _name = Ui.Field(c[1], _name, 40);
            if (FreeSlot != null)
            {
                return;
            }

            Ui.Text(
                f,
                "Every save slot is in use. Choose a save to overwrite (it's "
                    + "deleted when the new game starts):",
                MenuSkin.Text);
            var used = _slots.Where(x => !x.Free).ToList();
            var widths = Enumerable.Repeat(0f, used.Count).ToArray();
            var b = f.Row(Ui.RowH, widths);
            for (int i = 0; i < used.Count; i++)
            {
                var s = used[i];
                if (Choice(b[i], $"Slot {s.Id}", _overwrite == s.Id, true)
                    && _overwrite != s.Id)
                {
                    int id = s.Id;
                    string label = SaveBook.NameOf(id);
                    string text = "";
                    if (label != "")
                    {
                        text = $" ({label})";
                    }

                    Confirm.Ask(
                        "Overwrite save",
                        $"Start the new game in slot {id}{text}? Its save "
                            + $"({Summary(s.Profile)}) is deleted when the "
                            + "game starts.",
                        "Overwrite", () => _overwrite = id);
                }
            }

            if (_overwrite > 0)
            {
                Ui.Text(
                    f,
                    $"The new game goes in slot {_overwrite}; its save is "
                        + "deleted when you press Start.",
                    MenuSkin.Warn);
            }
        }

        private void DrawFooter(Rect r)
        {
            var c = Ui.Split(r, 0, 110, 90);
            string why = CantStart();
            if (why != null)
            {
                Ui.Label(c[0], PixelFont.Fit(why, c[0].width), MenuSkin.Dim);
            }

            string text = "Start";
            if (_host)
            {
                text = "Start and host";
            }

            if (Ui.Button(c[1], text, why == null))
            {
                Start();
            }

            if (Ui.Button(c[2], "Cancel"))
            {
                Close();
            }
        }

        /// <summary>Why Start is off, or null.</summary>
        private string CantStart()
        {
            Verdict verdict = StartGameRules.MayStart(StartOf());
            if (!verdict.Refused)
            {
                return null;
            }

            return Refusals.Text(verdict);
        }

        /// <summary>What Start looks at: the menu and the choice.</summary>
        private StartFacts StartOf()
        {
            Slot chosen = Chosen;
            bool used = chosen != null && chosen.Profile != null;
            return new StartFacts
            {
                MenuReady = WorldShare.MainMenuReady(),
                Choice = _choice,
                ChosenUsed = used,
                ChosenCompatible =
                    used && chosen.Profile.isCompatible != false,
                NewSlot = NewSlot,
            };
        }
    }
}
