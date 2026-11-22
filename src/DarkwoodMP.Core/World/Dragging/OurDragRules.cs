namespace DarkwoodMP.World.Dragging
{
    /// Our own player's drag (the plugin's DragSync): taking hold, the
    /// host's answer, its moves going out, and letting go.
    public static class OurDragRules
    {
        /// Seconds between two moves of our drag.
        public const float SendInterval = 0.1f;

        /// Seconds after our player let go that its end goes out again,
        /// once it has settled.
        public const float SettleAfter = 1.5f;

        /// How a refusal that names the owner ends (Refusal.OtherMoving).
        /// The client tells it from the others by this, so it is part of
        /// the protocol in practice.
        public const string MovingSuffix = "is moving it";

        /// Our player has just taken hold of a thing. Someone else's drag
        /// is refused before anything is sent; a thing shown following
        /// another game's move is ours from now on unless refused.
        public static StartAction Start(in StartFacts start)
        {
            if (start.Other != 0)
            {
                return StartAction.Refused;
            }

            if (!start.Named)
            {
                return StartAction.Unnamed;
            }

            if (start.Host)
            {
                return StartAction.Own;
            }

            return StartAction.Claim;
        }

        /// The host answered our claim. Only a refusal ending in "is moving
        /// it" is shown as it is; any other (the thing isn't there, out of
        /// reach, the session ended) shows the game's "too far".
        public static ClaimReply Reply(in ReplyFacts reply)
        {
            if (!reply.Same)
            {
                return ClaimReply.Ignore;
            }

            if (reply.Ok)
            {
                return ClaimReply.Claimed;
            }

            if (reply.Reason.EndsWith(MovingSuffix))
            {
                return ClaimReply.ShowReason;
            }

            return ClaimReply.TooFar;
        }

        /// Client, a DragOwner from the host. The entry changes whatever
        /// our player drags; our drag is claimed when we are the owner, and
        /// otherwise let go of only when the message says so. A claim
        /// still waiting doesn't change this.
        public static OwnerNews News(in NewsFacts news)
        {
            if (!news.Client || !news.FromHost || !news.InWorld)
            {
                return default;
            }

            var entry = OwnerEntry.Remember;
            if (news.Owner == 0)
            {
                entry = OwnerEntry.Forget;
            }

            if (!news.Ours)
            {
                return new OwnerNews { Entry = entry };
            }

            var drag = ClaimReply.Ignore;
            if (news.Owner == news.Local)
            {
                drag = ClaimReply.Claimed;
            }
            else if (news.LetGo && news.Owner != 0)
            {
                drag = ClaimReply.Moving;
            }
            else if (news.LetGo)
            {
                drag = ClaimReply.TooFar;
            }

            return new OwnerNews { Entry = entry, Drag = drag };
        }

        /// Our player let go: only our own entry is removed, and the host
        /// tells everyone. Looks at Named, Owned, Owner, Local and Host.
        public static LetGoEntry LetsGo(in OwnerFacts owner)
        {
            if (!owner.Named || !owner.Owned || owner.Owner != owner.Local)
            {
                return LetGoEntry.Keep;
            }

            if (owner.Host)
            {
                return LetGoEntry.Tell;
            }

            return LetGoEntry.Forget;
        }

        /// Host, each frame our player drags something: whether our own
        /// entry for it is marked heard now, so the host's own drag never
        /// goes stale. Looks at Owned, Owner, Local and Host; not at Named,
        /// as the shell doesn't.
        public static bool KeepsOursHeard(in OwnerFacts owner)
        {
            if (!owner.Host || !owner.Owned)
            {
                return false;
            }

            return owner.Owner == owner.Local;
        }

        /// Where our copy is held still when our player lets go at once:
        /// at the host's last word, else where it is known to be, else
        /// where it is; not at all when it is shown moving already.
        public static HoldAt Hold(in HoldFacts hold)
        {
            if (hold.Shown)
            {
                return HoldAt.None;
            }

            if (hold.HostWord)
            {
                return HoldAt.HostWord;
            }

            if (hold.Known)
            {
                return HoldAt.Known;
            }

            return HoldAt.Here;
        }

        /// Whether our drag's next move goes out now: a named drag of the
        /// host's, or one the host agreed to, once it is due. Not last, and
        /// unreliable.
        public static bool SendDue(in SendFacts send)
        {
            if (!send.Dragging || !send.Named)
            {
                return false;
            }

            if (!send.Host && !send.Claimed)
            {
                return false;
            }

            return Due(send.Now, send.Next);
        }

        /// Whether a time kept for later has come (now at it or past).
        public static bool Due(Seconds now, Seconds at)
        {
            return now.Value >= at.Value;
        }

        /// When the move after one sent now is due.
        public static Seconds NextSend(Seconds now)
        {
            return new Seconds(now.Value + SendInterval);
        }

        /// When the end of a drag let go of now goes out again.
        public static Seconds SettleAt(Seconds now)
        {
            return new Seconds(now.Value + SettleAfter);
        }
    }
}
