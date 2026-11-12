using System;
using System.Collections.Generic;
using DarkwoodMP.Net;
using HarmonyLib;

namespace DarkwoodMP.World
{
    /// <summary>
    /// One player at a time talks to an NPC, as with containers: a dialogue can give a one-time reward
    /// (giveItem), and two players in the same dialogue would both get it. The host keeps who talks to whom. A
    /// joiner's dialogue opens at once and asks the host (TalkLock as a Request); if someone else is talking to
    /// that NPC, the host refuses with who it is and the joiner's dialogue closes again. The host's own player
    /// simply isn't let in while a joiner talks to them. Closing the dialogue lets go (TalkLock with talking 0).
    ///
    /// What the dialogue did (lines said, which a one-time reward checks, reputation, flags) goes out at once when
    /// it closes (FlagsSync.FlushNow), before the lock is let go on the same reliable channel, so the host has it
    /// before the NPC is free. The others get it a moment later, so for Grace seconds after someone lets go only
    /// the host's own player (who has it already) may talk to that NPC; a joiner is told to wait.
    ///
    /// A trader or the wolf is the same NPC in every game, though each game puts its own copy in its player's
    /// hideout in the morning: they are locked by name, so two players can't talk to "him" in two hideouts at once.
    /// </summary>
    public static class TalkLock
    {
        private static Session S => Session.Instance;
        /// <summary>Host: who talks to which NPC (Key).</summary>
        private static readonly Dictionary<string, ulong> _talkers = new Dictionary<string, ulong>();
        /// <summary>Host: who last let go of each NPC, and when (unscaled time).</summary>
        private static readonly Dictionary<string, KeyValuePair<ulong, float>> _released = new Dictionary<string, KeyValuePair<ulong, float>>();
        /// <summary>Seconds after someone lets go of an NPC before a joiner may talk to them (their dialogue's changes on the way).</summary>
        private const float Grace = 1.5f;
        /// <summary>The NPC our own player talks to now, as shared (null: none).</summary>
        private static NPC _ours;
        private static ObjRef _oursRef;
        private static string _oursName = "";

        public static void Register(Session s)
        {
            s.Register(MsgType.TalkLock, OnLock);
            s.OnPeerDisconnected += peer => { var gone = new List<string>(); foreach (var kv in _talkers) if (kv.Value == peer) gone.Add(kv.Key); foreach (var k in gone) _talkers.Remove(k); };
            s.OnSessionEnded += () => { _talkers.Clear(); _released.Clear(); _ours = null; };
            // The world unloaded during our dialogue (a death, a reload, a dialogue that ends the chapter): the window's
            // close never ran, so let the NPC go, or every other player would be told we're still talking to them.
            UnityEngine.SceneManagement.SceneManager.sceneUnloaded += _ => { if (_oursRef.Valid && _ours == null) Closed(); };
        }

        /// <summary>The name an NPC is locked by if they're the same NPC in every game wherever they stand, else "".</summary>
        private static string UniqueName(NPC npc) =>
            npc != null && npc.name != null && (npc.trader || npc.name.IndexOf("wolf", StringComparison.OrdinalIgnoreCase) >= 0) ? npc.name : "";

        private static string Key(ObjRef r, string name) => name.Length > 0 ? "npc:" + name : r.ToString();

        /// <summary>Host: who other than this player talks to the NPC, or null.</summary>
        private static string TalkingOther(string key, ulong who) =>
            _talkers.TryGetValue(key, out var holder) && holder != who ? S.NameOf(holder) : null;

        /// <summary>Host: we let go of the NPC (ours or a joiner's dialogue closed).</summary>
        private static void Release(string key, ulong who)
        {
            if (!_talkers.TryGetValue(key, out var h) || h != who) return;
            _talkers.Remove(key);
            _released[key] = new KeyValuePair<ulong, float>(who, UnityEngine.Time.unscaledTime);
        }

        /// <summary>Host, before our own player's dialogue opens: whether someone else has the NPC.</summary>
        internal static bool HostMayTalk(NPC npc)
        {
            if (!WorldActions.Sharing || !S.IsHost) return true;
            var r = ObjRef.Of(npc.gameObject);
            if (!r.Valid) return true;
            string other = TalkingOther(Key(r, UniqueName(npc)), S.LocalId);
            if (other == null) return true;
            Player.Instance?.displayMessage($"{other} is talking to them");
            Plugin.Log.LogInfo($"{npc.name}: {other} is talking to them");
            return false;
        }

        /// <summary>Our player's dialogue with this NPC opened.</summary>
        internal static void Opened(NPC npc)
        {
            if (!WorldActions.Sharing) return;
            var r = ObjRef.Of(npc.gameObject);
            if (!r.Valid) return;
            _ours = npc; _oursRef = r; _oursName = UniqueName(npc);
            if (S.IsHost) { _talkers[Key(r, _oursName)] = S.LocalId; return; }
            var w = S.Begin(MsgType.TalkLock);
            r.Write(w);
            w.W.Write((byte)1);
            w.WriteStr(_oursName);
            Authority.Request(w, $"talk to {npc.name} {r}", (ok, reason, _) => { if (!ok) Refused(npc, reason); }, inventory: false);
        }

        /// <summary>Our player's dialogue closed.</summary>
        internal static void Closed()
        {
            if (_ours == null && !_oursRef.Valid) return;
            var r = _oursRef;
            string name = _oursName;
            _ours = null; _oursRef = default; _oursName = "";
            var s = S;
            if (s == null || !r.Valid) return;
            // What the dialogue did goes first, so it's there before the NPC is free for the next player.
            FlagsSync.Instance?.FlushNow();
            if (s.IsHost) { Release(Key(r, name), s.LocalId); return; }
            if (!Combat.NetEnemies.ClientMode) return;
            var w = s.Begin(MsgType.TalkLock);
            r.Write(w);
            w.W.Write((byte)0);
            w.WriteStr(name);
            s.SendToHost(w, Channel.Control, reliable: true);
        }

        /// <summary>Joiner: the host said someone else talks to them: close our dialogue (once it has finished opening).</summary>
        private static void Refused(NPC npc, string reason)
        {
            if (_ours == npc) { _ours = null; _oursRef = default; } // nothing of ours to let go
            Player.Instance?.displayMessage(reason);
            CloseWhenReady(npc, 40);
        }

        private static void CloseWhenReady(NPC npc, int tries)
        {
            var win = Singleton<UI>.Instance?.dialogueWindow;
            if (win == null || win.npc != npc || npc == null) return;
            // The window opens over a second or so (a portrait, then a fade: inDialogue, then tweening until it's in);
            // closing it before that leaves the game's own opening steps without an NPC.
            bool opening = win.tweening || Player.Instance == null || !Player.Instance.inDialogue;
            if (opening) { if (tries > 0) Singleton<Controller>.Instance.Invoke(() => CloseWhenReady(npc, tries - 1), 0.25f, timeScaleDependent: false); return; }
            // From the main menu, close() shows the NPC's goodbye line instead of closing. Setting the menu back to
            // main before each try showed that line again every time, so a refused dialogue never closed (the map
            // test's wolf). Any other menu closes at once, without a goodbye from someone we never talked to.
            DialogueGuard.Close(win);
            if (win.npc == npc) Singleton<Controller>.Instance.Invoke(() => CloseWhenReady(npc, tries - 1), 0.25f, timeScaleDependent: false);
        }

        private static void OnLock(ulong from, PacketReader r)
        {
            var s = S;
            if (s == null || !s.IsHost) return;
            var obj = ObjRef.Read(r);
            bool talking = r.R.ReadByte() != 0;
            string key = Key(obj, r.ReadStr());
            if (!talking) { Release(key, from); return; }
            string other = TalkingOther(key, from);
            if (other != null) { Authority.Refuse($"{other} is talking to them"); return; }
            if (_released.TryGetValue(key, out var rel) && rel.Key != from && UnityEngine.Time.unscaledTime - rel.Value < Grace)
            {
                Authority.Refuse($"{s.NameOf(rel.Key)} just talked to them: try again in a moment");
                return;
            }
            var npc = ObjRef.Resolve<NPC>(obj);
            if (npc != null && !Authority.InReach(npc.transform.position, 1500f, npc.name)) return;
            _talkers[key] = from;
            if (WorldActions.Debug) Plugin.Log.LogInfo($"[world] {s.NameOf(from)} talks to {(npc != null ? npc.name : obj.ToString())}");
        }
    }

    [HarmonyPatch(typeof(NPC), nameof(NPC.talkTo))]
    static class TalkToPatch
    {
        static bool Prefix(NPC __instance) =>
            __instance.characterDialogue == null || !__instance.wantsToTalk || TalkLock.HostMayTalk(__instance);

        static void Postfix(NPC __instance)
        {
            var win = Singleton<UI>.Instance?.dialogueWindow;
            if (win != null && win.npc == __instance) TalkLock.Opened(__instance);
        }
    }

    [HarmonyPatch(typeof(DialogueWindow), nameof(DialogueWindow.close))]
    static class DialogueClosePatch
    {
        static void Postfix(DialogueWindow __instance)
        {
            if (__instance.npc == null) TalkLock.Closed();
        }
    }
}
