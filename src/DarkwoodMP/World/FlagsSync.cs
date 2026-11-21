using System;
using System.Collections.Generic;
using System.Linq;
using DarkwoodMP.Combat;
using DarkwoodMP.Net;
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
    public sealed class FlagsSync
    {
        public const float Interval = 2f;
        /// <summary>
        /// The mod's own flags start with this (World.SharedTank's). The game's
        /// setFlag refuses a name that isn't in its flag list, so they go
        /// straight into Flags.flagsDict (SetModFlag); Flags.SaveState saves
        /// every entry there, so they're in the host's save and a joiner's
        /// snapshot, and the game itself never reads them.
        /// </summary>
        public const string ModPrefix = "dwmp_";
        public static FlagsSync Instance { get; private set; }
        private readonly Session _session;
        /// <summary>
        /// The after-night reward each trader got, by name: the day and the
        /// amount.
        /// </summary>
        private readonly Dictionary<string, KeyValuePair<int, int>> _rewards =
            new Dictionary<string, KeyValuePair<int, int>>();
        private Dictionary<string, string> _baseline;
        private int _baselineScene = -1; // the world (scene) the baseline is of
        private float _next;

        private static readonly
            AccessTools.FieldRef<Flags, Dictionary<string, Flags.Flag>>
            FlagsDict = AccessTools
                .FieldRefAccess<Flags, Dictionary<string, Flags.Flag>>(
                    "flagsDict");

        public FlagsSync(Session session)
        {
            Instance = this;
            _session = session;
            session.Register(MsgType.FlagsDelta, OnDelta);
            session.OnSessionEnded += () =>
            {
                _baseline = null;
                _rewards.Clear();
                _deathsAllowed.Clear();
            };
        }

        private static bool Sharing =>
            WorldShare.WorldReady
                && ((Session.Instance.IsHost
                        && Session.Instance.Peers.Count > 0)
                    || NetEnemies.ClientMode);

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
                bool sameWorld = _baseline != null && Session.Instance != null
                    && Session.Instance.Active && WorldShare.InWorld
                    && UnityEngine.SceneManagement.SceneManager
                        .GetActiveScene().handle == _baselineScene;
                if (!sameWorld || WorldShare.Loading
                    || (Session.Instance.IsHost
                        && Session.Instance.Peers.Count == 0)
                    || !(Session.Instance.IsHost || NetEnemies.ClientMode))
                {
                    _baseline = null;
                }

                return;
            }

            if (Time.unscaledTime < _next)
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
            _next = Time.unscaledTime + Interval;
            var now = Snapshot(_session.IsHost);
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

            var changed = new List<KeyValuePair<string, string>>();
            foreach (var kv in now)
            {
                bool had = _baseline.TryGetValue(kv.Key, out var was);
                if (had && was == kv.Value)
                {
                    continue;
                }

                if (kv.Key.StartsWith("r:"))
                {
                    // Reputation: the change, so that changes made in two games
                    // add up.
                    if (had && int.TryParse(was, out int w)
                        && int.TryParse(kv.Value, out int v))
                    {
                        changed.Add(
                            new KeyValuePair<string, string>(
                                kv.Key, "+" + (v - w)));
                    }
                    else
                    {
                        changed.Add(
                            new KeyValuePair<string, string>(
                                kv.Key, "=" + kv.Value));
                    }

                    continue;
                }

                changed.Add(kv);
            }

            // Journal entries that went (handed over in a dialogue).
            // Dream entries dropped as the dream ended aren't sent: every game
            // drops its own.
            bool shared = SharedDream;
            foreach (var kv in _baseline)
            {
                if (IsRemovable(kv.Key) && !now.ContainsKey(kv.Key)
                    && (shared || !DreamEntry(kv.Value)))
                {
                    changed.Add(
                        new KeyValuePair<string, string>(kv.Key, Removed));
                }
            }

            _baseline = now;
            if (changed.Count > 0)
            {
                Send(changed, except: 0);
            }
        }

        /// <summary>The value that says a journal entry was removed.</summary>
        private const string Removed = "-";
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
        /// Whether a journal entry's value (its JSON) is one found in a dream
        /// (inDream).
        /// </summary>
        private static bool DreamEntry(string value) =>
            value != null && value.Contains("\"inDream\":true");

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

            if (k.inDream && !SharedDream)
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

        private static bool IsRemovable(string key) =>
            key.Length > 3 && key[0] == 'j' && key[2] == ':'
                && (key[1] == 'e' || key[1] == 'n' || key[1] == 'k'
                    || key[1] == 'i');

        /// <summary>
        /// A change to this NPC's reputation that every game makes by itself,
        /// or that another message carries: it isn't sent as a change. Call it
        /// in the same frame as the change.
        /// </summary>
        public void Absorb(string npc, int delta)
        {
            if (_baseline == null || delta == 0 || npc == null)
            {
                return;
            }

            if (_baseline.TryGetValue("r:" + npc, out var was)
                && int.TryParse(was, out int w))
            {
                _baseline["r:" + npc] = (w + delta).ToString();
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
            if (!Sharing || trader == null || amount == 0)
            {
                return;
            }

            if (_rewards.TryGetValue(trader, out var had) && had.Key == day)
            {
                AddReputation(trader, -amount);
                Plugin.Log.LogInfo(
                    $"[world] {trader} already got day {day}'s after-night "
                        + $"reward ({had.Value}) in another game: ours "
                        + $"({amount}) taken back");
                return;
            }

            _rewards[trader] = new KeyValuePair<int, int>(day, amount);
            Plugin.Log.LogInfo(
                $"[world] {trader}'s after-night reward for day {day}: "
                    + $"{amount}, sent to the others");
            Send(new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>(
                    "ra:" + trader, $"{day}|{amount}")
            }, except: 0);
        }

        /// <summary>
        /// Another game's after-night reward for a trader. Returns whether the
        /// host passes it on.
        /// </summary>
        private bool ApplyReward(string trader, string value)
        {
            var parts = value.Split('|');
            int day = int.Parse(parts[0]), amount = int.Parse(parts[1]);
            bool had =
                _rewards.TryGetValue(trader, out var was) && was.Key == day;
            if (_session.IsHost)
            {
                if (had)
                {
                    Plugin.Log.LogInfo(
                        $"[world] {trader} already got day {day}'s after-night "
                            + $"reward: another one ({amount}) not given");
                    return false;
                }

                AddReputation(trader, amount);
            }
            else
            {
                int delta;
                if (had)
                {
                    delta = amount - was.Value;
                }
                else
                {
                    delta = amount;
                }

                // the host's is the one that counts
                AddReputation(trader, delta);
            }

            _rewards[trader] = new KeyValuePair<int, int>(day, amount);
            Plugin.Log.LogInfo(
                $"[world] {trader}'s after-night reward for day {day}: "
                    + $"{amount}");
            return true;
        }

        /// <summary>
        /// Flags about this game's own player, which each game keeps for itself
        /// (not sent, and another game's not taken): where the player is
        /// (PlayersPlace) and which one-time help popups this player has seen
        /// (PlayersPopup).
        /// </summary>
        public static bool PlayersOwn(string flag) =>
            PlayersPlace(flag) || PlayersPopup(flag);

        /// <summary>
        /// Where this game's own player is: WhereAmI.checkWhereAmI sets
        /// player_inFirstHideout (second, third) every time it looks, true only
        /// while the player is at that hideout. Two players at different places
        /// would otherwise overwrite each other's every two seconds.
        /// </summary>
        public static bool PlayersPlace(string flag) =>
            flag == "player_inFirstHideout"
                || flag == "player_inSecondHideout"
                || flag == "player_inThirdHideout";

        /// <summary>
        /// The game's one-time help popups, each shown on this game's screen
        /// and then marked as seen: the secondary attack hint the first time
        /// the player takes a melee weapon in hand
        /// (Player.onDoneSwitchingItem), the map hint (Map), the active skill
        /// hint (SkillsMenu.tryShowActiveSkillsHelpPopup), and the reload hint
        /// the first time something that can be reloaded runs out
        /// (InvItemClass.drainDurability, player_firstDrainReloadableItem). The
        /// game reads them only to decide whether to show that popup, so
        /// another player having seen it says nothing about ours: sharing them
        /// would hide a hint from a player who never saw it. (A joiner starts
        /// with the host's, which came with the host's save: what a joiner's
        /// game marks seen isn't kept with their character.)
        /// </summary>
        public static bool PlayersPopup(string flag) =>
            flag == "player_shownSecondaryAttackPopup"
                || flag == "player_shownMapPopup"
                || flag == "player_shownActiveSkillPopup"
                || flag == "player_firstDrainReloadableItem";

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
                if (kv.Value != null && !PlayersOwn(kv.Key))
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
        private static Dictionary<string, string> _atSnapshot;

        /// <summary>
        /// Host, in the frame the world is read for a joiner's snapshot: the
        /// story as the snapshot has it. What changes after that, while the
        /// joiner downloads and loads it, reaches the joiner no other way:
        /// FlagsDelta is taken only by games in the world (a loading one drops
        /// it), and with nobody in yet nothing is sent at all, the first look
        /// after the joiner is let in taking what there is then as known.
        /// JoinerInWorld sends the difference.
        /// </summary>
        public static void NoteSnapshot()
        {
            _atSnapshot = Snapshot(true);
            SnapshotNoted++;
        }

        /// <summary>
        /// How many snapshots' story was noted (for the tests).
        /// </summary>
        internal static int SnapshotNoted;

        /// <summary>
        /// Host: the world offered to joiners isn't a snapshot (LiveWorld off:
        /// the last save).
        /// </summary>
        public static void ForgetSnapshot() => _atSnapshot = null;

        /// <summary>
        /// Host: a joiner is in the world it loaded (it asked for its
        /// character). Every story entry that changed since its snapshot is
        /// sent to that joiner alone (FlagsDelta; reputation whole, "=v";
        /// entries handed over, "-").
        /// </summary>
        public void JoinerInWorld(ulong who)
        {
            if (!_session.IsHost || _atSnapshot == null)
            {
                return;
            }

            var now = Snapshot(true);
            if (now == null)
            {
                return;
            }

            var changed = new List<KeyValuePair<string, string>>();
            foreach (var kv in now)
            {
                if (_atSnapshot.TryGetValue(kv.Key, out var was)
                    && was == kv.Value)
                {
                    continue;
                }

                string stored;
                if (kv.Key.StartsWith("r:"))
                {
                    stored = "=" + kv.Value;
                }
                else
                {
                    stored = kv.Value;
                }

                changed.Add(new KeyValuePair<string, string>(kv.Key, stored));
            }

            // as Flush: a dream entry the game dropped as the dream ended isn't
            // sent
            bool shared = SharedDream;
            foreach (var kv in _atSnapshot)
            {
                if (IsRemovable(kv.Key) && !now.ContainsKey(kv.Key)
                    && (shared || !DreamEntry(kv.Value)))
                {
                    changed.Add(
                        new KeyValuePair<string, string>(kv.Key, Removed));
                }
            }

            if (changed.Count == 0)
            {
                return;
            }

            Send(changed, except: 0, to: who);
            string name = _session.NameOf(who);
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
            for (int start = 0; start < entries.Count; start += 64)
            {
                int n = Math.Min(64, entries.Count - start);
                var w = _session.Begin(MsgType.FlagsDelta);
                w.W.Write((ushort)n);
                for (int i = start; i < start + n; i++)
                {
                    w.WriteStr(entries[i].Key);
                    w.WriteStr(entries[i].Value);
                }

                if (_session.IsHost && to != 0)
                {
                    _session.SendTo(to, w, Channel.Control, reliable: true);
                }
                else if (_session.IsHost)
                {
                    _session.Broadcast(
                        w, Channel.Control, reliable: true, except: except);
                }
                else
                {
                    _session.SendToHost(w, Channel.Control, reliable: true);
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

            if (_session.IsClient && from != _session.HostId)
            {
                return;
            }

            int n = r.R.ReadUInt16();
            var got = new List<KeyValuePair<string, string>>();
            for (int i = 0; i < n; i++)
            {
                got.Add(
                    new KeyValuePair<string, string>(r.ReadStr(), r.ReadStr()));
            }

            var relay = new List<KeyValuePair<string, string>>();
            foreach (var kv in got)
            {
                bool pass = true;
                try
                {
                    if (kv.Key.StartsWith("ra:"))
                    {
                        pass = ApplyReward(kv.Key.Substring(3), kv.Value);
                    }
                    else if (kv.Key.StartsWith("r:"))
                    {
                        ApplyReputation(kv.Key.Substring(2), kv.Value);
                    }
                    else if (kv.Key == "k" && _session.IsHost)
                    {
                        pass = false; // karma is the host's
                    }
                    else if (_session.IsHost && kv.Key.StartsWith("n:")
                        && !DeathOk(from, kv.Key.Substring(2), kv.Value))
                    {
                        pass = false;
                    }
                    else
                    {
                        Apply(kv.Key, kv.Value);
                        if (_baseline != null)
                        {
                            if (kv.Value == Removed && IsRemovable(kv.Key))
                            {
                                _baseline.Remove(kv.Key);
                            }
                            else if (IsRemovable(kv.Key))
                            {
                                // A journal entry: what our journal has now,
                                // which can differ from theirs (one we had
                                // already, made by our own game). Taking theirs
                                // as the baseline would send ours back at every
                                // look, and theirs again, for good. One we
                                // didn't take isn't ours to send.
                                string own = OwnJournal(kv.Key);
                                if (own != null)
                                {
                                    _baseline[kv.Key] = own;
                                }
                                else
                                {
                                    _baseline.Remove(kv.Key);
                                }
                            }
                            else
                            {
                                // known now; not ours to send back
                                _baseline[kv.Key] = kv.Value;
                            }
                        }
                    }
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
                    if (e.Key[0] == 'r' || e.Value == Removed)
                    {
                        return e.Key + " " + e.Value;
                    }

                    return e.Key;
                });

                Plugin.Log.LogInfo(
                    $"[world] got {got.Count} story changes: "
                        + $"{string.Join(", ", changes)}");
            }

            if (_session.IsHost && relay.Count > 0)
            {
                Send(relay, except: from);
            }
        }

        /// <summary>
        /// Host: NPCs whose death a joiner's NpcDied showed us just now
        /// (WorldActions), and when.
        /// </summary>
        private readonly Dictionary<string, float> _deathsAllowed =
            new Dictionary<string, float>();

        /// <summary>
        /// Host: a joiner killed this NPC and we checked it (their NpcDied
        /// request was allowed).
        /// </summary>
        internal void DeathAllowed(string npc)
        {
            if (!string.IsNullOrEmpty(npc))
            {
                _deathsAllowed[npc] = UnityEngine.Time.unscaledTime;
            }
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
            bool dead;
            try
            {
                bool? saved = JObject.Parse(value).Value<bool?>("dead");
                dead = false;
                if (saved.HasValue)
                {
                    dead = saved.Value;
                }
            }
            catch
            {
                return true; // not ours to judge: Apply reports it
            }

            if (st != null && st.dead)
            {
                if (dead)
                {
                    return true;
                }

                SendNpcState(from, npc, st);
                return false;
            }

            if (!dead)
            {
                return true;
            }

            if (_deathsAllowed.TryGetValue(npc, out float t)
                && UnityEngine.Time.unscaledTime - t < 30f)
            {
                return true;
            }

            Plugin.Log.LogInfo(
                $"[authority] refused {_session.NameOf(from)}'s state of "
                    + $"{npc}: dead, but no kill of theirs was allowed here");
            SendNpcState(from, npc, st);
            return false;
        }

        private void SendNpcState(ulong to, string npc, Flags.NPCState st)
        {
            if (st == null || st.name == null)
            {
                return;
            }

            var w = _session.Begin(MsgType.FlagsDelta);
            w.W.Write((ushort)1);
            w.WriteStr("n:" + npc);
            w.WriteStr(NpcJson(st));
            _session.SendTo(to, w, Channel.Control, reliable: true);
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
            if (key == "k")
            {
                f.karmaPoints = int.Parse(value);
                return;
            }

            if (key[0] == 'j')
            {
                ApplyJournal(key[1], key.Substring(3), value);
                return;
            }

            string name = key.Substring(2);
            switch (key[0])
            {
                case 'f':
                    if (PlayersOwn(name))
                    {
                        break;
                    }

                    var parts = value.Split('|');
                    if (name.StartsWith(ModPrefix))
                    {
                        SetModFlag(
                            f, name, bool.Parse(parts[0]), int.Parse(parts[1]));
                        break;
                    }

                    f.setFlag(name, bool.Parse(parts[0]));
                    f.setFlag(name, int.Parse(parts[1]));
                    break;
                case 'n':
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
                case 'd':
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
        /// Set one of the mod's own flags (ModPrefix) in Flags.flagsDict, which
        /// the game's setFlag won't take.
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

            if (value == Removed)
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

            if (kind != 'l' && kind != 'd' && DreamEntry(value) && !SharedDream)
            {
                // Found in a shared dream that has ended here (or not begun):
                // the game would never drop it.
                Plugin.Log.LogInfo(
                    $"Journal: another player's {name} from a dream we aren't "
                        + "in: not taken");
                return;
            }

            string popup = null;
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
                    popup = "JournalEntry";
                    break;
                case 'n':
                    if (j.notesDict.ContainsKey(name))
                    {
                        return;
                    }

                    j.notesDict[name] =
                        JsonConvert.DeserializeObject<Journal.Note>(value);
                    popup = "Note";
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
                    if (!QuietPlace(name))
                    {
                        popup = "Location";
                    }

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
                default:
                    return;
            }

            string what = kind switch
            {
                'e' => "entry",
                'n' => "note",
                'k' => "key",
                'i' => "item",
                'l' => "place",
                _ => "direction",
            };

            Plugin.Log.LogInfo(
                "Journal: another player's " + $"{what} {name}");
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
            bool byId = deadId > 0 && deadId < SharedIds.SendLimit;
            foreach (var npc in UnityEngine.Object.FindObjectsOfType<NPC>())
            {
                if (npc.name != name)
                {
                    continue;
                }

                if (byId)
                {
                    var sv = npc.GetComponent<SaveableObject>();
                    if (sv == null || sv.uniqueId != deadId)
                    {
                        continue;
                    }
                }

                var c = npc.GetComponent<Character>();
                if (c == null || !c.alive)
                {
                    continue;
                }

                // A copy of one the host runs as an enemy dies with the host's
                // state (Combat.NetEnemies).
                if (Combat.NetEnemies.Instance != null
                    && Combat.NetEnemies.Instance.IsPuppet(c, out _))
                {
                    continue;
                }

                Plugin.Log.LogInfo(
                    $"{name} was killed in another player's game");
                WorldActions.Replaying = true;
                try
                {
                    c.die();
                }
                finally
                {
                    WorldActions.Replaying = false;
                }
            }
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
                switch (_type)
                {
                    case "Location":
                    case "LocationAlreadyFound":
                        has = Has(_desc, "UI");
                        break;
                    case "JournalEntry":
                        has = Has(_desc + "_name", "JournalEntries");
                        break;
                    case "Note":
                        has = Has(_desc + "_name", "Notes")
                            || Has(_desc + "_name", "Items");
                        break;
                    case "Key":
                    case "InvItem":
                        has = Has(_desc + "_name", "Items");
                        break;
                    default:
                        // reputation and others carry their own text
                        return true;
                }
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
            if (_this == null || FlagsSync.Instance == null)
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

            if (st != null && st.reputation != __state)
            {
                FlagsSync.Instance.Absorb(ev.Value, st.reputation - __state);
            }
        }
    }
}
