namespace DarkwoodMP.World.Skills
{
    /// The Skills panel's decisions (SkillsPanel): when it opens and
    /// closes, what a click picks, which cells can be picked, what the
    /// footer and the details say, and which game input it takes.
    public static class SkillPanelRules
    {
        /// The game's mouse actions the panel takes while the mouse is
        /// pressed on it (SkillsPanel.MouseActions).
        static readonly string[] MouseActions =
        {
            "Primary Action",
            "Aim",
            "Secondary Attack",
            "Attack",
        };

        /// Why the panel can't open now (SkillsPanel.CanOpen); None: it
        /// can. Logged, never sent.
        public static SkillNote CanOpen(in PanelFacts panel)
        {
            if (!panel.HasPlayer
                || !panel.InWorld
                || panel.Loading
                || panel.MainMenu
                || panel.LoadingGame)
            {
                return SkillNote.NotInWorld;
            }

            if (panel.Paused
                || panel.ForbidInputs
                || panel.InMenu
                || panel.Dying
                || panel.Health <= 0f)
            {
                return SkillNote.Busy;
            }

            if (panel.Watching)
            {
                return SkillNote.Watching;
            }

            if (!panel.HasTiers)
            {
                return SkillNote.NoSkills;
            }

            return SkillNote.None;
        }

        /// Whether the open panel closes by itself this frame (the start of
        /// SkillsPanel.Update). Fewer checks than CanOpen: not health,
        /// Loading, forbidInputs or watching.
        public static bool ClosesNow(in PanelFacts panel)
        {
            return panel.Open
                && (!panel.InWorld
                    || panel.MainMenu
                    || !panel.HasPlayer
                    || panel.InMenu
                    || panel.Paused
                    || panel.Dying);
        }

        /// Whether SkillsPanel.Update reads the panel's key this frame: not
        /// while the co-op menu shows or an IMGUI control has the keyboard,
        /// and not when the key is KeyCode.None.
        public static bool ReadsKey(in KeyFacts key)
        {
            return !key.CoopMenuOpen && !key.KeyboardTaken && !key.Off;
        }

        /// Whether a click on a cell counts: the skill can be picked and
        /// isn't learned.
        public static bool TakesClick(bool allowed, bool learned)
        {
            return allowed && !learned;
        }

        /// What is picked after a click that counts. The skill clicked is
        /// picked, or unpicked if it was. With both picked from different
        /// tiers, the one not clicked is dropped; with both picked and the
        /// good skill needing no negative one, the negative one is dropped.
        public static SkillPicks Pick(in PickFacts pick)
        {
            string good = pick.Good;
            string negative = pick.Negative;
            if (pick.ClickedNegative)
            {
                negative = Toggled(negative, pick.Clicked);
            }
            else
            {
                good = Toggled(good, pick.Clicked);
            }

            if (good != null && negative != null)
            {
                int goodTier = SkillRules.Find(pick.Tiers, good).Tier;
                int negativeTier = SkillRules.Find(pick.Tiers, negative).Tier;
                if (goodTier != negativeTier)
                {
                    if (pick.ClickedNegative)
                    {
                        good = null;
                    }
                    else
                    {
                        negative = null;
                    }
                }
            }

            if (good != null
                && negative != null
                && pick.Chosen != null
                && !SkillRules.NeedsNegative(pick.Tiers, pick.Chosen, good))
            {
                negative = null;
            }

            return new SkillPicks { Good = good, Negative = negative };
        }

        /// Whether a good skill's cell can be picked: MayTake allows it.
        /// Choice.Good is the cell's skill.
        public static bool GoodAllowed(in ChoiceFacts choice)
        {
            return !SkillRules.MayTake(choice).Refused;
        }

        /// Whether a negative skill's cell can be picked. Choice.Negative is
        /// the cell's skill and Choice.Good the good skill picked (or null):
        /// with one picked, only when it needs a negative skill, of its
        /// tier; with none, any negative skill a tier still owes.
        public static bool NegativeAllowed(in ChoiceFacts choice)
        {
            if (choice.Good != null
                && !SkillRules.NeedsNegative(
                    choice.Tiers,
                    choice.Chosen,
                    choice.Good))
            {
                return false;
            }

            return !SkillRules.MayTakeNegative(choice).Refused;
        }

        /// The negative skill a choice sends with the good one: the one
        /// picked, only when the good skill needs it (SkillsPanel's
        /// needNeg); null otherwise.
        public static string NegativeSent(in ChoiceFacts choice)
        {
            if (choice.Good != null
                && SkillRules.NeedsNegative(
                    choice.Tiers,
                    choice.Chosen,
                    choice.Good))
            {
                return choice.Negative;
            }

            return null;
        }

        /// Whether the Learn button is on: a good skill picked, no choice
        /// waiting, and Check allows it with the negative skill it sends.
        public static bool Ready(in ChoiceFacts choice, bool waiting)
        {
            if (choice.Good == null || waiting)
            {
                return false;
            }

            var sent = choice with { Negative = NegativeSent(choice) };
            return !SkillRules.Check(sent).Refused;
        }

        /// What the footer says (SkillsPanel.DrawFooter): the last Learn's
        /// reason first, then waiting for the host, then the points or the
        /// negative skill still to pick.
        public static PanelNote Footer(in FooterFacts footer)
        {
            if (footer.HasMessage)
            {
                return PanelNote.Message;
            }

            if (footer.Waiting)
            {
                return PanelNote.Asking;
            }

            if (footer.Good == null)
            {
                if (footer.Points > 0)
                {
                    return PanelNote.None;
                }

                return PanelNote.NoPoints;
            }

            if (footer.NeedsNegative && footer.Negative == null)
            {
                return PanelNote.ChooseNegative;
            }

            return PanelNote.None;
        }

        /// What the details say about a skill shown (the one under the
        /// mouse, else the good, else the negative one picked). Uses the
        /// tiers, the level and the skills had; not the picks.
        public static SkillDetail Detail(in ChoiceFacts choice, string show)
        {
            if (choice.Chosen != null && choice.Chosen.Contains(show))
            {
                return new SkillDetail { Note = PanelNote.Learned };
            }

            var spot = SkillRules.Find(choice.Tiers, show);
            if (spot.Negative)
            {
                var negative = SkillRules.MayTakeNegative(
                    choice with { Good = null, Negative = show });
                if (negative.Refused)
                {
                    return Refused(negative);
                }

                return new SkillDetail { Note = PanelNote.TakenWithFirst };
            }

            var good = SkillRules.MayTake(choice with { Good = show });
            if (good.Refused)
            {
                return Refused(good);
            }

            if (SkillRules.NeedsNegative(choice.Tiers, choice.Chosen, show))
            {
                return new SkillDetail { Note = PanelNote.ComesWithNegative };
            }

            return new SkillDetail { Note = PanelNote.CanBeLearned };
        }

        /// Whether the game's read of this action may be taken by the panel
        /// (SkillsPanel.EatsAction's first check): the panel is open and the
        /// action is one of the game's mouse actions. PanelPress.Eats says
        /// whether it is.
        public static bool WatchesAction(bool open, string action)
        {
            if (!open || action == null)
            {
                return false;
            }

            foreach (var each in MouseActions)
            {
                if (each == action)
                {
                    return true;
                }
            }

            return false;
        }

        /// Whether Escape closes the panel instead of reaching the game
        /// (InputScriptOnPressEsc): open, and no film to skip (forbidInputs).
        public static bool TakesEscape(bool open, bool forbidInputs)
        {
            return open && !forbidInputs;
        }

        static string Toggled(string picked, string clicked)
        {
            if (picked == clicked)
            {
                return null;
            }

            return clicked;
        }

        static SkillDetail Refused(Verdict verdict)
        {
            return new SkillDetail
            {
                Note = PanelNote.Refused,
                Verdict = verdict,
            };
        }
    }
}
