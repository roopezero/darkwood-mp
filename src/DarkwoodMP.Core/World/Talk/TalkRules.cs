using System;

namespace DarkwoodMP.World.Talk
{
    /// One player at a time talks to an NPC (the plugin's TalkLock). The
    /// host keeps who holds each NPC's lock key; a joiner's dialogue opens
    /// at once and asks the host, which may refuse it.
    public static class TalkRules
    {
        /// Seconds after someone lets go of an NPC before another joiner
        /// may talk to them: the dialogue's changes are on the way.
        public const float Grace = 1.5f;

        /// How far from the NPC a joiner may be to talk to them.
        public const float Reach = 1500f;

        /// Tries at closing a refused dialogue while it is still opening.
        public const int Tries = 40;

        /// Seconds between two tries (unscaled).
        public const float RetryEvery = 0.25f;

        /// A name with this in it, any case, is the wolf's.
        public const string Wolf = "wolf";

        /// What a lock key by name starts with.
        public const string ByName = "npc:";

        /// The name an NPC is locked by when they are the same NPC in
        /// every game wherever they stand (a trader, the wolf), else "".
        /// One from the shared save goes by its ref instead: a new
        /// chapter-1 world has three "wolfman" traders.
        public static string LockName(in TalkNameFacts npc)
        {
            if (npc.Name == null)
            {
                return "";
            }

            bool wolf = npc.Name.IndexOf(
                Wolf,
                StringComparison.OrdinalIgnoreCase) >= 0;
            if ((npc.Trader || wolf) && !npc.Saved)
            {
                return npc.Name;
            }

            return "";
        }

        /// The lock key: by name when it has one, else the ObjRef as text
        /// (ObjRef.ToString(), Kind:Id).
        public static string Key(string name, string objRef)
        {
            if (name.Length > 0)
            {
                return ByName + name;
            }

            return objRef;
        }

        /// Whether someone other than who holds the key.
        public static bool HeldByOther(bool held, ulong holder, ulong who)
        {
            return held && holder != who;
        }

        /// Whether a release counts: only the holder's does. Then the key
        /// is free, and who let go and when are kept for the Grace.
        public static bool Releases(bool held, ulong holder, ulong who)
        {
            return held && holder == who;
        }

        /// Host: whether its own player's talkTo runs. It is refused,
        /// locally and with no grace or reach, only while a joiner holds
        /// the NPC: the host's game has every dialogue's changes already.
        public static Verdict HostMayTalk(in HostTalkFacts talk)
        {
            if (!talk.HasDialogue || !talk.WantsToTalk)
            {
                return Verdict.Allow;
            }

            if (!talk.Sharing || !talk.IsHost || !talk.RefValid)
            {
                return Verdict.Allow;
            }

            if (HeldByOther(talk.Held, talk.Holder, talk.Local))
            {
                return Verdict.Refuse(Refusal.OtherTalking, talk.HolderName);
            }

            return Verdict.Allow;
        }

        /// What happens when our player's dialogue opens.
        public static TalkOpen Opens(in TalkOpenFacts talk)
        {
            if (!talk.Sharing || talk.OursAlone || !talk.RefValid)
            {
                return TalkOpen.NotShared;
            }

            if (talk.IsHost)
            {
                return TalkOpen.Take;
            }

            return TalkOpen.Ask;
        }

        /// Host: a joiner's claim. Refused while another player holds the
        /// NPC, then while someone else let go less than Grace ago, then
        /// when the NPC is in the host's world and further than Reach.
        /// The requester's own lock and own release don't count. An NPC
        /// the host can't find isn't checked for reach.
        public static Verdict MayClaim(in TalkFacts claim)
        {
            if (HeldByOther(claim.Held, claim.Holder, claim.From))
            {
                return Verdict.Refuse(Refusal.OtherTalking, claim.HolderName);
            }

            if (claim.Released
                && claim.Releaser != claim.From
                && (claim.Now - claim.ReleasedAt).Value < Grace)
            {
                return Verdict.Refuse(Refusal.JustTalked, claim.ReleaserName);
            }

            if (!claim.Resolved)
            {
                return Verdict.Allow;
            }

            return ReachRules.InReach(claim.Reach with { Reach = Reach });
        }

        /// What happens when our player's dialogue closes.
        public static TalkEnd Ends(in TalkEndFacts talk)
        {
            if (!talk.Ours && !talk.OursValid)
            {
                return TalkEnd.None;
            }

            if (!talk.HasSession || !talk.OursValid)
            {
                return TalkEnd.Forget;
            }

            if (talk.IsHost)
            {
                return TalkEnd.Release;
            }

            if (!talk.ClientMode)
            {
                return TalkEnd.Flush;
            }

            return TalkEnd.Send;
        }

        /// Whether a world unloaded during our dialogue: a valid ref kept
        /// as ours, and its NPC gone. Then our dialogue closes (Ends), so
        /// the others aren't told we still talk to them.
        public static bool UnloadEnds(bool oursValid, bool ours)
        {
            return oursValid && !ours;
        }

        /// Client: one try at closing a dialogue the host refused.
        public static CloseTry CloseWhenReady(in TalkCloseFacts close)
        {
            if (!close.SameNpc)
            {
                return CloseTry.Stop;
            }

            if (!close.Opening)
            {
                return CloseTry.Close;
            }

            if (close.Tries > 0)
            {
                return CloseTry.Wait;
            }

            return CloseTry.Stop;
        }
    }
}
