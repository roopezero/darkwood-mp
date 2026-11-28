using System;
using System.Collections.Generic;
using System.Linq;
using DarkwoodMP.Combat;
using DarkwoodMP.Net;
using Newtonsoft.Json;
using UnityEngine;

namespace DarkwoodMP.World.Story
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
    /// JournalPopups). An entry, note, key or item can also go: a
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
    internal sealed partial class FlagsSync
    {
        private readonly CoopSession _coop;

        private Dictionary<string, string> _baseline;

        private int _baselineScene = -1; // the world (scene) the baseline is of

        private Seconds _next;

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
            var now = StoryRead.Snapshot(Net.IsHost);
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

            var changed =
                StoryDiff.Changes(_baseline, now, StoryRead.SharedDream);
            _baseline = now;
            if (changed.Count > 0)
            {
                Send(changed, except: 0);
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

            if (!StoryRules.KeepsDreamEntry(k.inDream, StoryRead.SharedDream))
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
            _atSnapshot = StoryRead.Snapshot(true);
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

            var now = StoryRead.Snapshot(true);
            if (now == null)
            {
                return;
            }

            // as Flush: a dream entry the game dropped as the dream ended
            // isn't sent
            var changed = StoryDiff.SinceSnapshot(
                _atSnapshot, now, StoryRead.SharedDream);
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
            if (key.StartsWith(StoryKeys.Dialogue)
                && !StoryRules.TakesDialogue(
                    Places.Whereabouts.LaterPart() != null))
            {
                // Not taken, so our baseline keeps our own state: taking
                // theirs as the baseline would send ours back at every look,
                // and theirs again, for good.
                Plugin.Log.LogInfo(
                    $"[story] {key}: our player is in a later part of the "
                        + "epilogue, where dialogues are each player's own; "
                        + "not taken");
                return false;
            }

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

            StoryApply.Apply(key, value);
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
                own = StoryRead.OwnJournal(key);
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
    }
}
