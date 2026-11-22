using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DarkwoodMP.Net;
using DarkwoodMP.World;
using UnityEngine;

namespace DarkwoodMP.Menus
{
    /// <summary>
    /// The main menu's Start Game, in place of the game's profiles screen: one
    /// window to pick a save (or a new game), the new game's settings, and
    /// whether to host it.
    ///
    /// 1. Save: a list of the saved slots (the slot, the name you gave it, its
    ///    day, chapter and difficulty, when it was last played, and who has
    ///    played it with you, from SaveBook), and New Game in the first free
    ///    slot. With every slot in use, New Game asks which save to overwrite
    ///    (ConfirmDialog). A save can be renamed and deleted (as the game's own
    ///    Delete, asked first).
    /// 2. New game: the game's own choices (difficulty, skip the prologue,
    ///    backer content); off for a save, where they show what the save was
    ///    started with.
    /// 3. Host a multiplayer game: Steam or Direct, your name, the server's
    ///    name, port, password and most players, and what new players start
    ///    with. Off until the box is ticked.
    ///
    /// Start goes through the game's own code: a save loads as its Play button
    /// does (UI.initLoadGame), a new game starts as the profiles screen's new
    /// game does (the slot deleted, a new GameProfile, UI.initNewGame). With
    /// the box ticked the game hosts once the world has loaded
    /// (Plugin.HostWhenReady), as Multiplayer in the pause menu would.
    /// </summary>
    public sealed partial class StartGameWindow
    {
        private sealed class Slot
        {
            public int Id;
            // null: free (never used, deleted, or dead for good)
            public GameProfile Profile;
            public List<string> PlayedWith = new List<string>();
            public bool Free => Profile == null;
        }

        private readonly UiWindow _win;
        private readonly List<Slot> _slots = new List<Slot>();
        private List<GameProfile> _profiles;
        // the save list couldn't be read as the game would show it
        private string _problem;
        // a slot id, or 0 for New Game
        private int _choice = -1;
        // New Game with every slot in use: the slot to overwrite (asked first)
        private int _overwrite;
        private bool _listOpen;
        private string _name = "";
        private int _nameFor = -1;

        private GameProfile.Difficulty _difficulty =
            GameProfile.Difficulty.normal;
        private bool _skipPrologue;
        private bool _backer = true;
        private bool _host;

        public bool Open => _win.Open;

        private static ConfirmDialog Confirm => Plugin.Instance.Host.Confirm;

        public StartGameWindow()
        {
            _win = new UiWindow(0x0D41, "Start Game")
            {
                MinSize = new Vector2(380, 220),
                FitContent = true,
                DefaultRect = () => UiWindow.RightSide(
                    470 * S, 520 * S, top: 0.04f),
                Body = DrawBody,
                Footer = DrawFooter,
                FooterHeight = 13 + 8,
                OnClose = CommitName,
            };

            MenuWindows.OpenStartGame = Show;
            MenuWindows.Register(() => _win.Open);
            MenuWindows.Draw += OnGUI;
        }

        public void Show()
        {
            Refresh();
            _host = false;
            _listOpen = false;
            _win.ResetPlace();
            _win.Show();
            Plugin.Log.LogInfo(
                $"[start game] opened: {_slots.Count(s => !s.Free)} saves, "
                    + $"{_slots.Count(s => s.Free)} free slots");
        }

        public void Close() => _win.Close();

        /// <summary>
        /// The game's profiles, as its profiles screen reads them
        /// (SaveManager.loadGameProfiles).
        /// </summary>
        private void Refresh()
        {
            _slots.Clear();
            var sm = Singleton<SaveManager>.Instance;
            List<GameProfile> profiles = null;
            if (sm != null)
            {
                MainMenu.SaveState state = sm.loadGameProfiles();
                if (state != null)
                {
                    profiles = state.profiles;
                }
            }

            var saves = new List<SaveFacts>();
            if (profiles != null)
            {
                saves = profiles.Select(FactsOf).ToList();
            }

            bool files = sm != null && sm.CheckIfWorldFilesExist();
            _problem = StartGameRules.Problem(saves, files);

            // Five slots, as the game always has; a slot that isn't active (or
            // whose character died for good) is free.
            _profiles = new List<GameProfile>();
            int count = StartGameRules.SlotCount(saves.Count);
            for (int i = 0; i < count; i++)
            {
                var slot = new Slot { Id = i + 1 };
                if (i < saves.Count && StartGameRules.IsUsed(saves[i]))
                {
                    _profiles.Add(profiles[i]);
                    slot.Profile = profiles[i];
                    slot.PlayedWith = SaveBook.PlayedWith(slot.Id);
                }
                else
                {
                    _profiles.Add(new GameProfile(i + 1, _Active: false, 0));
                }

                _slots.Add(slot);
            }

            _choice = StartGameRules.Choice(_choice, SlotsFacts());
            _overwrite = 0;
            LoadName();
        }

        /// <summary>A profile from the game's list, for its slot.</summary>
        private static SaveFacts FactsOf(GameProfile profile)
        {
            if (profile == null)
            {
                return default;
            }

            return new SaveFacts
            {
                Present = true,
                Active = profile.Active,
                Dead = profile.dead,
            };
        }

        /// <summary>The slots listed, for the choice and a new game.</summary>
        private List<SlotFacts> SlotsFacts() => _slots
            .Select(slot => new SlotFacts
            {
                Id = slot.Id,
                Used = !slot.Free,
                Saved = SavedAt(slot.Profile),
            })
            .ToList();

        private static DateTime SavedAt(GameProfile p)
        {
            string timeSaved = null;
            if (p != null)
            {
                timeSaved = p.timeSaved;
            }

            if (DateTime.TryParse(timeSaved, out var t))
            {
                return t;
            }

            return DateTime.MinValue;
        }

        private Slot Chosen
        {
            get
            {
                if (_choice > 0)
                {
                    return _slots.FirstOrDefault(s => s.Id == _choice);
                }

                return null;
            }
        }

        /// <summary>
        /// The slot a new game goes in: the first free one, or the one chosen
        /// to overwrite.
        /// </summary>
        private int NewSlot =>
            StartGameRules.NewSlot(SlotsFacts(), _overwrite);

        private Slot FreeSlot => _slots.FirstOrDefault(s => s.Free);

        private void Choose(int choice)
        {
            CommitName();
            _choice = choice;
            _overwrite = 0;
            _listOpen = false;
            LoadName();
        }

        private void LoadName()
        {
            _nameFor = _choice;
            if (_choice > 0)
            {
                _name = SaveBook.NameOf(_choice);
            }
            else
            {
                _name = "";
            }
        }

        /// <summary>
        /// A save's new name is written when it's left (another choice, Start,
        /// closing the window).
        /// </summary>
        private void CommitName()
        {
            if (StartGameRules.CommitsName(_nameFor, _choice, Chosen != null))
            {
                SaveBook.SetName(_nameFor, _name);
            }
        }
    }
}
