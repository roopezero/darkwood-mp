using System;
using System.Collections.Generic;
using System.Linq;
using DarkwoodMP.Combat;
using DarkwoodMP.Net;
using DarkwoodMP.World.Story;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DarkwoodMP.World
{
    /// <summary>
    /// The story's state, shared: Darkwood keeps what has happened in Flags:
    /// named flags (a quest step done, a door's key found, an NPC told
    /// something), each NPC's state (reputation, whether they want to talk,
    /// dead, who attacked them) and which lines of each dialogue have been
    /// said. Talking to an NPC, trading with them, finishing a quest or killing
    /// someone changes it in that player's game.
    ///
    /// The journal goes with it: the story's entries, the notes read, keys and
    /// quest items found, the places found on the map, and the directions to
    /// places. Another player's new entry is added here, with the game's own
    /// "journal updated" popup for an entry, a note or a place (not for a place
    /// the game finds quietly, and not for anything the game has no text for:
    /// JournalPopupTextPatch). An entry, note, key or item can also go: a
    /// dialogue's removeItem takes a key or quest item the player hands over.
    /// That goes as the value "-", and the entry is removed in the other games
    /// too. What a player picks up in a dream is marked inDream, and the game
    /// drops it from the journal as the dream ends (Journal.clearDreamEntries).
    /// In a dream every game is in (SharedDream: the prologue's stages, the
    /// level dreams) it's shared like the rest, still marked inDream, so the
    /// key to a door in the dream opens it for everyone and every game drops it
    /// as its own dream ends; that drop isn't sent. In a dream of one player's
    /// own it's theirs alone and isn't in the snapshot.
    ///
    /// Every two seconds each game looks for entries that changed since it last
    /// sent or got them, and sends those (FlagsDelta: u16 n, n x (str key,
    /// str value)); the host applies a client's and passes them on. Flags are
    /// set through the game's own setFlag. An NPC whose state says they died is
    /// killed where they're still alive. Achievements stay each player's own.
    ///
    /// Numbers that more than one game changes at about the same time aren't
    /// sent whole:
    /// - An NPC's reputation ("r:" key) goes as the change ("+d"), so two
    ///   trades close together both count. A reputation not sent before goes
    ///   whole ("=v"). The "n:" state is sent without it. Changes that every
    ///   game makes by itself aren't sent at all (Absorb): a shared scripted
    ///   event's reputation step, and a trade, whose change TraderTrade
    ///   carries.
    /// - The after-night reward for a trader ("ra:" key, "day|amount") counts
    ///   once a day, not once per game: the host keeps the first one it gets
    ///   for a day, and a joiner's own is replaced by the host's.
    /// - Karma ("k") is the host's: every game adds the same +20 at dawn, and
    ///   only the host sends it.
    /// </summary>
    internal sealed class FlagsSync
    {
        private readonly CoopSession _coop;
        /// <summary>
        /// The after-night reward each trader got.
        /// </summary>
        private readonly TraderRewards _rewards = new TraderRewards();
        private Dictionary<string, string> _baseline;
        private int _baselineScene = -1; // the world (scene) the baseline is of
        private Seconds _next;

        private static readonly
            AccessTools.FieldRef<Flags, Dictionary<string, Flags.Flag>>
            FlagsDict = AccessTools
                .FieldRefAccess<Flags, Dictionary<string, Flags.Flag>>(
                    "flagsDict");

        public FlagsSync(CoopSession coop)
        {
            _coop = coop;
            coop.Handle(MsgType.FlagsDelta, OnDelta);
        }

        private Session Net => _coop.Net;

        private bool Sharing => StoryRules.Sharing(SharingOf());

        private SharingFacts SharingOf()
        {
            bool ready = WorldShare.WorldReady;
            var net = _coop.Net;
            bool host = false;
            int peers = 0;
            if (ready)
            {
                host = net.IsHost;
                peers = net.Peers.Count;
            }

            return new SharingFacts
            {
                WorldReady = ready,
                Host = host,
                Peers = peers,
                Client = ready && NetEnemies.ClientMode,
            };
        }

        private BaselineFacts BaselineOf()
        {
            var net = _coop.Net;
            bool active = net.Active;
            bool host = net.IsHost;
            int peers = net.Peers.Count;

            bool sameScene = UnityEngine.SceneManagement.SceneManager
                .GetActiveScene().handle == _baselineScene;
            return new BaselineFacts
            {
                Known = _baseline != null,
                Active = active,
                InWorld = WorldShare.InWorld,
                SameScene = sameScene,
                Loading = WorldShare.Loading,
                Host = host,
                Peers = peers,
                Client = NetEnemies.ClientMode,
            };
        }

        public void Update()
        {
            if (!Sharing)
            {
                // A world being (re)loaded, another scene, or a host with
                // nobody in: start again from what there is when sharing
                // starts, since that's what the others load (reputation goes as
                // changes, which must count from there). Any other pause
                // (inputs forbidden while dead, dreaming, spectating, the
                // couple of seconds after a load) keeps what was last sent, so
                // the changes made meanwhile go when it resumes: a player dying
                // sets player_diedAtLeastOneTime as its inputs are forbidden.
                if (!StoryRules.KeepsBaseline(BaselineOf()))
                {
                    _baseline = null;
                }

                return;
            }

            if (!StoryRules.IsDue(new Seconds(Time.unscaledTime), _next))
            {
                return;
            }

            Flush();
        }

        /// <summary>
        /// Send what changed now, not at the next two-second tick (a dialogue
        /// closing: TalkLock).
        /// </summary>
        public void FlushNow()
        {
            if (Sharing)
            {
                Flush();
            }
        }

        private void Flush()
        {
            _next = StoryRules.NextLook(new Seconds(Time.unscaledTime));
            var now = Snapshot(Net.IsHost);
            if (now == null)
            {
                return;
            }

            if (_baseline == null) // what we start from isn't news
            {
                _baseline = now;
                _baselineScene =
                    UnityEngine.SceneManagement.SceneManager
                        .GetActiveScene().handle;
                return;
            }

            var changed = StoryDiff.Changes(_baseline, now, SharedDream);
            _baseline = now;
            if (changed.Count > 0)
            {
                Send(changed, except: 0);
            }
        }

        /// <summary>
        /// Whether our game is in a dream every game is in (DreamSync.Shared),
        /// so what's found in it is shared. Once the dream ends here, it isn't.
        /// (Going from one prologue stage to the next isn't an end: the game
        /// keeps what was found in the first stage until the dreaming ends,
        /// Dreams.endDreaming.)
        /// </summary>
        internal static bool SharedDream
        {
            get
            {
                var d = Singleton<Dreams>.Instance;
                return DreamSync.Shared != null && d != null && d.dreaming;
            }
        }

        /// <summary>
        /// Client: our journal's key of this type, sent again (FlagsDelta) just
        /// before we unlock with it, on the same ordered channel as the unlock
        /// request, so the host's check (WorldActions.HasKey) finds it even
        /// when the two-second sending missed it or the host didn't take it
        /// then.
        /// </summary>
        internal void SendKey(string type)
        {
            if (!Sharing || string.IsNullOrEmpty(type))
            {
                return;
            }

            UI instance = Singleton<UI>.Instance;
            Journal j = null;
            if (instance != null)
            {
                j = instance.journal;
            }

            if (j == null || !j.keysDict.TryGetValue(type, out var k)
                || k == null)
            {
                return;
            }

            if (!StoryRules.KeepsDreamEntry(k.inDream, SharedDream))
            {
                return; // a key of a dream of our own: the host can't have it
            }

            string json = JsonConvert.SerializeObject(k);
            if (_baseline != null)
            {
                _baseline["jk:" + type] = json;
            }

            Send(new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("jk:" + type, json)
            }, except: 0);
        }

        /// <summary>
        /// Our journal's entry for a "je:", "jn:", "jk:" or "ji:" key, as
        /// Snapshot writes it (null: none).
        /// </summary>
        private static string OwnJournal(string key)
        {
            UI instance = Singleton<UI>.Instance;
            Journal j = null;
            if (instance != null)
            {
                j = instance.journal;
            }

            if (j == null || key.Length < 4)
            {
                return null;
            }

            string name = key.Substring(3);
            object e = null;
            switch (key[1])
            {
                case 'e':
                    if (j.journalEntriesDict.TryGetValue(name, out var x))
                    {
                        e = x;
                    }

                    break;
                case 'n':
                    if (j.notesDict.TryGetValue(name, out var n))
                    {
                        e = n;
                    }

                    break;
                case 'k':
                    if (j.keysDict.TryGetValue(name, out var k))
                    {
                        e = k;
                    }

                    break;
                case 'i':
                    if (j.itemsDict.TryGetValue(name, out var i))
                    {
                        e = i;
                    }

                    break;
            }

            if (e != null)
            {
                return JsonConvert.SerializeObject(e);
            }

            return null;
        }

        /// <summary>
        /// A change to this NPC's reputation that every game makes by itself,
        /// or that another message carries: it isn't sent as a change. Call it
        /// in the same frame as the change.
        /// </summary>
        public void Absorb(string npc, int delta)
        {
            if (_baseline == null || npc == null)
            {
                return;
            }

            string key = StoryKeys.Reputation + npc;
            _baseline.TryGetValue(key, out var was);
            string absorbed = StoryDiff.Absorbed(was, delta);
            if (absorbed != null)
            {
                _baseline[key] = absorbed;
            }
        }

        /// <summary>
        /// Change this NPC's reputation here, without sending the change.
        /// </summary>
        internal void AddReputation(string npc, int delta)
        {
            Flags instance = Singleton<Flags>.Instance;
            Flags.NPCState st = null;
            if (instance != null)
            {
                st = instance.getNPCState(npc);
            }

            if (st == null || delta == 0)
            {
                return;
            }

            st.reputation += delta;
            Absorb(npc, delta);
            RefreshReputation(npc);
        }

        /// <summary>
        /// This game gave a trader the after-night reward
        /// (Controller.startAfterNight; already absorbed). It counts once a
        /// day: if another game's for today came first, ours is taken back;
        /// otherwise it goes to the others.
        /// </summary>
        internal void LocalReward(string trader, int day, int amount)
        {
            if (!Sharing)
            {
                return;
            }

            var got = _rewards.Local(trader, day, amount);
            if (got.Kind == RewardKind.Ignored)
            {
                return;
            }

            if (got.Kind == RewardKind.TakenBack)
            {
                AddReputation(trader, got.Change);
                Plugin.Log.LogInfo(
                    $"[world] {trader} already got day {day}'s after-night "
                        + $"reward ({got.Had}) in another game: ours "
                        + $"({amount}) taken back");
                return;
            }

            Plugin.Log.LogInfo(
                $"[world] {trader}'s after-night reward for day {day}: "
                    + $"{amount}, sent to the others");
            Send(new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>(
                    StoryKeys.Reward + trader,
                    StoryKeys.RewardValue(day, amount))
            }, except: 0);
        }

        /// <summary>
        /// Another game's after-night reward for a trader. Returns whether the
        /// host passes it on.
        /// </summary>
        private bool ApplyReward(string trader, string value)
        {
            StoryKeys.ReadReward(value, out int day, out int amount);
            RewardOutcome got;
            if (Net.IsHost)
            {
                got = _rewards.TakeOnHost(trader, day, amount);
            }
            else
            {
                // the host's is the one that counts
                got = _rewards.TakeOnClient(trader, day, amount);
            }

            if (got.Kind == RewardKind.Refused)
            {
                Plugin.Log.LogInfo(
                    $"[world] {trader} already got day {day}'s after-night "
                        + $"reward: another one ({amount}) not given");
                return false;
            }

            AddReputation(trader, got.Change);
            Plugin.Log.LogInfo(
                $"[world] {trader}'s after-night reward for day {day}: "
                    + $"{amount}");
            return true;
        }

        private static Dictionary<string, string> Snapshot(bool host)
        {
            var f = Singleton<Flags>.Instance;
            if (f == null)
            {
                return null;
            }

            var d = new Dictionary<string, string>();
            if (host)
            {
                d["k"] = f.karmaPoints.ToString();
            }

            foreach (var kv in FlagsDict(f))
            {
                if (kv.Value != null && !StoryRules.PlayersOwn(kv.Key))
                {
                    d["f:" + kv.Key] = $"{kv.Value.isTrue}|{kv.Value.amount}";
                }
            }

            foreach (var n in f.npcStates)
            {
                if (n == null || n.name == null)
                {
                    continue;
                }

                d["n:" + n.name] = NpcJson(n);
                d["r:" + n.name] = n.reputation.ToString();
            }

            foreach (var dl in f.dialogues)
            {
                if (dl != null)
                {
                    d["d:" + dl.name] = JsonConvert.SerializeObject(
                        new CharacterDialogue.SaveState(dl));
                }
            }

            UI instance = Singleton<UI>.Instance;
            Journal j = null;
            if (instance != null)
            {
                j = instance.journal;
            }

            if (j != null)
            {
                // what's found in a dream: only a shared dream's (see the
                // class)
                bool dream = SharedDream;
                foreach (var e in j.journalEntriesDict.Values)
                {
                    if (e != null && (dream || !e.inDream))
                    {
                        d["je:" + e.type] = JsonConvert.SerializeObject(e);
                    }
                }

                foreach (var e in j.notesDict.Values)
                {
                    if (e != null && (dream || !e.inDream))
                    {
                        d["jn:" + e.type] = JsonConvert.SerializeObject(e);
                    }
                }

                foreach (var e in j.keysDict.Values)
                {
                    if (e != null && (dream || !e.inDream))
                    {
                        d["jk:" + e.type] = JsonConvert.SerializeObject(e);
                    }
                }

                foreach (var e in j.itemsDict.Values)
                {
                    if (e != null && (dream || !e.inDream))
                    {
                        d["ji:" + e.type] = JsonConvert.SerializeObject(e);
                    }
                }

                foreach (var name in j.locationsDict.Keys)
                {
                    d["jl:" + name] = "";
                }

                foreach (var e in j.locationDirections)
                {
                    if (e != null && e.locationName != null)
                    {
                        d["jd:" + e.locationName] =
                            JsonConvert.SerializeObject(e);
                    }
                }
            }

            return d;
        }

        /// <summary>
        /// An NPC's "n:" value: its state as JSON, without the reputation (that
        /// goes as "r:").
        /// </summary>
        private static string NpcJson(Flags.NPCState n)
        {
            var jo = JObject.FromObject(n);
            jo.Remove("reputation");
            return jo.ToString(Formatting.None);
        }

        /// <summary>
        /// Host: what the story was in the frame the last joiner's snapshot was
        /// read (null: not known).
        /// </summary>
        private Dictionary<string, string> _atSnapshot;

        /// <summary>
        /// Host, in the frame the world is read for a joiner's snapshot: the
        /// story as the snapshot has it. What changes after that, while the
        /// joiner downloads and loads it, reaches the joiner no other way:
        /// FlagsDelta is taken only by games in the world (a loading one drops
        /// it), and with nobody in yet nothing is sent at all, the first look
        /// after the joiner is let in taking what there is then as known.
        /// JoinerInWorld sends the difference.
        /// </summary>
        public void NoteSnapshot()
        {
            _atSnapshot = Snapshot(true);
            SnapshotNoted++;
        }

        /// <summary>
        /// How many snapshots' story was noted (for the tests).
        /// </summary>
        internal int SnapshotNoted { get; private set; }

        /// <summary>
        /// Host: the world offered to joiners isn't a snapshot (LiveWorld off:
        /// the last save).
        /// </summary>
        public void ForgetSnapshot() => _atSnapshot = null;

        /// <summary>
        /// Host: a joiner is in the world it loaded (it asked for its
        /// character). Every story entry that changed since its snapshot is
        /// sent to that joiner alone (FlagsDelta; reputation whole, "=v";
        /// entries handed over, "-").
        /// </summary>
        public void JoinerInWorld(ulong who)
        {
            if (!Net.IsHost || _atSnapshot == null)
            {
                return;
            }

            var now = Snapshot(true);
            if (now == null)
            {
                return;
            }

            // as Flush: a dream entry the game dropped as the dream ended
            // isn't sent
            var changed =
                StoryDiff.SinceSnapshot(_atSnapshot, now, SharedDream);
            if (changed.Count == 0)
            {
                return;
            }

            Send(changed, except: 0, to: who);
            string name = Net.NameOf(who);
            string keys = string.Join(", ", changed.Take(8).Select(e => e.Key));
            string more = "";
            if (changed.Count > 8)
            {
                more = ", ...";
            }

            Plugin.Log.LogInfo(
                $"{name} is in the world: {changed.Count} story changes since "
                    + $"their snapshot sent to them ({keys}{more})");
        }

        private void Send(
            List<KeyValuePair<string, string>> entries, ulong except,
            ulong to = 0)
        {
            // In chunks, to keep each message well under any size limit.
            for (int start = 0; start < entries.Count;
                start += StoryRules.PerMessage)
            {
                int n = StoryRules.ChunkLength(start, entries.Count);
                var w = Net.Begin(MsgType.FlagsDelta);
                FlagsDelta.Write(w, entries.GetRange(start, n));

                if (Net.IsHost && to != 0)
                {
                    Net.SendTo(to, w, Channel.Control, reliable: true);
                }
                else if (Net.IsHost)
                {
                    Net.Broadcast(
                        w, Channel.Control, reliable: true, except: except);
                }
                else
                {
                    Net.SendToHost(w, Channel.Control, reliable: true);
                }
            }

            if (WorldActions.Debug)
            {
                Plugin.Log.LogInfo(
                    $"[world] sent {entries.Count} story changes: "
                        + string.Join(", ", entries.ConvertAll(e => e.Key)));
            }
        }

        private void OnDelta(ulong from, PacketReader r)
        {
            if (!WorldShare.InWorld)
            {
                return;
            }

            if (Net.IsClient && from != Net.HostId)
            {
                return;
            }

            var got = FlagsDelta.Read(r);
            var relay = new List<KeyValuePair<string, string>>();
            foreach (var kv in got)
            {
                bool pass = true;
                try
                {
                    pass = Take(from, kv.Key, kv.Value);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning(
                        $"Couldn't apply {kv.Key}: {e.Message}");
                }

                if (pass)
                {
                    relay.Add(kv);
                }
            }

            if (WorldActions.Debug)
            {
                List<string> changes = got.ConvertAll(e =>
                {
                    if (e.Key[0] == 'r' || e.Value == StoryKeys.Removed)
                    {
                        return e.Key + " " + e.Value;
                    }

                    return e.Key;
                });

                Plugin.Log.LogInfo(
                    $"[world] got {got.Count} story changes: "
                        + $"{string.Join(", ", changes)}");
            }

            if (Net.IsHost && relay.Count > 0)
            {
                Send(relay, except: from);
            }
        }

        /// <summary>
        /// Another game's story entry, taken here. Returns whether the host
        /// passes it on.
        /// </summary>
        private bool Take(ulong from, string key, string value)
        {
            bool host = Net.IsHost;
            switch (StoryRules.Route(key, host))
            {
                case StoryRoute.Reward:
                    return ApplyReward(
                        key.Substring(StoryKeys.Reward.Length), value);
                case StoryRoute.Reputation:
                    ApplyReputation(
                        key.Substring(StoryKeys.Reputation.Length), value);
                    return true;
                case StoryRoute.HostsKarma:
                    return false; // karma is the host's
                case StoryRoute.NpcCheck:
                    if (!DeathOk(
                        from, key.Substring(StoryKeys.Npc.Length), value))
                    {
                        return false;
                    }

                    break;
            }

            Apply(key, value);
            if (_baseline == null)
            {
                return true;
            }

            // A journal entry: what our journal has now, which can differ
            // from theirs (one we had already, made by our own game). Taking
            // theirs as the baseline would send ours back at every look, and
            // theirs again, for good. One we didn't take isn't ours to send.
            // Anything else is known now; not ours to send back.
            string own = null;
            if (StoryKeys.IsRemovable(key) && value != StoryKeys.Removed)
            {
                own = OwnJournal(key);
            }

            string kept = StoryDiff.AfterTaking(key, value, own);
            if (kept == null)
            {
                _baseline.Remove(key);
            }
            else
            {
                _baseline[key] = kept;
            }

            return true;
        }

        /// <summary>
        /// Host: NPCs whose death a joiner's NpcDied showed us just now
        /// (WorldActions), and when.
        /// </summary>
        private readonly AllowedKills _deathsAllowed = new AllowedKills();

        /// <summary>
        /// Host: a joiner killed this NPC and we checked it (their NpcDied
        /// request was allowed).
        /// </summary>
        internal void DeathAllowed(string npc)
        {
            _deathsAllowed.Allow(npc, new Seconds(Time.unscaledTime));
        }

        /// <summary>
        /// Host: checks a joiner's NPC state against our NPC's death. A
        /// player's kill reaches us first as an NpcDied request, which we check
        /// (reach, the NPC here) and which kills our copy; a dead state that
        /// didn't come that way isn't taken (or any client could kill any NPC
        /// everywhere). Nothing in the game brings a dead NPC back, so a state
        /// that says alive over our dead one is out of date (sent before our
        /// death reached them) and isn't taken either. Either way the sender
        /// gets our state back.
        /// </summary>
        private bool DeathOk(ulong from, string npc, string value)
        {
            var st = Singleton<Flags>.Instance.getNPCState(npc);
            switch (NpcStateRules.Judge(FactsOf(st, npc, value)))
            {
                case NpcStateOutcome.Stale:
                    SendNpcState(from, npc, st);
                    return false;
                case NpcStateOutcome.NotAllowed:
                    Plugin.Log.LogInfo(
                        $"[authority] refused {Net.NameOf(from)}'s "
                            + $"state of {npc}: dead, but no kill of theirs "
                            + "was allowed here");
                    SendNpcState(from, npc, st);
                    return false;
                default:
                    return true;
            }
        }

        /// <summary>
        /// What the host knows of a joiner's state of an NPC: its "dead"
        /// (unreadable: Apply reports it), the host's own state, and whether
        /// a kill of theirs was allowed just now.
        /// </summary>
        private NpcStateFacts FactsOf(
            Flags.NPCState ours, string npc, string value)
        {
            bool readable = true;
            bool dead = false;
            try
            {
                bool? saved = JObject.Parse(value).Value<bool?>("dead");
                if (saved.HasValue)
                {
                    dead = saved.Value;
                }
            }
            catch
            {
                readable = false;
            }

            bool allowed = false;
            if (readable && dead)
            {
                allowed = _deathsAllowed.Recent(
                    npc, new Seconds(Time.unscaledTime));
            }

            return new NpcStateFacts
            {
                Readable = readable,
                TheirsDead = dead,
                OursKnown = ours != null,
                OursDead = ours != null && ours.dead,
                KillAllowed = allowed,
            };
        }

        private void SendNpcState(ulong to, string npc, Flags.NPCState st)
        {
            if (st == null || st.name == null)
            {
                return;
            }

            var w = Net.Begin(MsgType.FlagsDelta);
            FlagsDelta.Write(w, new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("n:" + npc, NpcJson(st))
            });

            Net.SendTo(to, w, Channel.Control, reliable: true);
        }

        /// <summary>
        /// Another game's reputation change ("+d"), or a reputation sent whole
        /// ("=v").
        /// </summary>
        private void ApplyReputation(string npc, string value)
        {
            var st = Singleton<Flags>.Instance.getNPCState(npc);
            if (st == null)
            {
                return;
            }

            int v = int.Parse(value.Substring(1));
            if (value[0] != '=')
            {
                AddReputation(npc, v);
                return;
            }

            st.reputation = v;
            if (_baseline != null)
            {
                _baseline["r:" + npc] = v.ToString();
            }

            RefreshReputation(npc);
        }

        private static void Apply(string key, string value)
        {
            var f = Singleton<Flags>.Instance;
            var kind = StoryKeys.KindOf(key);
            if (kind == StoryKey.Malformed)
            {
                throw new FormatException("the key is too short");
            }

            if (kind == StoryKey.Karma)
            {
                f.karmaPoints = int.Parse(value);
                return;
            }

            if (kind == StoryKey.Journal)
            {
                ApplyJournal(key[1], key.Substring(3), value);
                return;
            }

            string name = key.Substring(2);
            switch (kind)
            {
                case StoryKey.Flag:
                    var home = StoryRules.FlagHomeOf(name);
                    if (home == FlagHome.PlayersOwn)
                    {
                        break;
                    }

                    var parts = value.Split('|');
                    if (home == FlagHome.Mod)
                    {
                        SetModFlag(
                            f, name, bool.Parse(parts[0]), int.Parse(parts[1]));
                        break;
                    }

                    f.setFlag(name, bool.Parse(parts[0]));
                    f.setFlag(name, int.Parse(parts[1]));
                    break;
                case StoryKey.Npc:
                    var state = f.getNPCState(name);
                    bool wasDead = state != null && state.dead;
                    if (state == null)
                    {
                        f.npcStates.Add(
                            state =
                                JsonConvert.DeserializeObject<Flags.NPCState>(
                                    value));
                    }
                    else
                    {
                        JsonConvert.PopulateObject(value, state);
                    }

                    if (state.dead && !wasDead)
                    {
                        KillNpc(name, state.deadID);
                    }

                    RefreshReputation(name);
                    break;
                case StoryKey.Dialogue:
                    var dialogue = f.getDialogue(name);
                    if (dialogue != null)
                    {
                        JsonConvert
                            .DeserializeObject<CharacterDialogue.SaveState>(
                                value).loadValues(dialogue);
                    }

                    break;
            }
        }

        /// <summary>
        /// Set one of the mod's own flags (StoryKeys.ModPrefix) in
        /// Flags.flagsDict, which the game's setFlag won't take.
        /// Flags.SaveState saves every entry there, so they're in the host's
        /// save and a joiner's snapshot, and the game itself never reads
        /// them.
        /// </summary>
        internal static void SetModFlag(
            Flags f, string name, bool on, int amount)
        {
            if (!f.flagsDict.TryGetValue(name, out var flag) || flag == null)
            {
                f.flagsDict[name] = flag = new Flags.Flag { name = name };
            }

            flag.isTrue = on;
            flag.amount = amount;
        }

        /// <summary>
        /// The trade window with this NPC is open: its reputation label shows
        /// the new value at once.
        /// </summary>
        private static void RefreshReputation(string npc)
        {
            NPC talked = null;
            if (Player.Instance != null)
            {
                talked = Player.Instance.talkedToNPC;
            }

            if (talked == null || talked.name != npc
                || talked.inventory == null
                || talked.inventory.reputationText == null)
            {
                return;
            }

            if (!talked.inventory.reputationText.gameObject.activeInHierarchy)
            {
                return;
            }

            try
            {
                talked.inventory.refreshReputation();
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning(
                    $"Couldn't refresh {npc}'s reputation: {e.Message}");
            }
        }

        /// <summary>
        /// A place another player found (walking into it, or reading a map:
        /// Location.discoverMe) is shown on our map too, as discoverMe shows it
        /// on theirs: Map.showElement on the location's MapElement.
        /// </summary>
        private static void ShowOnMap(string name)
        {
            try
            {
                WorldGenerator instance = Singleton<WorldGenerator>.Instance;
                Location loc = null;
                if (instance != null)
                {
                    loc = instance.getLocation(name);
                }

                MapElement el = null;
                if (loc != null)
                {
                    el = loc.GetComponent<MapElement>();
                }

                if (el == null || el.isOnMap)
                {
                    return;
                }

                Map map = Singleton<Map>.Instance;
                if (map != null)
                {
                    map.showElement(el);
                }

                Plugin.Log.LogInfo($"Journal: {name} shown on the map");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning(
                    $"Couldn't show {name} on the map: {e.Message}");
            }
        }

        /// <summary>
        /// Another player's new journal entry: added here if it isn't yet
        /// (removed, for "-").
        /// </summary>
        private static void ApplyJournal(char kind, string name, string value)
        {
            UI instance = Singleton<UI>.Instance;
            Journal j = null;
            if (instance != null)
            {
                j = instance.journal;
            }

            if (j == null)
            {
                return;
            }

            var take = JournalRules.Take(kind, value, SharedDream);
            if (take == JournalTake.Remove)
            {
                bool gone = kind switch
                {
                    'e' => j.journalEntriesDict.Remove(name),
                    'n' => j.notesDict.Remove(name),
                    'k' => j.keysDict.Remove(name),
                    'i' => j.itemsDict.Remove(name),
                    _ => false,
                };

                if (gone)
                {
                    Plugin.Log.LogInfo(
                        $"Journal: another player no longer has {name} (handed "
                            + "over): removed here too");
                }

                return;
            }

            if (take == JournalTake.OtherDream)
            {
                // Found in a shared dream that has ended here (or not begun):
                // the game would never drop it.
                Plugin.Log.LogInfo(
                    $"Journal: another player's {name} from a dream we aren't "
                        + "in: not taken");
                return;
            }

            if (take == JournalTake.Unknown)
            {
                return;
            }

            bool quiet = false;
            switch (kind)
            {
                case 'e':
                    if (j.journalEntriesDict.ContainsKey(name))
                    {
                        return;
                    }

                    j.journalEntriesDict[name] =
                        JsonConvert.DeserializeObject<Journal.JournalEntry>(
                            value);
                    break;
                case 'n':
                    if (j.notesDict.ContainsKey(name))
                    {
                        return;
                    }

                    j.notesDict[name] =
                        JsonConvert.DeserializeObject<Journal.Note>(value);
                    break;
                case 'k':
                    if (j.keysDict.ContainsKey(name))
                    {
                        return;
                    }

                    j.keysDict[name] =
                        JsonConvert.DeserializeObject<Journal.Key>(value);
                    break;
                case 'i':
                    if (j.itemsDict.ContainsKey(name))
                    {
                        return;
                    }

                    j.itemsDict[name] =
                        JsonConvert.DeserializeObject<Journal.Item>(value);
                    break;
                case 'l':
                    if (j.locationsDict.ContainsKey(name))
                    {
                        return;
                    }

                    j.locationsDict[name] = name;
                    quiet = QuietPlace(name);

                    ShowOnMap(name);
                    break;
                case 'd':
                    if (j.getLocationDirection(name) != null)
                    {
                        return;
                    }

                    j.locationDirections.Add(
                        JsonConvert
                            .DeserializeObject<Journal.LocationDirection>(
                                value));
                    break;
            }

            string what = JournalRules.Noun(kind);
            Plugin.Log.LogInfo(
                "Journal: another player's " + $"{what} {name}");
            string popup = JournalRules.Popup(kind, quiet);
            if (popup != null && WorldShare.WorldReady)
            {
                try
                {
                    j.showJournalInfoPopup(popup, name);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning(
                        $"Couldn't show the journal popup for {name}: "
                            + $"{e.Message}");
                }
            }
        }

        /// <summary>Whether the game finds this place without a popup
        /// (Location.discoverMe): it's marked noDiscoverPopup, or it counts as
        /// found from the start.</summary>
        private static bool QuietPlace(string name)
        {
            foreach (var loc in Resources.FindObjectsOfTypeAll<Location>())
            {
                if (loc != null && Core.getTrueLocationName(loc.name) == name
                    && (loc.noDiscoverPopup || loc.discoveredOnStart))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Someone killed this NPC in their game: it dies here too, if it's
        /// still alive. Several copies of a story NPC can stand at once (the
        /// wolfman as a trader in more than one place, and as a night
        /// attacker); the one that died has the save id in deadID
        /// (Character.die), so when every game knows that id only that copy
        /// dies. The game itself removes the other copies only as they next
        /// start (NPC.Start, onlyOneInstance).
        /// </summary>
        private static void KillNpc(string name, int deadId)
        {
            foreach (var npc in UnityEngine.Object.FindObjectsOfType<NPC>())
            {
                if (!NpcStateRules.Kills(FactsOf(npc, name, deadId)))
                {
                    continue;
                }

                var c = npc.GetComponent<Character>();

                Plugin.Log.LogInfo(
                    $"{name} was killed in another player's game");
                Coop.Guards.Replaying = true;
                try
                {
                    c.die();
                }
                finally
                {
                    Coop.Guards.Replaying = false;
                }
            }
        }

        /// <summary>
        /// One NPC here, when another game's state says the NPC of this
        /// name died, with this save id. A copy of one the host runs as an
        /// enemy dies with the host's state (Combat.NetEnemies).
        /// </summary>
        private static NpcCopyFacts FactsOf(NPC npc, string name, int deadId)
        {
            if (npc.name != name)
            {
                return new NpcCopyFacts { SameName = false };
            }

            var sv = npc.GetComponent<SaveableObject>();
            int saveId = 0;
            if (sv != null)
            {
                saveId = sv.uniqueId;
            }

            var c = npc.GetComponent<Character>();
            bool alive = c != null && c.alive;
            bool puppet = alive && Combat.NetEnemies.Instance != null
                && Combat.NetEnemies.Instance.IsPuppet(c, out _);
            return new NpcCopyFacts
            {
                SameName = true,
                ById = NpcStateRules.ById(deadId, SharedIds.SendLimit),
                Saved = sv != null,
                SaveId = saveId,
                DeadId = deadId,
                Alive = alive,
                Puppet = puppet,
            };
        }
    }

    /// <summary>
    /// The "journal updated" popup (Journal.showJournalInfoPopup) shows the
    /// game's text for what was found, looked up by its name. When the game has
    /// no text for that name, Language.Get gives back the bare name, and the
    /// popup would say something like "Location found: place_10". This happens
    /// for some small places in the game itself, and for a place, entry or note
    /// another player found (FlagsSync), so such a popup isn't shown at all;
    /// what was found is still added to the journal.
    /// </summary>
    [HarmonyPatch(
        typeof(Journal), nameof(Journal.showJournalInfoPopup),
        new[] { typeof(string), typeof(string) })]
    static class JournalPopupTextPatch
    {
        static bool Prefix(string _type, string _desc)
        {
            bool has;
            try
            {
                var text = JournalRules.TextOf(_type, _desc);
                if (text.Always)
                {
                    // reputation and others carry their own text
                    return true;
                }

                has = Has(text.Key, text.Sheet)
                    || (text.OtherSheet != null
                        && Has(text.Key, text.OtherSheet));
            }
            catch (Exception)
            {
                return true; // the language isn't loaded: leave it to the game
            }

            if (!has)
            {
                Plugin.Log.LogInfo(
                    $"Journal: no popup for {_type} {_desc}, the game has no "
                        + "text for it");
            }

            return has;
        }

        private static bool Has(string key, string sheet)
            => !string.IsNullOrEmpty(key)
                && global::Language.HasKey(key, sheet)
                && !string.IsNullOrEmpty(global::Language.Get(key, sheet));
    }

    /// <summary>
    /// A scripted event's reputation step (GameEvent modifyCharacter /
    /// reputation) is a shared step: it runs in every game (ScriptedEvents), so
    /// each game's change is its own and isn't sent as a change too.
    /// </summary>
    [HarmonyPatch]
    static class EventReputationPatch
    {
        private static System.Reflection.FieldInfo _this;

        static System.Reflection.MethodBase TargetMethod()
        {
            var mn = AccessTools.EnumeratorMoveNext(
                AccessTools.Method(typeof(GameEvent), nameof(GameEvent.fire)));
            if (mn != null)
            {
                _this = AccessTools.Field(mn.DeclaringType, "<>4__this");
            }

            return mn;
        }

        static void Prefix(object __instance, out int __state)
        {
            __state = int.MinValue;
            if (_this == null || Coop.Current == null)
            {
                return;
            }

            var ev = _this.GetValue(__instance) as GameEvent;
            if (ev == null || ev.type != GameEvent.Type.modifyCharacter
                || ev.characterModifyType
                    != GameEvent.CharacterModify.reputation)
            {
                return;
            }

            Flags instance = Singleton<Flags>.Instance;
            Flags.NPCState st = null;
            if (instance != null)
            {
                st = instance.getNPCState(ev.Value);
            }

            if (st != null)
            {
                __state = st.reputation;
            }
        }

        static void Postfix(object __instance, int __state)
        {
            if (__state == int.MinValue)
            {
                return;
            }

            var ev = (GameEvent)_this.GetValue(__instance);
            Flags instance = Singleton<Flags>.Instance;
            Flags.NPCState st = null;
            if (instance != null)
            {
                st = instance.getNPCState(ev.Value);
            }

            CoopSession coop = Coop.Current;
            if (coop != null && st != null && st.reputation != __state)
            {
                coop.Story.Absorb(ev.Value, st.reputation - __state);
            }
        }
    }
}
