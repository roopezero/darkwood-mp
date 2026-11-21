using System.Collections.Generic;
using DarkwoodMP.Net;
using HarmonyLib;

namespace DarkwoodMP.World
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
    /// </summary>
    public static class AfterNight
    {
        public static void Register(Session s)
        {
            s.Register(MsgType.AfterNightEnded, OnEnded);
            s.Register(MsgType.AfterNightLeft, OnLeft);
            s.OnSessionEnded += () =>
            {
                _clockFreed = false;
                _since = -1f;
                _endPending = false;
                _overDay = -1;
                _told.Clear();
            };
        }

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
        /// Host: we turned the clock back on during our own after-night.
        /// </summary>
        private static bool _clockFreed;
        /// <summary>
        /// Host: when our after-night began (unscaled time), -1 when not in
        /// one.
        /// </summary>
        private static float _since = -1f;
        /// <summary>
        /// Joiners' after-nights start when their clock reaches dawn, a moment
        /// after the host's: wait this long first.
        /// </summary>
        private const float Grace = 5f;

        /// <summary>
        /// Host: the day whose morning is over (someone walked out of their
        /// hideout), -1 when none is.
        /// </summary>
        private static int _overDay = -1;
        /// <summary>
        /// Host: when we last told each joiner still in a morning that it is
        /// over (unscaled time).
        /// </summary>
        private static readonly Dictionary<ulong, float> _told =
            new Dictionary<ulong, float>();

        /// <summary>
        /// Host: a new dawn in our game (startAfterNight runs at each dawn):
        /// today's morning isn't over yet.
        /// </summary>
        internal static void HostDawn()
        {
            var s = Session.Instance;
            if (s == null || !s.IsHost)
            {
                return;
            }
            _overDay = -1;
            _told.Clear();
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
        /// Host, every frame: run the clock during our after-night while a
        /// joiner is out of their hideout.
        /// </summary>
        public static void Update()
        {
            var s = Session.Instance;
            var c = Singleton<Controller>.Instance;
            var p = Player.Instance;
            if (s != null)
            {
                PendingUpdate();
            }
            if (c == null || p == null || s == null || !s.IsHost)
            {
                return;
            }
            TellLateMornings(s, c);
            bool frozen = c.isAfterNight && p.effects != null
                && p.effects.hasEffectType(CharacterEffectType.timeFreeze);
            if (!frozen)
            {
                _clockFreed = false;
                _since = -1f;
                return;
            }
            if (_since < 0f)
            {
                _since = UnityEngine.Time.unscaledTime;
            }
            bool someoneOut = false;
            var sync = Sync.PlayerSync.Instance;
            if (CoopPause.InCoop && sync != null
                && UnityEngine.Time.unscaledTime - _since >= Grace)
            {
                foreach (var kv in sync.Remotes)
                {
                    var last = kv.Value.Last;
                    if (kv.Key != Sync.PlayerSync.DummyId && last != null
                        && !last.AfterNight)
                    {
                        someoneOut = true;
                        break;
                    }
                }
            }
            if (someoneOut && !_clockFreed)
            {
                _clockFreed = true;
                c.DoUpdateTime = true;
                Plugin.Log.LogInfo(
                    "[world] a joiner is out of their hideout: the clock runs "
                        + "on during our after-night");
            }
            else if (!someoneOut && _clockFreed)
            {
                _clockFreed = false;
                c.DoUpdateTime = false;
                Plugin.Log.LogInfo(
                    "[world] every joiner is in an after-night: the clock "
                        + "stops again until we walk out");
            }
        }

        internal static void HostEnded()
        {
            var s = Session.Instance;
            if (s == null || !s.IsHost || !WorldShare.InWorld
                || WorldActions.Replaying)
            {
                return;
            }
            var c = Singleton<Controller>.Instance;
            if (c != null)
            {
                _overDay = c.day;
            }
            if (s.Peers.Count == 0)
            {
                return;
            }
            s.Broadcast(
                s.Begin(MsgType.AfterNightEnded), Channel.Control,
                reliable: true);
            Plugin.Log.LogInfo(
                "[world] the host's after-night is over: sent to the others");
        }

        /// <summary>Client: our player walked out of the hideout and ended our
        /// morning (Location.OnTriggerExit). The morning is everyone's, so the
        /// host ends it in every game.</summary>
        internal static void LocalWalkedOut()
        {
            var s = Session.Instance;
            if (s == null || !s.IsClient || !WorldShare.InWorld
                || WorldActions.Replaying)
            {
                return;
            }
            s.SendToHost(
                s.Begin(MsgType.AfterNightLeft), Channel.Control,
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
        private static void OnLeft(ulong from, PacketReader r)
        {
            var s = Session.Instance;
            var c = Singleton<Controller>.Instance;
            if (s == null || !s.IsHost || !s.Peers.Contains(from)
                || !WorldShare.InWorld || c == null)
            {
                return;
            }
            _overDay = c.day;
            var d = Singleton<Dreams>.Instance;
            if (c.isAfterNight && !(d != null && d.dreaming))
            {
                Plugin.Log.LogInfo(
                    $"[world] {NameOf(s, from)} walked out of their hideout: "
                        + "our morning is over too");
                if (TalkingToMorningVisitor())
                {
                    _endPending = true;
                    _pendingSince = UnityEngine.Time.unscaledTime;
                    Plugin.Log.LogInfo(
                        "[world] closing our dialogue with the trader first");
                    return;
                }
                End(c);
                return;
            }
            s.Broadcast(
                s.Begin(MsgType.AfterNightEnded), Channel.Control,
                reliable: true, except: from);
            string name = NameOf(s, from);
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
        private static void TellLateMornings(Session s, Controller c)
        {
            var sync = Sync.PlayerSync.Instance;
            if (_overDay < 0 || sync == null || !WorldShare.InWorld)
            {
                return;
            }
            if (_overDay != c.day)
            {
                _overDay = -1;
                _told.Clear();
                return;
            }
            float now = UnityEngine.Time.unscaledTime;
            foreach (var kv in sync.Remotes)
            {
                var last = kv.Value.Last;
                if (last == null || !last.AfterNight
                    || !s.Peers.Contains(kv.Key))
                {
                    continue;
                }
                if (_told.TryGetValue(kv.Key, out float at) && now - at < 3f)
                {
                    continue;
                }
                _told[kv.Key] = now;
                s.SendTo(
                    kv.Key, s.Begin(MsgType.AfterNightEnded), Channel.Control,
                    reliable: true);
                Plugin.Log.LogInfo(
                    $"[world] {NameOf(s, kv.Key)} is still in a morning that's "
                        + "over: told them");
            }
        }

        private static void OnEnded(ulong from, PacketReader r)
        {
            var s = Session.Instance;
            if (s == null || !s.IsClient || from != s.HostId
                || !WorldShare.InWorld || _endPending)
            {
                return;
            }
            var c = Singleton<Controller>.Instance;
            var d = Singleton<Dreams>.Instance;
            if (c == null || !c.isAfterNight || (d != null && d.dreaming))
            {
                Plugin.Log.LogInfo(
                    "[world] the host's after-night is over (ours already "
                        + "was)");
                return;
            }
            if (TalkingToMorningVisitor())
            {
                // endAfterNight destroys the trader, and a dialogue or trade
                // whose NPC is destroyed can't be closed (issue #15): close it
                // first, the game's way (what's on the table goes back), then
                // end the morning. The wolf stays, but the morning he came for
                // is over for everyone, so his dialogue closes too.
                _endPending = true;
                _pendingSince = UnityEngine.Time.unscaledTime;
                Plugin.Log.LogInfo(
                    "[world] the host's after-night is over: closing our "
                        + "dialogue with the trader first");
                return;
            }
            End(c);
        }

        /// <summary>End our morning because another game's ended. On the host
        /// this isn't a replay: its end (AfterNightEndPatch) sends
        /// AfterNightEnded to everyone.</summary>
        private static void End(Controller c)
        {
            _endPending = false;
            if (c == null || !c.isAfterNight)
            {
                return;
            }
            bool host = Session.Instance != null && Session.Instance.IsHost;
            if (!host)
            {
                WorldActions.Replaying = true;
            }
            try
            {
                c.endAfterNight();
            }
            finally
            {
                if (!host)
                {
                    WorldActions.Replaying = false;
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

        /// <summary>
        /// The morning ended in another game while we talked to the trader;
        /// ours ends once that dialogue closes.
        /// </summary>
        private static bool _endPending;
        private static float _pendingSince;

        /// <summary>Our dialogue is with a hideout's morning visitor: its
        /// trader (which endAfterNight destroys) or the wolf (Location.wolf,
        /// which stays; the map test's save has him).</summary>
        private static bool TalkingToMorningVisitor()
        {
            var p = Player.Instance;
            DialogueWindow w = null;
            if (Singleton<UI>.Instance != null)
            {
                w = Singleton<UI>.Instance.dialogueWindow;
            }
            if (p == null || w == null || w.npc == null || !p.inDialogue)
            {
                return false;
            }
            var wg = Singleton<WorldGenerator>.Instance;
            if (wg == null || wg.locations == null)
            {
                return false;
            }
            foreach (var l in wg.locations)
            {
                if (l == null || !l.playerBase)
                {
                    continue;
                }
                var go = w.npc.gameObject;
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
        private static void PendingUpdate()
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
            if (c == null || w == null || Player.Instance == null
                || !WorldShare.InWorld)
            {
                _endPending = false;
                return;
            }
            // close() lets go of the NPC at once (the fade and the controls
            // follow without him), so the morning can end.
            if (!TalkingToMorningVisitor())
            {
                End(c);
                return;
            }
            // does nothing while the window is still opening: tried again next
            // frame
            DialogueGuard.Close(w);
            if (w.npc == null)
            {
                End(c);
                return;
            }
            if (UnityEngine.Time.unscaledTime - _pendingSince > 10f)
            {
                Plugin.Log.LogWarning(
                    "[world] our dialogue with the trader didn't close: "
                        + "closing it without him and ending the morning");
                DialogueGuard.ForceClose(w, "not closing");
                End(c);
            }
        }
    }

    /// <summary>
    /// The after-night reward (startAfterNight: the trader's reputation += 100
    /// to 250) is given in each game whose player is in a hideout at dawn. It
    /// goes to the others as the reward for that day, which counts once.
    /// </summary>
    [HarmonyPatch(typeof(Controller), nameof(Controller.startAfterNight))]
    static class AfterNightRewardPatch
    {
        static void Prefix(out Dictionary<string, int> __state)
        {
            AfterNight.HostDawn(); // a new morning: not over yet
            __state = null;
            var f = Singleton<Flags>.Instance;
            if (f == null || FlagsSync.Instance == null)
            {
                return;
            }
            __state = new Dictionary<string, int>();
            foreach (var n in f.npcStates)
            {
                if (n != null && n.name != null)
                {
                    __state[n.name] = n.reputation;
                }
            }
        }

        static void Postfix(
            Controller __instance, Dictionary<string, int> __state)
        {
            var f = Singleton<Flags>.Instance;
            if (__state == null || f == null)
            {
                return;
            }
            foreach (var n in f.npcStates)
            {
                if (n == null || n.name == null
                    || !__state.TryGetValue(n.name, out int was)
                    || n.reputation == was)
                {
                    continue;
                }
                FlagsSync.Instance.Absorb(n.name, n.reputation - was);
                FlagsSync.Instance.LocalReward(
                    n.name, __instance.day, n.reputation - was);
            }
        }
    }

    /// <summary>A joiner walking out of its hideout in the morning
    /// (Location.OnTriggerExit -> endAfterNight) tells the host, which ends the
    /// morning everywhere. A morning ended any other way (a death's reset, the
    /// host's end) isn't sent.</summary>
    [HarmonyPatch(
        typeof(Location), "OnTriggerExit",
        new[] { typeof(UnityEngine.Collider) })]
    static class AfterNightWalkOutPatch
    {
        static void Prefix(out bool __state)
        {
            var c = Singleton<Controller>.Instance;
            __state = c != null && c.isAfterNight;
        }
        static void Postfix(bool __state)
        {
            var c = Singleton<Controller>.Instance;
            if (__state && c != null && !c.isAfterNight)
            {
                AfterNight.LocalWalkedOut();
            }
        }
    }

    [HarmonyPatch(typeof(Controller), nameof(Controller.endAfterNight))]
    static class AfterNightEndPatch
    {
        static void Prefix(Controller __instance, out bool __state) =>
            __state = __instance.isAfterNight;
        // Killed traders end it through NpcDied, which ends it in each game
        // (and leaves the body there).
        static void Postfix(
            Controller __instance, bool __state, bool byKillingTrader)
        {
            if (__state && !__instance.isAfterNight && !byKillingTrader)
            {
                AfterNight.HostEnded();
            }
        }
    }
}
