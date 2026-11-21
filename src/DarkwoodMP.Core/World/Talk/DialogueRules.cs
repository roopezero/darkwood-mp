namespace DarkwoodMP.World.Talk
{
    /// Keeps a dialogue, and a trade, closable when its NPC goes away while
    /// it is open (the plugin's DialogueGuard). The game's close() does
    /// nothing once the NPC is destroyed, and closeTrade() throws.
    public static class DialogueRules
    {
        /// Seconds a dialogue may stay open with its NPC gone before it is
        /// closed by force.
        public const float StuckAfter = 4f;

        /// Seconds an NPC may be switched off, while playing together,
        /// before its dialogue is closed; and between two tries.
        public const float OffAfter = 1f;

        /// Each frame: what to do with the open dialogue, and the watch to
        /// keep. Destroyed closes by force once it isn't tweening; gone
        /// for StuckAfter closes by force even while tweening; switched off
        /// while together closes it the game's way.
        public static DialogueTick Watch(
            in DialogueFacts dialogue,
            DialogueWatch was)
        {
            if (!dialogue.Open)
            {
                return new DialogueTick();
            }

            var now = dialogue.Now;
            bool gone = dialogue.Gone;
            bool off = !gone && !dialogue.Active && dialogue.Together;
            var watch = new DialogueWatch
            {
                Gone = gone,
                GoneSince = GoneSince(gone, was, now),
                Off = off,
                OffSince = OffSince(off, was, now),
            };

            if (dialogue.Destroyed && !dialogue.Tweening)
            {
                return Act(DialogueAction.ForceDestroyed, watch);
            }

            if (gone && (now - watch.GoneSince).Value > StuckAfter)
            {
                return Act(DialogueAction.ForceGone, watch);
            }

            if (off
                && !dialogue.Tweening
                && (now - watch.OffSince).Value > OffAfter)
            {
                var tried = watch with { OffSince = now };
                return Act(DialogueAction.Close, tried);
            }

            return Act(DialogueAction.None, watch);
        }

        /// How to close a dialogue the game's way, without the NPC's
        /// goodbye line.
        public static DialogueClose CloseWay(in DialogueCloseFacts close)
        {
            if (!close.HasWindow || close.Tweening)
            {
                return DialogueClose.Busy;
            }

            if (close.Destroyed)
            {
                return DialogueClose.Force;
            }

            if (close.Cleared)
            {
                return DialogueClose.Cleared;
            }

            if (close.InShop)
            {
                return DialogueClose.CloseTrade;
            }

            return DialogueClose.Close;
        }

        /// What DialogueGuard.Close returns: whether it did something.
        public static bool Closed(DialogueClose way)
        {
            return way != DialogueClose.Busy && way != DialogueClose.Cleared;
        }

        /// Whether the game's closeTrade runs. With the NPC destroyed it
        /// would throw, so ForceClose instead, even while tweening.
        public static bool RunsCloseTrade(bool destroyed)
        {
            return !destroyed;
        }

        /// Whether the game's close or escPress runs. With the NPC
        /// destroyed, ForceClose instead, unless it is tweening.
        public static bool RunsClose(bool destroyed, bool tweening)
        {
            return !destroyed || tweening;
        }

        /// Whether the game's finishedElement runs. On a closed window it
        /// would throw on currentDialogue.name.
        public static bool RunsFinishedLine(in LineFacts line)
        {
            if (line.Elements > 0)
            {
                return true;
            }

            return line.HasDialogue && line.HasNpc && line.Active;
        }

        /// Whether ForceClose undoes a trade: in a shop, or the trading
        /// window is on.
        public static bool UndoesTrade(bool inShop, bool trading)
        {
            return inShop || trading;
        }

        /// Since when the NPC has been gone: kept while it lasts, now when
        /// it starts, zero when it isn't.
        static Seconds GoneSince(bool gone, DialogueWatch was, Seconds now)
        {
            if (!gone)
            {
                return default;
            }

            if (was.Gone)
            {
                return was.GoneSince;
            }

            return now;
        }

        /// The same for switched off.
        static Seconds OffSince(bool off, DialogueWatch was, Seconds now)
        {
            if (!off)
            {
                return default;
            }

            if (was.Off)
            {
                return was.OffSince;
            }

            return now;
        }

        static DialogueTick Act(DialogueAction action, DialogueWatch watch)
        {
            return new DialogueTick { Action = action, Watch = watch };
        }
    }
}
