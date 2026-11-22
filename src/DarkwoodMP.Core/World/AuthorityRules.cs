using System.Collections.Generic;
using DarkwoodMP.Net;

namespace DarkwoodMP.World
{
    /// The host decides what a client's player does to the shared world:
    /// the client sends it as a Request, and the host's handler builds the
    /// answer (the plugin's Authority). These are its decisions.
    public static class AuthorityRules
    {
        /// The most bytes a request's message may hold (Authority._copy).
        public const int MaxRequest = 1 << 16;

        /// Messages an honest client sends only inside a Request: their
        /// handlers check them only there (InReach, Refuse), so the host
        /// drops one that comes from a client on its own.
        static readonly HashSet<MsgType> RequestOnly = new HashSet<MsgType>
        {
            MsgType.WorldAction, MsgType.EnemyHit, MsgType.NpcHit,
            MsgType.NpcAttack, MsgType.Burning,
            MsgType.Construct, MsgType.EventFired, MsgType.StoryRequest,
            MsgType.EggDestroyed, MsgType.InfectionCleared,
            MsgType.TraderTrade, MsgType.StorageSort, MsgType.SkillSpend,
            MsgType.EssenceAdd, MsgType.SkillReset,
            MsgType.PorterTransport, MsgType.PorterCalled, MsgType.OvenLit,
            MsgType.Fuel, MsgType.DragClaim,
        };

        /// Host: whether a client's message may be handled as it came, not
        /// inside a request. Inside one (handling) any message may.
        public static bool TakesBare(MsgType type, bool handling)
        {
            return handling || !RequestOnly.Contains(type);
        }

        /// Client: whether a message this long may go as a Request. A
        /// longer one isn't sent, and its callback never runs.
        public static bool Fits(int length)
        {
            return length <= MaxRequest;
        }

        /// Host: whether the message inside a request may be handled. An
        /// empty one, or one that is itself a Request (nested), may not.
        public static Verdict MayHandle(int length, bool nested)
        {
            if (length == 0 || nested)
            {
                return Verdict.Refuse(Refusal.EmptyRequest);
            }

            return Verdict.Allow;
        }

        /// Host: the answer to a request from this player, before its
        /// handler runs.
        public static RequestAnswer Begin(ulong from)
        {
            return new RequestAnswer
            {
                Handling = true,
                From = from,
                Result = "",
            };
        }

        /// Host: the handler is done. Who asked, the refusal and the
        /// result stay as they were.
        public static RequestAnswer End(in RequestAnswer answer)
        {
            return answer with { Handling = false };
        }

        /// Host, in a handler: refuse the request. The first refusal wins;
        /// outside a request nothing changes.
        public static RequestAnswer Refuse(
            in RequestAnswer answer,
            Verdict refusal)
        {
            if (!answer.Handling || answer.Verdict.Refused)
            {
                return answer;
            }

            return answer with { Verdict = refusal };
        }

        /// Host, in a handler: what came of the request, decided here. The
        /// last result wins; outside a request nothing changes.
        public static RequestAnswer Result(
            in RequestAnswer answer,
            string result)
        {
            if (!answer.Handling)
            {
                return answer;
            }

            return answer with { Result = result };
        }

        /// Host, in a handler: the request is about a place only the
        /// requester has (RequesterAway). Any refusal is taken back, and
        /// the result tells them so. Outside a request nothing changes.
        public static RequestAnswer AcceptAway(
            in RequestAnswer answer,
            string result)
        {
            if (!answer.Handling)
            {
                return answer;
            }

            return answer with { Verdict = Verdict.Allow, Result = result };
        }

        /// Host, in a handler: the requesting player is where the host's
        /// game doesn't run (their stand-in is Away), so what they did
        /// can't be checked here.
        public static bool RequesterAway(bool handling, bool known, bool away)
        {
            return handling && known && away;
        }
    }
}
