using System;
using System.Collections;
using DarkwoodMP.Net;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using DarkwoodMP.World.Scripted;

namespace DarkwoodMP.World.Story
{
    /// <summary>
    /// A joiner's story steps: its StoryRequest, and the host's answer,
    /// held until its game can run the step.
    /// </summary>
    internal sealed partial class Chapters
    {
        private Seconds _lastRequest =
            new Seconds(StoryRequestRules.NeverAsked);

        /// <summary>
        /// Joiner: our player's event reached a story step; the host decides.
        /// </summary>
        private void SendStoryRequest(
            GameEvent e, GameObject thisGO, StoryKind kind)
        {
            var now = new Seconds(Time.realtimeSinceStartup);
            if (!StoryRequestRules.MaySend(now, _lastRequest))
            {
                return; // one event can hold the step more than once
            }

            _lastRequest = now;
            GameEvents ge = null;
            if (thisGO != null)
            {
                ge = thisGO.GetComponent<GameEvents>();
            }

            ObjRef r = default;
            if (ge != null)
            {
                r = ObjRef.Of(thisGO);
            }

            // the host checks our reach (a stage starts as we arrive): our
            // newest position first
            Sync.Players.PlayerSync instance = Plugin.Instance.Host.Players;
            if (instance != null)
            {
                instance.FlushNow();
            }

            var w = _coop.Net.Begin(MsgType.StoryRequest);
            int index = 0;
            if (ge != null)
            {
                index = Math.Max(0, ge.events.IndexOf(e));
            }

            int chapter = 0;
            if (kind == StoryKind.Chapter)
            {
                chapter = e.intValue;
            }

            StoryRequest.Write(w, new StoryAsk
            {
                Event = ObjRef.NameOf(r),
                Index = (byte)index,
                Kind = kind,
                Chapter = chapter,
            });

            string what = What(kind, e.intValue);
            _coop.Authority.Request(w, what, (ok, reason, result) =>
            {
                string notice = StoryRequestRules.NoticeFor(new ReplyFacts
                {
                    Ok = ok,
                    Reason = reason,
                    Result = result,
                    What = what,
                });

                if (notice != null)
                {
                    Plugin.Notice(notice);
                }
            }, inventory: false);
        }

        /// <summary>
        /// Host: a joiner's event reached a story step. Check it, then run it
        /// here. When the host can't run it just now (it is in a dream of its
        /// own, its world isn't ready, or its game is already on its way to
        /// another part of the story), the request is held and run once the
        /// host can (Update): the joiner's trigger has fired for good in every
        /// game, so a "no" then would lose the chapter for everyone. Only a
        /// request that doesn't fit the story (the wrong chapter, an event that
        /// doesn't do that, too far away) is refused.
        /// </summary>
        private void OnStoryRequest(ulong from, PacketReader r)
        {
            var s = _coop.Net;
            if (s == null || !s.IsHost)
            {
                return;
            }

            StoryAsk ask = StoryRequest.Read(r);
            if (!_coop.Authority.Handling)
            {
                return; // only as a Request
            }

            var h = new Held
            {
                From = from,
                Obj = ObjRef.RefOf(ask.Event),
                Index = ask.Index,
                Kind = ask.Kind,
                Chapter = ask.Chapter,
                WasAway = AwayNow(from)
            };

            StoryAnswer answer = Decide(h);
            string what = What(h.Kind, h.Chapter);
            switch (answer.Outcome)
            {
                case StoryOutcome.Refuse:
                    _coop.Authority.Refuse(answer.Verdict);
                    return;
                case StoryOutcome.Done:
                    _coop.Authority.Result(
                        StoryRequestRules.DonePrefix + answer.Reason);
                    return;
                case StoryOutcome.Wait:
                    // A repeat of the one held keeps it (and when it was first
                    // checked).
                    if (!HeldRules.Repeats(StoryOf(_held), StoryOf(h)))
                    {
                        Plugin.Log.LogInfo(
                            $"{s.NameOf(from)} set off {what}, which can't "
                                + $"run here yet ({answer.Reason}): holding "
                                + "it until it can");
                        _held = h;
                        _ready = false;
                    }

                    _coop.Authority.Result(
                        StoryRequestRules.PendingPrefix + answer.Reason);
                    return;
            }

            Plugin.Log.LogInfo(
                $"{s.NameOf(from)} set off {what}: running it here");
            if (!Run(h))
            {
                _coop.Authority.Refuse(
                    Verdict.Refuse(Refusal.RunFailed));
            }
        }

        /// <summary>
        /// Host: a joiner's story step that waits until the host's game can run
        /// it.
        /// </summary>
        private sealed class Held
        {
            public ulong From;
            public ObjRef Obj;
            public int Index, Chapter;
            public StoryKind Kind;
            public bool WasAway, ReachChecked;
            public string Key => StoryRequestRules.KeyOf(Kind, Chapter);
        }

        private Held _held;
        /// <summary>
        /// Host: the held step could run on every frame since _readySince.
        /// </summary>
        private bool _ready;
        private Seconds _readySince;
        /// <summary>
        /// Host: the story steps run for a joiner or started by our own game in
        /// this session (StoryRequestRules.KeyOf): none runs twice.
        /// </summary>
        private readonly System.Collections.Generic.HashSet<string>
            _ran = new System.Collections.Generic.HashSet<string>();

        /// <summary>Who set off a held step, and which (none: zero).</summary>
        private static HeldStory StoryOf(Held h)
        {
            if (h == null)
            {
                return default;
            }

            return new HeldStory
            {
                From = h.From,
                Kind = h.Kind,
                Chapter = h.Chapter,
            };
        }

        /// <summary>
        /// Whether a joiner is away in a place of their own now (false with no
        /// puppet for them).
        /// </summary>
        private static bool AwayNow(ulong player)
        {
            Sync.Players.RemotePlayer rp = null;
            if (Plugin.Instance.Host.Players != null
                && Plugin.Instance.Host.Players.Remotes.TryGetValue(
                    player, out var remote))
            {
                rp = remote;
            }

            return rp != null && rp.Away;
        }

        /// <summary>
        /// Host: the only code reading the game for a joiner's story step. The
        /// event is looked up only once the rules get as far as it.
        /// </summary>
        private StoryFacts FactsOf(Held h)
        {
            var facts = new StoryFacts
            {
                Kind = h.Kind,
                Chapter = h.Chapter,
                Ran = _ran.Contains(h.Key),
                OnTheWay = _chapterSent || Generating || _endSent,
                HostInWorld = WorldShare.InWorld && Player.Instance != null
                    && !WorldShare.Loading && WorldShare.WorldReady,
                Dreams = Prologue.DreamsOf(Singleton<Dreams>.Instance),
                HostChapter = HostChapter,
                ReachChecked = h.ReachChecked,
                WasAway = h.WasAway,
                IsAway = AwayNow(h.From),
            };

            if (!StoryRequestRules.ReachesEvent(facts))
            {
                return facts;
            }

            GameEvents ge = null;
            if (h.Obj.Valid)
            {
                ge = ObjRef.Resolve<GameEvents>(h.Obj);
            }

            if (ge == null)
            {
                return facts;
            }

            StoryStepFacts step = default;
            if (h.Index < ge.events.Count)
            {
                step = StepOf(ge.events[h.Index]);
            }

            ReachFacts reach = _coop.Authority.NearOf(
                h.From, ge.transform.position);
            return facts with
            {
                EventFound = true,
                EventName = ge.name,
                Step = step,
                Known = reach.Known,
                StandIn = reach.StandIn,
                EventAt = reach.Target,
            };
        }

        /// <summary>
        /// Host: what to do with a joiner's story step now. Its reach is
        /// checked once, from where they were then: a held request isn't
        /// refused because they moved on meanwhile.
        /// </summary>
        private StoryAnswer Decide(Held h)
        {
            StoryAnswer answer = StoryRequestRules.Decide(FactsOf(h));
            h.ReachChecked = answer.ReachChecked;
            return answer;
        }

        /// <summary>Host: run a joiner's story step in our game.</summary>
        private bool Run(Held h)
        {
            _ran.Add(h.Key);
            try
            {
                Singleton<Controller>.Instance.StartCoroutine(
                    StepFor(h.Kind, h.Chapter).fire(
                        Player.Instance.gameObject));
                return true;
            }
            catch (Exception e)
            {
                _ran.Remove(h.Key);
                Plugin.Log.LogWarning(
                    $"Couldn't run the story step: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// Host: run the held story step once our game can, and has for 3
        /// seconds (a dream's end settles).
        /// </summary>
        private void UpdateHeld()
        {
            var h = _held;
            if (h == null)
            {
                return;
            }

            var s = _coop.Net;
            bool here = s != null && s.IsHost && s.Peers.Contains(h.From);
            StoryAnswer answer = default;
            if (here)
            {
                answer = Decide(h);
            }

            HeldStep step = HeldRules.Next(new HeldFacts
            {
                PlayerHere = here,
                Outcome = answer.Outcome,
                Ready = _ready,
                ReadySince = _readySince,
                Now = new Seconds(Time.realtimeSinceStartup),
            });

            string what = What(h.Kind, h.Chapter);
            switch (step.Action)
            {
                case HeldAction.Dropped:
                    Plugin.Log.LogInfo(
                        $"Dropped the held {what}: its player left");
                    _held = null;
                    return;
                case HeldAction.Keep:
                    _ready = step.Ready;
                    _readySince = step.ReadySince;
                    return;
            }

            _held = null;
            _ready = false;
            switch (step.Action)
            {
                case HeldAction.Done:
                    Plugin.Log.LogInfo($"The held {what}: {answer.Reason}");
                    return;
                case HeldAction.Refused:
                    Plugin.Log.LogWarning(
                        $"Not running the held {what} after all: "
                            + answer.Words);
                    return;
            }

            Plugin.Log.LogInfo(
                $"{s.NameOf(h.From)} set off {what} earlier: running it here "
                    + "now");
            Run(h);
        }
    }
}
