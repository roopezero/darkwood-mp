using System;
using System.Collections.Generic;

namespace DarkwoodMP.Menus
{
    /// Start Game's decisions about the save slots and what Start does
    /// (StartGameWindow: Refresh, NewSlot, CantStart, Start, CommitName).
    public static class StartGameRules
    {
        /// The game always has five slots; a longer list shows them all.
        public const int Slots = 5;

        /// The warning over the list when the game's list of saves can't
        /// be read but its files are there.
        public const string Damaged = "The game's list of saves is missing or"
            + " damaged, but there are save files. The game's own profiles"
            + " screen can restore it from a backup.";

        /// How many slots the window lists for a list of saves of this
        /// length (0 when the game gave none).
        public static int SlotCount(int saves) => Math.Max(Slots, saves);

        /// A slot holds a save when its profile is there, active, and its
        /// character didn't die for good. Else the slot is free.
        public static bool IsUsed(in SaveFacts save) =>
            save.Present && save.Active && !save.Dead;

        /// The warning over the list, or null: no profile is active and the
        /// world's files exist (SaveManager.CheckIfWorldFilesExist; false
        /// with no SaveManager). An active profile whose character died
        /// counts as one, so it hides the warning while its slot is free.
        public static string Problem(
            IReadOnlyList<SaveFacts> saves,
            bool filesExist)
        {
            for (int i = 0; i < saves.Count; i++)
            {
                if (saves[i].Present && saves[i].Active)
                {
                    return null;
                }
            }

            if (filesExist)
            {
                return Damaged;
            }

            return null;
        }

        /// The choice once the list was read again: New Game (0) stays, a
        /// slot that still holds a save stays, and anything else becomes
        /// the save written last, or New Game when there is none. Of saves
        /// written at the same time, the first listed wins.
        public static int Choice(int choice, IReadOnlyList<SlotFacts> slots)
        {
            if (choice != 0 && !HoldsSave(slots, choice))
            {
                choice = -1;
            }

            if (choice >= 0)
            {
                return choice;
            }

            int latest = 0;
            var saved = DateTime.MinValue;
            for (int i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                if (slot.Used && (latest == 0 || slot.Saved > saved))
                {
                    latest = slot.Id;
                    saved = slot.Saved;
                }
            }

            return latest;
        }

        /// The slot a new game goes in: the first free one, else the one
        /// chosen to overwrite (0 when none was).
        public static int NewSlot(
            IReadOnlyList<SlotFacts> slots,
            int overwrite)
        {
            for (int i = 0; i < slots.Count; i++)
            {
                if (!slots[i].Used)
                {
                    return slots[i].Id;
                }
            }

            return overwrite;
        }

        /// Why Start is off, its reason shown beside it; Allow when it's
        /// on. The main menu is asked first, then the chosen save, or for
        /// New Game a slot to put it in.
        public static Verdict MayStart(in StartFacts start)
        {
            if (!start.MenuReady)
            {
                return Verdict.Refuse(Refusal.WaitForMainMenu);
            }

            if (start.Choice > 0)
            {
                if (!start.ChosenUsed)
                {
                    return Verdict.Refuse(Refusal.NoSaveInSlot);
                }

                if (!start.ChosenCompatible)
                {
                    return Verdict.Refuse(Refusal.NeedsProfilesScreen);
                }

                return Verdict.Allow;
            }

            if (start.NewSlot > 0)
            {
                return Verdict.Allow;
            }

            return Verdict.Refuse(Refusal.ChooseOverwrite);
        }

        /// Start hosts once the world has loaded when the host box is
        /// ticked and the player is in no game.
        public static bool HostsAfterStart(bool ticked, bool inGame) =>
            ticked && !inGame;

        /// A save's typed name is written when the name was loaded for a
        /// slot (above 0), that slot is still the choice, and it's listed.
        public static bool CommitsName(int nameFor, int choice, bool listed) =>
            nameFor > 0 && listed && nameFor == choice;

        /// A new game's typed name is written, after the game starts, only
        /// when it isn't blank.
        public static bool StoresNewName(string name) =>
            !string.IsNullOrWhiteSpace(name);

        static bool HoldsSave(IReadOnlyList<SlotFacts> slots, int id)
        {
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i].Id == id && slots[i].Used)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
