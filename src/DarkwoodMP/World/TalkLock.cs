using System.Collections.Generic;
using DarkwoodMP.Net;
using DarkwoodMP.World.Talk;
using UnityEngine.SceneManagement;

namespace DarkwoodMP.World
{
    /// <summary>
    /// One player at a time talks to an NPC, as with containers: a dialogue can
    /// give a one-time reward (giveItem), and two players in the same dialogue
    /// would both get it. The host keeps who talks to whom. A joiner's dialogue
    /// opens at once and asks the host (TalkLock as a Request); if someone else
    /// is talking to that NPC, the host refuses with who it is and the joiner's
    /// dialogue closes again. The host's own player simply isn't let in while a
    /// joiner talks to them. Closing the dialogue lets go (TalkLock with
    /// talking 0).
    ///
    /// What the dialogue did (lines said, which a one-time reward checks,
    /// reputation, flags) goes out at once when it closes (FlagsSync.FlushNow),
    /// before the lock is let go on the same reliable channel, so the host has
    /// it before the NPC is free. The others get it a moment later, so for
    /// Grace seconds after someone lets go only the host's own player (who has
    /// it already) may talk to that NPC; a joiner is told to wait.
    ///
    /// A trader or the wolf is the same NPC in every game, though each game
    /// puts its own copy in its player's hideout in the morning: they are
    /// locked by name, so two players can't talk to "him" in two hideouts at
    /// once.
    /// </summary>
    internal sealed class TalkLock
    {
        private readonly CoopSession _coop;
        private readonly Session _session;
        /// <summary>Host: who talks to which NPC (Key).</summary>
        private readonly Dictionary<string, ulong> _talkers =
            new Dictionary<string, ulong>();
        /// <summary>
        /// Host: who last let go of each NPC, and when (unscaled time).
        /// </summary>
        private readonly Dictionary<string, KeyValuePair<ulong, float>>
            _released = new Dictionary<string, KeyValuePair<ulong, float>>();
        /// <summary>
        /// The NPC our own player talks to now, as shared (null: none).
        /// </summary>
        private NPC _ours;
        private ObjRef _oursRef;
        private string _oursName = "";

        /// <summary>For one session (CoopSession.Talk).</summary>
        public TalkLock(CoopSession coop)
        {
            _coop = coop;
            _session = coop.Net;
            coop.Handle(MsgType.TalkLock, OnLock);
            _session.OnPeerDisconnected += OnPeerLeft;
            coop.Hook(() => _session.OnPeerDisconnected -= OnPeerLeft);
            SceneManager.sceneUnloaded += OnUnloaded;
            coop.Hook(() => SceneManager.sceneUnloaded -= OnUnloaded);
        }

        /// <summary>Host: a player who left talks to nobody.</summary>
        private void OnPeerLeft(ulong peer)
        {
            var gone = new List<string>();
            foreach (var kv in _talkers)
            {
                if (kv.Value == peer)
                {
                    gone.Add(kv.Key);
                }
            }

            foreach (var k in gone)
            {
                _talkers.Remove(k);
            }
        }

        /// <summary>
        /// The world unloaded during our dialogue (a death, a reload, a
        /// dialogue that ends the chapter): the window's close never ran, so
        /// let the NPC go, or every other player would be told we're still
        /// talking to them.
        /// </summary>
        private void OnUnloaded(Scene scene)
        {
            if (TalkRules.UnloadEnds(_oursRef.Valid, _ours != null))
            {
                Closed();
            }
        }

        /// <summary>
        /// NPC.talkTo's prefix: whether our player's dialogue may open (only
        /// the host refuses it, while a joiner talks to them).
        /// </summary>
        internal static bool BeforeTalkTo(NPC npc)
        {
            CoopSession coop = Coop.Current;
            if (coop == null)
            {
                return true;
            }

            return coop.Talk.HostMayTalk(npc);
        }

        /// <summary>
        /// NPC.talkTo's postfix: our player's dialogue with npc opened, if
        /// the window has them.
        /// </summary>
        internal static void AfterTalkTo(NPC npc)
        {
            CoopSession coop = Coop.Current;
            if (coop == null)
            {
                return;
            }

            UI instance = Singleton<UI>.Instance;
            DialogueWindow win = null;
            if (instance != null)
            {
                win = instance.dialogueWindow;
            }

            if (win != null && win.npc == npc)
            {
                coop.Talk.Opened(npc);
            }
        }

        /// <summary>
        /// DialogueWindow.close's postfix: our dialogue closed once the
        /// window let go of its NPC.
        /// </summary>
        internal static void AfterClose(DialogueWindow w)
        {
            CoopSession coop = Coop.Current;
            if (coop != null && w.npc == null)
            {
                coop.Talk.Closed();
            }
        }

        /// <summary>
        /// The name an NPC is locked by (TalkRules.LockName), or "".
        /// </summary>
        private static string UniqueName(NPC npc)
        {
            return TalkRules.LockName(NameFactsOf(npc));
        }

        /// <summary>What decides the name an NPC is locked by.</summary>
        private static TalkNameFacts NameFactsOf(NPC npc)
        {
            if (npc == null)
            {
                return default;
            }

            var saved = ObjRef.Of(npc.gameObject).K == ObjRef.Kind.Saved;
            return new TalkNameFacts
            {
                Name = npc.name,
                Trader = npc.trader,
                Saved = saved,
            };
        }

        private static string Key(ObjRef r, string name)
        {
            return TalkRules.Key(name, r.ToString());
        }

        /// <summary>
        /// Host: we let go of the NPC (ours or a joiner's dialogue closed).
        /// </summary>
        private void Release(string key, ulong who)
        {
            bool held = _talkers.TryGetValue(key, out var holder);
            if (!TalkRules.Releases(held, holder, who))
            {
                return;
            }

            _talkers.Remove(key);
            _released[key] = new KeyValuePair<ulong, float>(
                who, UnityEngine.Time.unscaledTime);
        }

        /// <summary>
        /// Host, before our own player's dialogue opens: whether someone else
        /// has the NPC.
        /// </summary>
        private bool HostMayTalk(NPC npc)
        {
            Verdict verdict = TalkRules.HostMayTalk(FactsOf(npc));
            if (!verdict.Refused)
            {
                return true;
            }

            string text = Refusals.Text(verdict);
            Player instance = Player.Instance;
            if (instance != null)
            {
                instance.displayMessage(text);
            }

            Plugin.Log.LogInfo($"{npc.name}: {text}");
            return false;
        }

        /// <summary>
        /// What the host looks at before its own player talks to npc. The ref
        /// and its holder are only read while sharing as the host.
        /// </summary>
        private HostTalkFacts FactsOf(NPC npc)
        {
            bool sharing = _coop.Guards.Sharing;
            var facts = new HostTalkFacts
            {
                HasDialogue = npc.characterDialogue != null,
                WantsToTalk = npc.wantsToTalk,
                Sharing = sharing,
            };

            if (!sharing || !_session.IsHost)
            {
                return facts;
            }

            var r = ObjRef.Of(npc.gameObject);
            string key = Key(r, UniqueName(npc));
            bool held = _talkers.TryGetValue(key, out var holder);
            string name = null;
            if (held)
            {
                name = _session.NameOf(holder);
            }

            return facts with
            {
                IsHost = true,
                RefValid = r.Valid,
                Held = held,
                Holder = holder,
                Local = _session.LocalId,
                HolderName = name,
            };
        }

        /// <summary>Our player's dialogue with this NPC opened.</summary>
        private void Opened(NPC npc)
        {
            var r = ObjRef.Of(npc.gameObject);
            TalkOpen open = TalkRules.Opens(OpenFactsOf(npc, r));
            if (open == TalkOpen.NotShared)
            {
                return; // a dream's NPC of our own: nobody else can talk to it
            }

            _ours = npc;
            _oursRef = r;
            _oursName = UniqueName(npc);
            if (open == TalkOpen.Take)
            {
                _talkers[Key(r, _oursName)] = _session.LocalId;
                return;
            }

            var w = _session.Begin(MsgType.TalkLock);
            TalkLockMessage.Write(w, new TalkHold
            {
                Npc = ObjRef.NameOf(r),
                Talking = true,
                Name = _oursName,
            });

            _coop.Authority.Request(
                w, $"talk to {npc.name} {r}", (ok, reason, _) =>
            {
                if (!ok)
                {
                    Refused(npc, reason);
                }
            }, inventory: false);
        }

        /// <summary>
        /// What our player's opened dialogue looks at. Only read while
        /// sharing, as before: the ref and the host's role after it.
        /// </summary>
        private TalkOpenFacts OpenFactsOf(NPC npc, ObjRef r)
        {
            bool sharing = _coop.Guards.Sharing;
            if (!sharing)
            {
                return default;
            }

            return new TalkOpenFacts
            {
                Sharing = true,
                OursAlone = WorldActions.OursAlone(npc.gameObject),
                RefValid = r.Valid,
                IsHost = _session.IsHost,
            };
        }

        /// <summary>Our player's dialogue closed.</summary>
        internal void Closed()
        {
            TalkEnd end = TalkRules.Ends(EndFactsOf());
            if (end == TalkEnd.None)
            {
                return;
            }

            var r = _oursRef;
            string name = _oursName;
            _ours = null;
            _oursRef = default;
            _oursName = "";
            if (end == TalkEnd.Forget)
            {
                return;
            }

            // What the dialogue did goes first, so it's there before the NPC is
            // free for the next player.
            FlagsSync instance = FlagsSync.Instance;
            if (instance != null)
            {
                instance.FlushNow();
            }

            var s = _session;
            if (end == TalkEnd.Release)
            {
                Release(Key(r, name), s.LocalId);
                return;
            }

            if (end == TalkEnd.Flush)
            {
                return;
            }

            var w = s.Begin(MsgType.TalkLock);
            TalkLockMessage.Write(w, new TalkHold
            {
                Npc = ObjRef.NameOf(r),
                Talking = false,
                Name = name,
            });

            s.SendToHost(w, Channel.Control, reliable: true);
        }

        /// <summary>What our player's closed dialogue looks at.</summary>
        private TalkEndFacts EndFactsOf()
        {
            var s = _session;
            var facts = new TalkEndFacts
            {
                Ours = _ours != null,
                OursValid = _oursRef.Valid,
                HasSession = s != null,
                ClientMode = Combat.NetEnemies.ClientMode,
            };

            if (s == null)
            {
                return facts;
            }

            return facts with { IsHost = s.IsHost };
        }

        /// <summary>
        /// Joiner: the host said someone else talks to them: close our dialogue
        /// (once it has finished opening).
        /// </summary>
        private void Refused(NPC npc, string reason)
        {
            if (_ours == npc) // nothing of ours to let go
            {
                _ours = null;
                _oursRef = default;
                _oursName = "";
            }

            Player instance = Player.Instance;
            if (instance != null)
            {
                instance.displayMessage(reason);
            }

            CloseWhenReady(npc, TalkRules.Tries);
        }

        private void CloseWhenReady(NPC npc, int tries)
        {
            UI instance = Singleton<UI>.Instance;
            DialogueWindow win = null;
            if (instance != null)
            {
                win = instance.dialogueWindow;
            }

            CloseTry next = TalkRules.CloseWhenReady(
                FactsOf(win, npc, tries));
            if (next == CloseTry.Stop)
            {
                return;
            }

            // The window opens over a second or so (a portrait, then a fade:
            // inDialogue, then tweening until it's in); closing it before that
            // leaves the game's own opening steps without an NPC.
            if (next == CloseTry.Wait)
            {
                TryAgain(npc, tries - 1);
                return;
            }

            // From the main menu, close() shows the NPC's goodbye line instead
            // of closing. Setting the menu back to main before each try showed
            // that line again every time, so a refused dialogue never closed
            // (the map test's wolf). Any other menu closes at once, without a
            // goodbye from someone we never talked to.
            _coop.Dialogues.Close(win);
            if (win.npc == npc)
            {
                TryAgain(npc, tries - 1);
            }
        }

        /// <summary>CloseWhenReady again in TalkRules.RetryEvery.</summary>
        private void TryAgain(NPC npc, int tries)
        {
            Singleton<Controller>.Instance.Invoke(
                () => CloseWhenReady(npc, tries), TalkRules.RetryEvery,
                timeScaleDependent: false);
        }

        /// <summary>
        /// One try at closing a refused dialogue: the window, its NPC and
        /// whether it is still opening. win may be null.
        /// </summary>
        private TalkCloseFacts FactsOf(
            DialogueWindow win, NPC npc, int tries)
        {
            bool same = win != null && win.npc == npc && npc != null;
            if (!same)
            {
                return new TalkCloseFacts { Tries = tries };
            }

            bool opening = win.tweening || Player.Instance == null
                || !Player.Instance.inDialogue;
            return new TalkCloseFacts
            {
                SameNpc = true,
                Opening = opening,
                Tries = tries,
            };
        }

        private void OnLock(ulong from, PacketReader r)
        {
            var s = _session;
            if (s == null || !s.IsHost)
            {
                return;
            }

            TalkHold hold = TalkLockMessage.Read(r);
            var obj = ObjRef.RefOf(hold.Npc);
            string key = Key(obj, hold.Name);
            if (!hold.Talking)
            {
                Release(key, from);
                return;
            }

            Verdict verdict = TalkRules.MayClaim(FactsOf(from, key));
            Authority authority = _coop.Authority;
            if (verdict.Refused)
            {
                authority.Refuse(verdict);
                return;
            }

            var npc = ObjRef.Resolve<NPC>(obj);

            // The rule's last check, the reach, is Authority's: it knows
            // where the claimer stands.
            if (npc != null
                && !authority.InReach(
                    npc.transform.position, TalkRules.Reach, npc.name))
            {
                return;
            }

            _talkers[key] = from;
            if (WorldActions.Debug)
            {
                string who = s.NameOf(from);
                string name;
                if (npc != null)
                {
                    name = npc.name;
                }
                else
                {
                    name = obj.ToString();
                }

                Plugin.Log.LogInfo($"[world] {who} talks to {name}");
            }
        }

        /// <summary>
        /// Host: a joiner's claim of the NPC by key, up to its reach, which
        /// Authority.InReach checks after. The holder's and the releaser's
        /// names are only read when there is one.
        /// </summary>
        private TalkFacts FactsOf(ulong from, string key)
        {
            var s = _session;
            bool held = _talkers.TryGetValue(key, out var holder);
            bool released = _released.TryGetValue(key, out var rel);
            string holderName = null;
            if (held)
            {
                holderName = s.NameOf(holder);
            }

            string releaserName = null;
            if (released)
            {
                releaserName = s.NameOf(rel.Key);
            }

            return new TalkFacts
            {
                From = from,
                Held = held,
                Holder = holder,
                HolderName = holderName,
                Released = released,
                Releaser = rel.Key,
                ReleaserName = releaserName,
                ReleasedAt = new Seconds(rel.Value),
                Now = new Seconds(UnityEngine.Time.unscaledTime),
            };
        }
    }
}
