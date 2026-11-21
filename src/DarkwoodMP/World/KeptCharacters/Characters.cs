using System;
using System.Collections.Generic;
using DarkwoodMP.Combat;
using DarkwoodMP.Net;
using Newtonsoft.Json;
using UnityEngine;
using Chapters = DarkwoodMP.World.Story.Chapters;

namespace DarkwoodMP.World.KeptCharacters
{
    /// <summary>
    /// Each player keeps their own character in the host's world, as goasis's
    /// servers keep one character for each player key. A character is what the
    /// game saves of its player: Player.SaveState (where they are, health,
    /// stamina, experience and level, upgrades, skills, recipes, effects,
    /// lives) and the player's inventory and hotbar (Inventory.SaveState each),
    /// as SaveManager.loadPlayer reads them.
    ///
    /// The host keeps the characters of the players who joined its world, by
    /// key, and writes them whenever its game saves, beside its save:
    /// prof&lt;id&gt;/darkwoodmp_players/&lt;hash of key&gt;.json. A client
    /// asks for its character once it has loaded the world (CharacterRequest)
    /// and gets it (CharacterLoad); someone new gets a fresh one (no items,
    /// full health, beside the host), or a copy of the host's if the host's
    /// NewCharacters setting says so. The client sends its character back every
    /// OurCharacterRules.SaveInterval, as it leaves, and as the game quits
    /// (CharacterSave). The essence and the number of levels are shared
    /// (World.Progression); the level a character has taken and its skills are
    /// its own.
    ///
    /// A character carries over into the host's next chapter as the host's own
    /// does (its level, skills and items;
    /// the chapter's ChapterPreset.initInventory and initPlayer are only for a
    /// chapter started without one): each record says which chapter the
    /// joiner's game was in when it sent it (the joiner says so in
    /// CharacterSave), and one from an earlier chapter starts beside the host
    /// (World.Chapters). The host used to stamp a record with its own chapter,
    /// and a joiner's last save from the old chapter's map (sent as it reloaded
    /// the world) came in after the host was already in the next one: that
    /// player then stood at the old map's place on the new map, in the dark
    /// outside it. A kept place that isn't on the world's map at all starts
    /// beside the host too.
    /// </summary>
    internal sealed partial class Characters
    {
        private readonly Session _session;

        /// The host's kept characters, which live as long as the game
        /// (ModHost.Kept): SaveManager.Save writes them also alone.
        private readonly CharacterRecords _kept;

        public Characters(CoopSession coop)
        {
            _session = coop.Net;
            _kept = Plugin.Instance.Host.Kept;
            coop.Handle(MsgType.CharacterRequest, OnRequest);
            coop.Handle(MsgType.CharacterSave, OnSave);
            coop.Handle(MsgType.CharacterLoad, OnLoad);
            coop.Handle(MsgType.CharacterDead, OnDead);
            _session.OnPeerDisconnected += OnPeerLeft;
            coop.Hook(() => _session.OnPeerDisconnected -= OnPeerLeft);
        }

        private void OnPeerLeft(ulong peer)
        {
            _carried.Remove(peer);
        }

        // =====================================================================
        // Host
        // =====================================================================

        /// A character message from this peer is taken, with its key.
        private bool Takes(ulong from, out string key)
        {
            bool keyed = _session.Keys.TryGetValue(from, out key);
            return CharacterRules.Takes(_session.IsHost, keyed);
        }

        private static KindFacts FactsOf(
            CharacterRecord rec, int hostChapter)
        {
            bool dead = false;
            int chapter = 0;
            if (rec != null)
            {
                dead = rec.Dead;
                chapter = rec.Chapter;
            }

            return new KindFacts
            {
                Kept = rec != null,
                Dead = dead,
                Chapter = chapter,
                HostChapter = hostChapter,
                NewCharacters = Plugin.Cfg.NewCharacters.Value,
            };
        }

        private void OnRequest(ulong from, PacketReader r)
        {
            if (!Takes(from, out var key))
            {
                return;
            }

            _kept.Records().TryGetValue(
                CharacterRecords.KeyHash(key), out var rec);
            var host = Player.Instance;
            var w = _session.Begin(MsgType.CharacterLoad);
            // A character from an earlier chapter keeps what it is and carries,
            // as the game carries the host's own over (its chapter save), but
            // where it stood was on the last chapter's map: it starts beside
            // the host.
            var answer = CharacterRules.KindOf(
                FactsOf(rec, Chapters.HostChapter));
            bool copy = answer == CharacterKind.HostCopy;
            bool earlier = answer == CharacterKind.OwnBesideHost;
            bool dead = answer == CharacterKind.Dead;
            string player = null;
            if (rec != null)
            {
                player = rec.Player;
            }

            if (player == null)
            {
                player = "";
            }

            string inv = null;
            if (rec != null)
            {
                inv = rec.Inv;
            }

            if (inv == null)
            {
                inv = "";
            }

            string hotbar = null;
            if (rec != null)
            {
                hotbar = rec.Hotbar;
            }

            if (hotbar == null)
            {
                hotbar = "";
            }

            // Where the host stands: the joiner's game finds a free spot beside
            // it (Sync.Players.SpawnSpot), off the host.
            Vector3 v;
            if (host != null)
            {
                v = host.transform.position;
            }
            else
            {
                v = Vector3.zero;
            }

            CharacterLoad.Write(
                w,
                new CharacterLoadBody
                {
                    Kind = answer,
                    Player = player,
                    Inventory = inv,
                    Hotbar = hotbar,
                    At = new Vec3(v.x, v.y, v.z),
                });

            _session.SendTo(from, w, Channel.Control, reliable: true);
            // after the character: the shared essence and level
            // (World.Progression)
            Plugin.Instance.Host.Progress.HostSendTo(from);
            // loose things that moved since the snapshot it loaded
            CoopSession coop = Coop.Current;
            if (coop != null)
            {
                coop.Drags.JoinerInWorld(from);
            }

            // story changes made since the snapshot it loaded
            coop = Coop.Current;
            if (coop != null)
            {
                coop.Story.JoinerInWorld(from);
            }

            if (dead)
            {
                Plugin.Log.LogInfo(
                    $"{_session.NameOf(from)}'s character died for good in "
                        + "this world: they watch the others");
                return;
            }

            string name = _session.NameOf(from);
            string which;
            if (rec != null)
            {
                DateTime saved = DateTimeOffset
                    .FromUnixTimeSeconds(rec.Saved).LocalDateTime;
                which = "theirs, from " + $"{saved}";
                if (earlier)
                {
                    which += $" in chapter {rec.Chapter}: beside you";
                }
            }
            else if (copy)
            {
                which = "a copy of yours";
            }
            else
            {
                which = "a new one";
            }

            Plugin.Log.LogInfo($"{name}'s character: " + which);
        }

        private void OnSave(ulong from, PacketReader r)
        {
            if (!Takes(from, out var key))
            {
                return;
            }

            var body = CharacterSave.Read(r);
            var rec = new CharacterRecord
            {
                Name = _session.NameOf(from),
                Player = body.Player,
                Inv = body.Inventory,
                Hotbar = body.Hotbar,
                Saved = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };

            // The chapter of the world the joiner's game is in: what it sends
            // from the last chapter's map, while it waits for the next one or
            // reloads, is stamped with that chapter, whenever it arrives.
            rec.Chapter =
                CharacterRules.ChapterOf(body.Chapter, Chapters.HostChapter);
            if (!CharacterRules.Keeps(rec.Player, rec.Inv))
            {
                return;
            }

            _carried[from] = new Carried { Inv = rec.Inv, Hotbar = rec.Hotbar };
            // A character that died for good stays dead, whatever its game
            // sends after (it goes on sending while it watches).
            var recs = _kept.Records();
            string id = CharacterRecords.KeyHash(key);
            if (recs.TryGetValue(id, out var old) && old.Dead)
            {
                rec.Dead = true;
            }

            // written with the world, next time the host's game saves
            recs[id] = rec;
        }

        /// <summary>
        /// What a client's player carried, by the character it sent last this
        /// session, and what it placed since.
        /// </summary>
        private sealed class Carried
        {
            public string Inv, Hotbar;
            public int Placed;
        }

        private readonly Dictionary<ulong, Carried> _carried =
            new Dictionary<ulong, Carried>();

        /// <summary>
        /// Host: how many items of these types this client's player carries
        /// (bags and hotbar), by the character it last sent this session, less
        /// the things it has placed since; null: not known (nothing sent yet).
        /// A client sends its character as its player begins placing something
        /// (ContainerPatches.ProgressBarCompleted), so this is current then.
        /// </summary>
        public int? Carries(ulong peer, ICollection<string> types)
        {
            if (!_session.IsHost || types == null
                || !_carried.TryGetValue(peer, out var c))
            {
                return null;
            }

            try
            {
                return CharacterRules.Carries(
                    CharacterRules.CountOf(BagOf(c.Inv), types),
                    CharacterRules.CountOf(BagOf(c.Hotbar), types),
                    c.Placed);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning(
                    $"Couldn't read what {_session.NameOf(peer)} carries: "
                        + $"{e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Host: this client's player has placed one more thing since its last
        /// character.
        /// </summary>
        public void Placed(ulong peer)
        {
            if (_carried.TryGetValue(peer, out var c))
            {
                c.Placed++;
            }
        }

        /// What an inventory's JSON holds; null when it's empty or holds
        /// no item list.
        private static List<CarriedItem> BagOf(string inv)
        {
            if (string.IsNullOrEmpty(inv))
            {
                return null;
            }

            var state = JsonConvert.DeserializeObject<Inventory.SaveState>(inv);
            if (state == null || state.invItems == null)
            {
                return null;
            }

            var bag = new List<CarriedItem>();
            foreach (var it in state.invItems)
            {
                if (it != null)
                {
                    bag.Add(
                        new CarriedItem { Type = it.type, Amount = it.amount });
                }
            }

            return bag;
        }

        private void OnDead(ulong from, PacketReader r)
        {
            if (!Takes(from, out var key))
            {
                return;
            }

            var recs = _kept.Records();
            string id = CharacterRecords.KeyHash(key);
            if (!recs.TryGetValue(id, out var rec))
            {
                recs[id] = rec = new CharacterRecord
                {
                    Name = _session.NameOf(from),
                    Saved = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    Chapter = Chapters.HostChapter
                };
            }

            rec.Dead = true;
            Plugin.Log.LogInfo(
                $"{_session.NameOf(from)}'s character died for good: kept dead "
                    + "in this world");
        }

        /// <summary>
        /// Client: tell the host our character died for good (World.Spectator).
        /// </summary>
        public void SendDead()
        {
            if (!NetEnemies.ClientMode || !_session.Connected)
            {
                return;
            }

            _session.SendToHost(
                _session.Begin(MsgType.CharacterDead), Channel.Control,
                reliable: true);
        }

        /// <summary>
        /// Host: whether this player's character has the electrician skill (its
        /// generators use half the fuel), from the character it last sent;
        /// null: not known yet.
        /// </summary>
        public bool? Electrician(ulong peer)
        {
            if (!_session.IsHost
                || !_session.Keys.TryGetValue(peer, out var key))
            {
                return null;
            }

            if (!_kept.Records().TryGetValue(
                    CharacterRecords.KeyHash(key), out var rec)
                || string.IsNullOrEmpty(rec.Player))
            {
                return null;
            }

            if (_electrician.TryGetValue(peer, out var known)
                && ReferenceEquals(known.Item1, rec.Player))
            {
                return known.Item2;
            }

            bool? has = null;
            try
            {
                Player.SaveState saveState =
                    JsonConvert.DeserializeObject<Player.SaveState>(rec.Player);
                List<PlayerSkill.SaveState> skills = null;
                if (saveState != null)
                {
                    PlayerSkills.SaveState skillS = saveState.skillS;
                    if (skillS != null)
                    {
                        skills = skillS.skillsStates;
                    }
                }

                has = CharacterRules.Electrician(TypesOf(skills));
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning(
                    $"Couldn't read {rec.Name}'s skills: {e.Message}");
            }

            _electrician[peer] = (rec.Player, has);
            return has;
        }

        /// The skills' types; a null skill has none. Null for no list.
        private static List<string> TypesOf(List<PlayerSkill.SaveState> skills)
        {
            if (skills == null)
            {
                return null;
            }

            var types = new List<string>();
            foreach (var k in skills)
            {
                if (k == null)
                {
                    types.Add(null);
                }
                else
                {
                    types.Add(k.type);
                }
            }

            return types;
        }

        private readonly Dictionary<ulong, (string, bool?)> _electrician =
            new Dictionary<ulong, (string, bool?)>();
    }
}
