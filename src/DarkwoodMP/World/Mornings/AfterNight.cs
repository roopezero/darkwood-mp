using DarkwoodMP.Net;
using UnityEngine;

namespace DarkwoodMP.World.Mornings
{
    /// <summary>
    /// The morning after a night (`Controller.startAfterNight`): each game puts
    /// its trader or the wolf in its own player's hideout and freezes its time
    /// until that player walks out (`endAfterNight`, which also takes the
    /// trader away). The clock is the host's, so the after-night is over for
    /// everyone when the host's ends: the host sends AfterNightEnded, and each
    /// joiner still in its after-night ends it too, so its trader leaves and it
    /// can't keep trading with a copy the host no longer has. A joiner who
    /// walks out first ends it the same way: it tells the host
    /// (AfterNightLeft), and the host ends its own morning, which sends
    /// AfterNightEnded to everyone; the host sends it itself when it has no
    /// morning of its own. So the trader leaves every game when the first
    /// player walks out (in the game the trader goes when the player leaves;
    /// here it is one morning for everyone).
    /// A player talking to the trader (or the wolf) then has that dialogue (and
    /// trade) closed first, and its morning ends once it has: endAfterNight
    /// destroys the trader, and the game can't close a dialogue whose NPC is
    /// gone (issue #15, see DialogueGuard).
    /// Once the morning is over, the host remembers it for that day: a joiner
    /// whose morning starts afterwards (one that joined late, or whose clock
    /// reached dawn after the first player walked out) is told it is over,
    /// until the host's next dawn (startAfterNight).
    /// The trader and the wolf still appear per game, at each player's hideout
    /// (they are NPCs, which run in every game).
    ///
    /// The clock: the after-night's timeFreeze effect stops the host's clock
    /// (Controller.DoUpdateTime), and the host's clock is everyone's. So the
    /// host lets its clock run again as soon as a joiner is out of their
    /// hideout (their PlayerState says whether they are in an after-night), and
    /// stops it again if they all go back into one. The host's own after-night,
    /// trader and colour stay as they are until the host walks out.
    ///
    /// The trader's reputation reward counts once a day, not once per game
    /// (FlagsSync.LocalReward).
    ///
    /// What happens is the core's MorningRules; AfterNightPatches hand the
    /// game's calls here.
    /// </summary>
    internal sealed class AfterNight
    {
        private readonly CoopSession _coop;

        /// <summary>
        /// Host: whether we turned the clock back on during our own
        /// after-night, and since when it has been frozen.
        /// </summary>
        private ClockState _clock;

        /// <summary>
        /// Host: the day whose morning is over (someone walked out of their
        /// hideout), and when each joiner still in it was last told.
        /// </summary>
        private readonly LateMornings _late = new LateMornings();

        /// <summary>
        /// The morning ended in another game while we talked to the trader;
        /// ours ends once that dialogue closes.
        /// </summary>
        private bool _endPending;
        private Seconds _pendingSince;

        public AfterNight(CoopSession coop)
        {
            _coop = coop;
            coop.Handle(MsgType.AfterNightEnded, OnEnded);
            coop.Handle(MsgType.AfterNightLeft, OnLeft);
        }

        private Session S => _coop.Net;

        private static Seconds Now => new Seconds(Time.unscaledTime);

        /// <summary>
        /// Whether this game's player is in an after-night (for PlayerState).
        /// </summary>
        public static bool Local
        {
            get
            {
                var c = Singleton<Controller>.Instance;
                return c != null && c.isAfterNight;
            }
        }

        /// <summary>
        /// Host: a new dawn in our game (startAfterNight runs at each dawn):
        /// today's morning isn't over yet.
        /// </summary>
        public void HostDawn()
        {
            if (!S.IsHost)
            {
                return;
            }

            _late.Dawn();
        }

        private static string NameOf(Session s, ulong id)
        {
            if (s.Names.TryGetValue(id, out var n))
            {
                return n;
            }

            return id.ToString();
        }

        /// <summary>
        /// Every frame: a waiting end; and on the host, the late joiners and
        /// the clock during our after-night while a joiner is out of their
        /// hideout.
        /// </summary>
        public void Update()
        {
            var c = Singleton<Controller>.Instance;
            var p = Player.Instance;
            PendingUpdate();
            if (c == null || p == null || !S.IsHost)
            {
                return;
            }

            TellLateMornings(c);
            ClockStep step = MorningRules.Clock(ClockOf(c, p), _clock);
            _clock = step.Clock;
            if (step.Change == ClockChange.Run)
            {
                c.DoUpdateTime = true;
                Plugin.Log.LogInfo(
                    "[world] a joiner is out of their hideout: the clock runs "
                        + "on during our after-night");
            }
            else if (step.Change == ClockChange.Stop)
            {
                c.DoUpdateTime = false;
                Plugin.Log.LogInfo(
                    "[world] every joiner is in an after-night: the clock "
                        + "stops again until we walk out");
            }
        }

        private ClockFacts ClockOf(Controller c, Player p)
        {
            bool frozen = c.isAfterNight && p.effects != null
                && p.effects.hasEffectType(CharacterEffectType.timeFreeze);
            var sync = Plugin.Instance.Host.Players;
            bool anyoneOut = false;
            if (frozen && sync != null)
            {
                foreach (var kv in sync.Remotes)
                {
                    var last = kv.Value.Last;
                    bool dummy = kv.Key == Sync.Players.PlayerSync.DummyId;
                    bool inMorning = last != null && last.AfterNight;
                    if (MorningRules.IsOut(dummy, last != null, inMorning))
                    {
                        anyoneOut = true;
                        break;
                    }
                }
            }

            return new ClockFacts
            {
                Frozen = frozen,
                Now = Now,
                InCoop = _coop.Pause.InCoop,
                HasSync = sync != null,
                AnyoneOut = anyoneOut,
            };
        }

        /// <summary>
        /// Controller.endAfterNight ended our morning: on the host, unless
        /// killing the trader did (NpcDied ends it in each game, and leaves
        /// the body there), the day's morning is over and everyone is told.
        /// </summary>
        public void Ended(Controller c, bool before, bool killed)
        {
            if (!MorningRules.EndedHere(before, c.isAfterNight, killed))
            {
                return;
            }

            var facts = new HostEndFacts
            {
                IsHost = S.IsHost,
                InWorld = WorldShare.InWorld,
                Replaying = _coop.Guards.Replaying,
                HasController = Singleton<Controller>.Instance != null,
                Peers = S.Peers.Count,
            };

            HostEndStep step = MorningRules.HostEnded(facts);
            if (step.MarkOver)
            {
                _late.Over(Singleton<Controller>.Instance.day);
            }

            if (!step.Send)
            {
                return;
            }

            S.Broadcast(
                S.Begin(MsgType.AfterNightEnded), Channel.Control,
                reliable: true);
            Plugin.Log.LogInfo(
                "[world] the host's after-night is over: sent to the others");
        }

        /// <summary>
        /// Location.OnTriggerExit, after it ran: a joiner walking out of its
        /// hideout in the morning (endAfterNight) tells the host, which ends
        /// the morning everywhere. A morning ended any other way (a death's
        /// reset, the host's end) isn't sent. before: whether we were in a
        /// morning as it began.
        /// </summary>
        public void TriggerExited(bool before)
        {
            var c = Singleton<Controller>.Instance;
            var facts = new WalkOutFacts
            {
                Before = before,
                HasController = c != null,
                After = c != null && c.isAfterNight,
                IsClient = S.IsClient,
                InWorld = WorldShare.InWorld,
                Replaying = _coop.Guards.Replaying,
            };

            if (!MorningRules.WalkedOut(facts))
            {
                return;
            }

            S.SendToHost(
                S.Begin(MsgType.AfterNightLeft), Channel.Control,
                reliable: true);
            Plugin.Log.LogInfo(
                "[world] we walked out of our hideout: the morning is over, "
                    + "told the host");
        }

        /// <summary>
        /// Host: a joiner walked out of their hideout in the morning. There is
        /// nothing to roll back (its trader is gone in its game already) and
        /// nothing to gain by lying (only that the morning ends sooner), so the
        /// host only checks that it's a joiner in the world. It ends its own
        /// morning (its dialogue with the trader closed first), whose end sends
        /// AfterNightEnded to everyone; without a morning of its own, it sends
        /// that itself.
        /// </summary>
        private void OnLeft(ulong from, PacketReader r)
        {
            var c = Singleton<Controller>.Instance;
            var d = Singleton<Dreams>.Instance;
            var facts = new LeftFacts
            {
                IsHost = S.IsHost,
                FromPeer = S.Peers.Contains(from),
                InWorld = WorldShare.InWorld,
                HasController = c != null,
                InMorning = c != null && c.isAfterNight,
                Dreaming = d != null && d.dreaming,
                Talking = TalkingToMorningVisitor(),
            };

            LeftStep step = MorningRules.OnLeft(facts);
            if (step == LeftStep.Ignore)
            {
                return;
            }

            _late.Over(c.day);
            if (step == LeftStep.Relay)
            {
                Relay(from, c);
                return;
            }

            Plugin.Log.LogInfo(
                $"[world] {NameOf(S, from)} walked out of their hideout: "
                    + "our morning is over too");
            if (step == LeftStep.EndAfterTalk)
            {
                _endPending = true;
                _pendingSince = Now;
                Plugin.Log.LogInfo(
                    "[world] closing our dialogue with the trader first");
                return;
            }

            End(c);
        }

        private void Relay(ulong from, Controller c)
        {
            S.Broadcast(
                S.Begin(MsgType.AfterNightEnded), Channel.Control,
                reliable: true, except: from);
            string name = NameOf(S, from);
            string ours = "already was";
            if (c.isAfterNight)
            {
                ours = "waits for our dream";
            }

            Plugin.Log.LogInfo(
                $"[world] {name} walked out of their hideout: the morning is "
                    + $"over, sent to the others (ours {ours})");
        }

        /// <summary>Host, every frame: a joiner still in a morning after
        /// today's is over (it joined late, or its dawn came after the first
        /// player walked out) is told so, every few seconds until its
        /// PlayerState says it's out.</summary>
        private void TellLateMornings(Controller c)
        {
            var sync = Plugin.Instance.Host.Players;
            var facts = new LateFacts
            {
                HasSync = sync != null,
                InWorld = WorldShare.InWorld,
                Today = c.day,
            };

            if (!_late.Check(facts))
            {
                return;
            }

            Seconds now = Now;
            foreach (var kv in sync.Remotes)
            {
                var last = kv.Value.Last;
                var remote = new RemoteMorning
                {
                    HasState = last != null,
                    InMorning = last != null && last.AfterNight,
                    IsPeer = S.Peers.Contains(kv.Key),
                };

                if (!_late.Tell(kv.Key, remote, now))
                {
                    continue;
                }

                S.SendTo(
                    kv.Key, S.Begin(MsgType.AfterNightEnded), Channel.Control,
                    reliable: true);
                Plugin.Log.LogInfo(
                    $"[world] {NameOf(S, kv.Key)} is still in a morning that's "
                        + "over: told them");
            }
        }

        private void OnEnded(ulong from, PacketReader r)
        {
            var c = Singleton<Controller>.Instance;
            var d = Singleton<Dreams>.Instance;
            var facts = new EndedFacts
            {
                IsClient = S.IsClient,
                FromHost = from == S.HostId,
                InWorld = WorldShare.InWorld,
                Pending = _endPending,
                HasController = c != null,
                InMorning = c != null && c.isAfterNight,
                Dreaming = d != null && d.dreaming,
                Talking = TalkingToMorningVisitor(),
            };

            EndedStep step = MorningRules.OnEnded(facts);
            if (step == EndedStep.AlreadyOver)
            {
                Plugin.Log.LogInfo(
                    "[world] the host's after-night is over (ours already "
                        + "was)");
                return;
            }

            if (step == EndedStep.EndAfterTalk)
            {
                // endAfterNight destroys the trader, and a dialogue or trade
                // whose NPC is destroyed can't be closed (issue #15): close it
                // first, the game's way (what's on the table goes back), then
                // end the morning. The wolf stays, but the morning he came for
                // is over for everyone, so his dialogue closes too.
                _endPending = true;
                _pendingSince = Now;
                Plugin.Log.LogInfo(
                    "[world] the host's after-night is over: closing our "
                        + "dialogue with the trader first");
                return;
            }

            if (step == EndedStep.End)
            {
                End(c);
            }
        }

        /// <summary>End our morning because another game's ended. On the host
        /// this isn't a replay: its end (ControllerEndAfterNight) sends
        /// AfterNightEnded to everyone.</summary>
        private void End(Controller c)
        {
            _endPending = false;
            if (c == null || !c.isAfterNight)
            {
                return;
            }

            bool host = S.IsHost;
            bool replay = MorningRules.EndsAsReplay(host);
            if (replay)
            {
                _coop.Guards.Replaying = true;
            }

            try
            {
                c.endAfterNight();
            }
            finally
            {
                if (replay)
                {
                    _coop.Guards.Replaying = false;
                }
            }

            string data =
                "[world] the host's after-night is over: ours ended too (the "
                    + "trader left)";
            if (host)
            {
                data =
                    "[world] a joiner's morning is over: ours ended too (the "
                        + "trader left)";
            }

            Plugin.Log.LogInfo(data);
        }

        /// <summary>Our dialogue is with a hideout's morning visitor: its
        /// trader (which endAfterNight destroys) or the wolf (Location.wolf,
        /// which stays; the map test's save has him).</summary>
        private static bool TalkingToMorningVisitor()
        {
            DialogueWindow w = null;
            if (Singleton<UI>.Instance != null)
            {
                w = Singleton<UI>.Instance.dialogueWindow;
            }

            return MorningRules.Talking(VisitorOf(Player.Instance, w));
        }

        private static VisitorFacts VisitorOf(Player p, DialogueWindow w)
        {
            var facts = new VisitorFacts
            {
                HasPlayer = p != null,
                HasWindow = w != null,
                HasNpc = w != null && w.npc != null,
                InDialogue = p != null && p.inDialogue,
            };

            if (!facts.HasPlayer || !facts.HasWindow || !facts.HasNpc
                || !facts.InDialogue)
            {
                return facts;
            }

            var wg = Singleton<WorldGenerator>.Instance;
            if (wg == null || wg.locations == null)
            {
                return facts;
            }

            return facts with
            {
                HasLocations = true,
                AtBase = AtBase(wg, w.npc.gameObject),
            };
        }

        /// Whether an NPC is a hideout's trader or wolf, or part of one.
        private static bool AtBase(WorldGenerator wg, GameObject go)
        {
            foreach (var l in wg.locations)
            {
                if (l == null || !l.playerBase)
                {
                    continue;
                }

                if ((l.trader != null
                        && (l.trader == go || go.transform.IsChildOf(
                            l.trader.transform)))
                    || (l.wolf != null
                        && (l.wolf == go || go.transform.IsChildOf(
                            l.wolf.transform))))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Every frame: close the dialogue with the leaving trader, then end
        /// our morning.
        /// </summary>
        private void PendingUpdate()
        {
            if (!_endPending)
            {
                return;
            }

            var c = Singleton<Controller>.Instance;
            DialogueWindow w = null;
            if (Singleton<UI>.Instance != null)
            {
                w = Singleton<UI>.Instance.dialogueWindow;
            }

            var facts = new PendingFacts
            {
                Pending = _endPending,
                HasController = c != null,
                HasWindow = w != null,
                HasPlayer = Player.Instance != null,
                InWorld = WorldShare.InWorld,
                Talking = TalkingToMorningVisitor(),
            };

            PendingStep step = MorningRules.Pending(facts);
            if (step == PendingStep.Forget)
            {
                _endPending = false;
                return;
            }

            if (step == PendingStep.End)
            {
                End(c);
                return;
            }

            // close() lets go of the NPC at once (the fade and the controls
            // follow without him), so the morning can end. It does nothing
            // while the window is still opening: tried again next frame.
            var guard = Plugin.Instance.Host.Dialogues;
            guard.Close(w);
            CloseStep after =
                MorningRules.AfterClose(w.npc == null, Now, _pendingSince);
            if (after == CloseStep.End)
            {
                End(c);
                return;
            }

            if (after == CloseStep.ForceEnd)
            {
                Plugin.Log.LogWarning(
                    "[world] our dialogue with the trader didn't close: "
                        + "closing it without him and ending the morning");
                guard.ForceClose(w, "not closing");

                End(c);
            }
        }
    }
}
