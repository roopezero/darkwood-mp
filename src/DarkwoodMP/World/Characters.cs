using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using DarkwoodMP.Combat;
using DarkwoodMP.Net;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;

namespace DarkwoodMP.World
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
    /// CharacterInterval seconds, as it leaves, and as the game quits
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
    public sealed class Characters
    {
        public const float CharacterInterval = 15f;

        public sealed class Record
        {
            public string Name;
            // Player.SaveState
            public string Player;
            // Inventory.SaveState of the player's inventory
            public string Inv;
            // Inventory.SaveState of the hotbar
            public string Hotbar;
            // unix time
            public long Saved;
            // the chapter the joiner's game was in when it was sent (0: not
            // known, from before it was kept) died for good (CharacterDead): it
            // comes back only to watch the others
            public int Chapter;
            public bool Dead;
        }

        public static Characters Instance { get; private set; }
        private readonly Session _session;

        public Characters(Session session)
        {
            Instance = this;
            _session = session;
            session.Register(MsgType.CharacterRequest, OnRequest);
            session.Register(MsgType.CharacterSave, OnSave);
            session.Register(MsgType.CharacterLoad, OnLoad);
            session.Register(MsgType.CharacterDead, OnDead);
            session.OnSessionEnded += () =>
            {
                _asked = false;
                _have = false;
                _quitSent = false;
                _carried.Clear();
            };
            session.OnPeerDisconnected += peer => _carried.Remove(peer);
        }

        // =====================================================================
        // Host
        // =====================================================================

        private readonly Dictionary<string, Record> _records =
            new Dictionary<string, Record>();
        private string _recordsDir; // which world _records belong to

        internal static string KeyHash(string key)
        {
            using (var sha = SHA256.Create())
            {
                string hashed = key;
                if (hashed == null)
                {
                    hashed = "";
                }
                return WorldFiles.Hex(
                    sha.ComputeHash(Encoding.UTF8.GetBytes(hashed))).Substring(
                        0, 32);
            }
        }

        private string RecordsDir()
        {
            var sm = Singleton<SaveManager>.Instance;
            if (sm == null || Core.currentProfile == null)
            {
                return null;
            }
            return Path.Combine(
                Path.Combine(
                    sm.baseSaveDirectory, "prof" + Core.currentProfile.id),
                "darkwoodmp_players");
        }

        /// <summary>
        /// A new world in a save folder that had one (a new game in the slot, a
        /// deleted slot, a reset test world):
        /// what was read from that folder is dropped, so it's read again.
        /// Records() only notices a different folder, so without this a new
        /// world in the same one would get the old world's characters, one that
        /// died for good too.
        /// </summary>
        public void Forget(string why)
        {
            if (_recordsDir == null && _records.Count == 0)
            {
                return;
            }
            if (_records.Count > 0)
            {
                Plugin.Log.LogInfo(
                    $"Players' characters of the last world in this folder forgotten ({why})");
            }
            _records.Clear();
            _recordsDir = null;
        }

        /// <summary>
        /// The characters of this world: loaded from beside its save the first
        /// time they're needed.
        /// </summary>
        private Dictionary<string, Record> Records()
        {
            string dir = RecordsDir();
            if (dir == null || dir == _recordsDir)
            {
                return _records;
            }
            _records.Clear();
            _recordsDir = dir;
            try
            {
                if (Directory.Exists(dir))
                {
                    foreach (var f in Directory.GetFiles(dir, "*.json"))
                    {
                        var rec = JsonConvert.DeserializeObject<Record>(
                            File.ReadAllText(f));
                        if (rec != null)
                        {
                            _records[Path.GetFileNameWithoutExtension(f)] = rec;
                        }
                    }
                }
                if (_records.Count > 0)
                {
                    Plugin.Log.LogInfo(
                        $"This world keeps {_records.Count} other players' characters");
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning(
                    $"Couldn't read players' characters in {dir}: {e.Message}");
            }
            return _records;
        }

        /// <summary>
        /// Host: the game saved; write the characters beside it.
        /// </summary>
        public void HostSaved()
        {
            if (WorldShare.Snapshotting || NetEnemies.ClientMode)
            {
                return; // a snapshot for a joiner, or a client's copy
            }
            var recs = Records();
            if (recs.Count == 0 || _recordsDir == null)
            {
                return;
            }
            try
            {
                Directory.CreateDirectory(_recordsDir);
                foreach (var kv in recs)
                {
                    File.WriteAllText(
                        Path.Combine(_recordsDir, kv.Key + ".json"),
                        JsonConvert.SerializeObject(kv.Value));
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning(
                    $"Couldn't write players' characters: {e.Message}");
            }
        }

        private void OnRequest(ulong from, PacketReader r)
        {
            if (!_session.IsHost
                || !_session.Keys.TryGetValue(from, out var key))
            {
                return;
            }
            Records().TryGetValue(KeyHash(key), out var rec);
            var host = Player.Instance;
            var w = _session.Begin(MsgType.CharacterLoad);
            bool copy = rec == null && Plugin.Cfg.NewCharacters.Value == "copy";
            // A character from an earlier chapter keeps what it is and carries,
            // as the game carries the host's own over (its chapter save), but
            // where it stood was on the last chapter's map: it starts beside
            // the host.
            int chapter = Chapters.HostChapter;
            bool earlier = rec != null && rec.Chapter != 0 && chapter != 0
                && rec.Chapter != chapter;
            bool dead = rec != null && rec.Dead;
            // 1: yours, 2: a copy of the host's (keep what you loaded), 0: new,
            // 3: yours, beside the host, 4: yours, dead for good
            int answer = 0;
            if (dead)
            {
                answer = 4;
            }
            else if (earlier)
            {
                answer = 3;
            }
            else if (rec != null)
            {
                answer = 1;
            }
            else if (copy)
            {
                answer = 2;
            }
            w.W.Write((byte)answer);
            string player = null;
            if (rec != null)
            {
                player = rec.Player;
            }
            if (player == null)
            {
                player = "";
            }
            w.WriteBigStr(player);
            string inv = null;
            if (rec != null)
            {
                inv = rec.Inv;
            }
            if (inv == null)
            {
                inv = "";
            }
            w.WriteBigStr(inv);
            string hotbar = null;
            if (rec != null)
            {
                hotbar = rec.Hotbar;
            }
            if (hotbar == null)
            {
                hotbar = "";
            }
            w.WriteBigStr(hotbar);
            // Where the host stands: the joiner's game finds a free spot beside
            // it (Sync.SpawnSpot), off the host.
            Vector3 v;
            if (host != null)
            {
                v = host.transform.position;
            }
            else
            {
                v = Vector3.zero;
            }
            w.Write(v);
            _session.SendTo(from, w, Channel.Control, reliable: true);
            // after the character: the shared essence and level
            // (World.Progression)
            Progression.HostSendTo(from);
            // loose things that moved since the snapshot it loaded
            DragSync instance = DragSync.Instance;
            if (instance != null)
            {
                instance.JoinerInWorld(from);
            }
            // story changes made since the snapshot it loaded
            FlagsSync flags = FlagsSync.Instance;
            if (flags != null)
            {
                flags.JoinerInWorld(from);
            }
            if (dead)
            {
                Plugin.Log.LogInfo(
                    $"{_session.NameOf(from)}'s character died for good in this world: they watch the others");
                return;
            }
            string name = _session.NameOf(from);
            string which;
            if (rec != null)
            {
                which =
                    $"theirs, from {DateTimeOffset.FromUnixTimeSeconds(rec.Saved).LocalDateTime}";
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
            if (!_session.IsHost
                || !_session.Keys.TryGetValue(from, out var key))
            {
                return;
            }
            var rec = new Record
            {
                Name = _session.NameOf(from),
                Player = r.ReadBigStr(),
                Inv = r.ReadBigStr(),
                Hotbar = r.ReadBigStr(),
                Saved = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };
            // The chapter of the world the joiner's game is in: what it sends
            // from the last chapter's map, while it waits for the next one or
            // reloads, is stamped with that chapter, whenever it arrives.
            int chapter = r.R.ReadInt32();
            if (chapter > 0)
            {
                rec.Chapter = chapter;
            }
            else
            {
                rec.Chapter = Chapters.HostChapter;
            }
            if (rec.Player.Length == 0 || rec.Inv.Length == 0)
            {
                return;
            }
            _carried[from] = new Carried { Inv = rec.Inv, Hotbar = rec.Hotbar };
            // A character that died for good stays dead, whatever its game
            // sends after (it goes on sending while it watches).
            var recs = Records();
            string id = KeyHash(key);
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
        /// (PlacingPatch), so this is current then.
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
                return Count(c.Inv, types) + Count(c.Hotbar, types) - c.Placed;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning(
                    $"Couldn't read what {_session.NameOf(peer)} carries: {e.Message}");
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

        private static int Count(string inv, ICollection<string> types)
        {
            if (string.IsNullOrEmpty(inv))
            {
                return 0;
            }
            var state = JsonConvert.DeserializeObject<Inventory.SaveState>(inv);
            int n = 0;
            if (state != null && state.invItems != null)
            {
                foreach (var it in state.invItems)
                {
                    if (it != null && it.type != null
                        && types.Contains(it.type))
                    {
                        n += Math.Max(1, it.amount);
                    }
                }
            }
            return n;
        }

        /// <summary>
        /// Host: a joiner's character died for good. Kept with it, so it
        /// doesn't come back alive when they rejoin.
        /// </summary>
        private void OnDead(ulong from, PacketReader r)
        {
            if (!_session.IsHost
                || !_session.Keys.TryGetValue(from, out var key))
            {
                return;
            }
            var recs = Records();
            string id = KeyHash(key);
            if (!recs.TryGetValue(id, out var rec))
            {
                recs[id] = rec = new Record
                {
                    Name = _session.NameOf(from),
                    Saved = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    Chapter = Chapters.HostChapter
                };
            }
            rec.Dead = true;
            Plugin.Log.LogInfo(
                $"{_session.NameOf(from)}'s character died for good: kept dead in this world");
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
            if (!Records().TryGetValue(KeyHash(key), out var rec)
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
                if (skills != null)
                {
                    has = skills.Exists(
                        k => k != null && k.type == "electrician");
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning(
                    $"Couldn't read {rec.Name}'s skills: {e.Message}");
            }
            _electrician[peer] = (rec.Player, has);
            return has;
        }
        private readonly Dictionary<ulong, (string, bool?)> _electrician =
            new Dictionary<ulong, (string, bool?)>();

        // =====================================================================
        // Client
        // =====================================================================

        private bool _asked, _have;
        private float _nextSave, _readyAt = -1f;

        /// <summary>
        /// How long a joiner's game waits for its character before it shows its
        /// player anyway.
        /// </summary>
        private const float PlaceWait = 6f;

        /// <summary>
        /// Client: in the host's world, but our character isn't placed yet. Our
        /// player still stands where the host's save had the host's player,
        /// which is often where the host is now, so PlayerSync doesn't send
        /// where we are until then (while the world loads, and at most
        /// PlaceWait seconds after): the others first see us where we're put.
        /// </summary>
        public bool Placing => NetEnemies.ClientMode && !_have
            && WorldShare.InWorld
            && (_readyAt < 0f
                || Time.unscaledTime - _readyAt < PlaceWait);

        /// <summary>
        /// Client: our character in the host's world has come (CharacterLoad).
        /// </summary>
        public bool HasCharacter => _have;

        /// <summary>
        /// The loaded world (scene handle) we asked for our character in.
        /// Joining again (Rejoin: a new chapter, the host's world reloaded, the
        /// panel's reload) is welcomed while the old world is still loaded, and
        /// the host's world is loaded only after that, in the same session. The
        /// request used to go from the old world in that last frame (the game's
        /// returnToMainMenu clears forbidInputs, and its world is there until
        /// the next frame); the answer was lost with that world and
        /// nothing asked again, so the player kept the host's character from
        /// the host's save: the host's place, hotbar and bags. Now each loaded
        /// world asks for itself.
        /// </summary>
        private int _askedScene;

        private static int ActiveScene =>
            UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle;

        public void Update()
        {
            if (!NetEnemies.ClientMode || !WorldShare.InWorld)
            {
                _readyAt = -1f;
                return;
            }
            if (_asked && ActiveScene != _askedScene)
            {
                // Another world loaded in this session: what we had or asked
                // for was the last one's.
                _asked = false;
                _have = false;
                _readyAt = -1f;
            }
            if (!WorldShare.WorldReady)
            {
                return;
            }
            if (_readyAt < 0f)
            {
                // the wait starts once the world has loaded
                _readyAt = Time.unscaledTime;
            }
            if (!_asked)
            {
                if (Core.mainMenu)
                {
                    // the menu over a world: we may be leaving it
                    // (returnToMainMenu sets it at once)
                    return;
                }
                _asked = true;
                _askedScene = ActiveScene;
                _session.SendToHost(
                    _session.Begin(MsgType.CharacterRequest), Channel.Control,
                    reliable: true);
                return;
            }
            if (_have && Time.unscaledTime >= _nextSave)
            {
                SaveNow();
            }
        }

        /// <summary>
        /// Client: send our character to the host now (also as we leave, and as
        /// the game quits).
        /// </summary>
        public void SaveNow()
        {
            if (_quitSent || !_have || !NetEnemies.ClientMode
                || !WorldShare.InWorld)
            {
                return;
            }
            if (Prologue.Active)
            {
                // the prologue's character isn't kept: the host keeps ours from
                // its end on
                return;
            }
            _nextSave = Time.unscaledTime + CharacterInterval;
            var p = Player.Instance;
            try
            {
                var w = _session.Begin(MsgType.CharacterSave);
                w.WriteBigStr(
                    JsonConvert.SerializeObject(new Player.SaveState(p)));
                w.WriteBigStr(
                    JsonConvert.SerializeObject(
                        new Inventory.SaveState(p.Inventory)));
                w.WriteBigStr(
                    JsonConvert.SerializeObject(
                        new Inventory.SaveState(p.Hotbar)));
                // the chapter of the world we're in (the host's, until it moves
                // on)
                w.W.Write(Chapters.HostChapter);
                _session.SendToHost(w, Channel.Control, reliable: true);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning(
                    $"Couldn't send your character: {e.Message}");
            }
        }

        // Set once the game quits: sent then, while the world is still whole.
        // Leaving at OnDestroy sends nothing more, as the player may already be
        // torn down by then.
        private bool _quitSent;

        /// <summary>
        /// Client, OnApplicationQuit: send our character now, and only now.
        /// </summary>
        public void SaveForQuit()
        {
            SaveNow();
            _quitSent = true;
        }

        private void OnLoad(ulong from, PacketReader r)
        {
            if (from != _session.HostId || !NetEnemies.ClientMode)
            {
                return;
            }
            if (!_asked || ActiveScene != _askedScene || !WorldShare.InWorld)
            {
                Plugin.Log.LogInfo(
                    "Our character came for a world we've left: asking again "
                        + "in this one");
                _asked = false;
                return;
            }
            byte kind = r.R.ReadByte();
            string ps = r.ReadBigStr(),
                inv = r.ReadBigStr(),
                hot = r.ReadBigStr();
            Vector3 spawn = r.ReadVector3();
            var p = Player.Instance;
            if (p == null)
            {
                return;
            }
            if (kind == 4)
            {
                // Our character died for good here: as the game doesn't bring
                // it back, we watch the others again.
                _have = true;
                _nextSave = Time.unscaledTime + CharacterInterval;
                Plugin.Log.LogInfo(
                    "Your character in this world died for good: you watch the "
                        + "others");
                Spectator.BeginDead();
                return;
            }
            try
            {
                // In the prologue, a new character is what the stage made it
                // (its own items): only put beside the host.
                bool prologue = Prologue.Active && (kind == 0 || kind == 2);
                if (kind == 1 || kind == 3)
                {
                    ApplyRecord(p, ps, inv, hot);
                }
                // Our own kept place, but not on this world's map (a character
                // kept with the wrong chapter, or one that was away in a
                // separate location): beside the host, as from the last
                // chapter.
                bool offMap = !prologue && kind == 1 && spawn != Vector3.zero
                    && !OnMap(p.transform.position);
                if (offMap)
                {
                    Plugin.Log.LogInfo(
                        $"[spawn] your kept place {p.transform.position} isn't on this map: your character is back, beside the host");
                }
                if (prologue)
                {
                    if (spawn != Vector3.zero)
                    {
                        p.transform.position = spawn;
                    }
                }
                else if ((kind == 3 || offMap) && spawn != Vector3.zero)
                {
                    // the last chapter's place isn't on this map
                    p.transform.position = spawn;
                }
                else if (kind == 0)
                {
                    Fresh(p, spawn);
                }
                // A copy of the host's: keep it, beside them. Our own (kind 1)
                // stays where it was saved, not on the host (or at 0,0 when no
                // position was given).
                else if (kind == 2 && spawn != Vector3.zero)
                {
                    p.transform.position = spawn;
                }
                Vector3? host;
                if (spawn == Vector3.zero)
                {
                    host = (Vector3?)null;
                }
                else
                {
                    host = spawn;
                }
                PlaceFree(p, host);
                p.refreshRecipes();
                Reequip(p);
                Progression.AfterCharacter(p);
                string data = "You start as a copy of the host's character";
                if (prologue)
                {
                    data = "A new character for you in the host's prologue";
                }
                else if (kind == 1)
                {
                    data = "Your character in this world is back";
                }
                else if (kind == 3)
                {
                    data =
                        "Your character from the last chapter is back, beside "
                            + "the host";
                }
                else if (kind == 0)
                {
                    data = "A new character for you in this world";
                }
                Plugin.Log.LogInfo(data);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Couldn't load your character: {e}");
            }
            _have = true;
            _nextSave = Time.unscaledTime + CharacterInterval;
        }

        /// <summary>
        /// Whether a place is on the loaded world's map: within 0.6 of a
        /// chunk's size (in x and z) of one of WorldGenerator's chunks, by
        /// their places, so a part of the map that's switched off counts too.
        /// True when that can't be told.
        /// </summary>
        internal static bool OnMap(Vector3 at)
        {
            float nearest = NearestChunk(at);
            WorldGenerator wg = Singleton<WorldGenerator>.Instance;
            float size = 0f;
            if (wg != null)
            {
                size = wg.chunkSize;
            }
            return nearest <= 0.6f * size;
        }

        /// <summary>
        /// How far (the larger of x and z) the nearest of the world's chunks is
        /// from a place; 0 when not known.
        /// </summary>
        internal static float NearestChunk(Vector3 at)
        {
            var wg = Singleton<WorldGenerator>.Instance;
            if (wg == null || wg.worldChunks == null
                || wg.worldChunks.Count == 0 || wg.chunkSize <= 0f)
            {
                return 0f;
            }
            float best = float.MaxValue;
            foreach (var c in wg.worldChunks)
            {
                if (c == null)
                {
                    continue;
                }
                var d = c.transform.position - at;
                best =
                    Mathf.Min(best, Mathf.Max(Mathf.Abs(d.x), Mathf.Abs(d.z)));
            }
            if (best == float.MaxValue)
            {
                return 0f;
            }
            return best;
        }

        /// <summary>
        /// Move our player to a free spot near where it was just put
        /// (Sync.SpawnSpot), so it doesn't appear on top of another player or
        /// in a wall. host: where the host stands, kept clear too (we may not
        /// have heard where it is yet).
        /// </summary>
        internal static void PlaceFree(Player p, Vector3? host)
        {
            var want = p.transform.position;
            Vector3[] taken = null;
            if (host.HasValue)
            {
                taken = new[] { host.Value };
            }
            var at = Sync.SpawnSpot.Find(p, want, taken);
            if ((at - want).sqrMagnitude < 1f)
            {
                Plugin.Log.LogInfo($"[spawn] placed at {want}: free");
                return;
            }
            p.transform.position = at;
            var body = p.GetComponent<Rigidbody>();
            if (body != null)
            {
                body.position = at;
                body.velocity = Vector3.zero;
            }
            Plugin.Log.LogInfo(
                $"[spawn] placed at {at}, {new Vector2(at.x - want.x, at.z - want.z).magnitude:0} from {want}, which wasn't free");
        }

        private static void ApplyRecord(
            Player p, string ps, string inv, string hot)
        {
            var state = JsonConvert.DeserializeObject<Player.SaveState>(ps);
            // loadValues applies upgrades by repeating them, as for a player
            // just loaded with none; ours already has the host's (we loaded the
            // host's save), so apply only the difference, and take back any
            // extra.
            state.healthUpgrades = Delta(state.healthUpgrades, p.healthUpgrades,
                () =>
            {
                p.healthUpgrades--;
                p.maxHealth -= 25f;
            });
            state.staminaUpgrades = Delta(state.staminaUpgrades,
                p.staminaUpgrades, () =>
            {
                p.staminaUpgrades--;
                p.maxStamina -= 25f;
            });
            int hotbarUpgrades = state.hotbarUpgrades,
                inventoryUpgrades = state.inventoryUpgrades;
            // these only add slots; the inventory and hotbar below come with
            // their own slot counts (loadValues adds or removes slots to
            // match), fewer included
            state.hotbarUpgrades = 0;
            state.inventoryUpgrades = 0;
            state.loadValues(onlyState: false); // position too
            var invState =
                JsonConvert.DeserializeObject<Inventory.SaveState>(inv);
            var hotState =
                JsonConvert.DeserializeObject<Inventory.SaveState>(hot);
            SetSlots(p.Inventory, invState.slotCount);
            SetSlots(p.Hotbar, hotState.slotCount);
            invState.loadValues(p.Inventory);
            hotState.loadValues(p.Hotbar);
            p.hotbarUpgrades = hotbarUpgrades;
            p.inventoryUpgrades = inventoryUpgrades;
            p.setHealth(Mathf.Min(state.health, p.maxHealth));
        }

        /// <summary>
        /// Give a bag as many slots as our character's, the game's way:
        /// Inventory.addSlot and removeSlot also make and destroy the slot's
        /// picture when the bag is shown (the hotbar always is).
        /// Inventory.SaveState.loadValues changes the slot list alone, so the
        /// hotbar on screen kept the host's slots (the save we loaded is the
        /// host's): one of ours more had no picture, and one fewer left the
        /// host's last slot showing.
        /// </summary>
        private static void SetSlots(Inventory bag, int count)
        {
            if (bag == null || bag.slots == null || count <= 0)
            {
                return;
            }
            while (bag.slots.Count < count)
            {
                bag.addSlot();
            }
            while (bag.slots.Count > count)
            {
                bag.removeSlot();
            }
        }

        private static int Delta(int wanted, int have, Action takeOne)
        {
            for (int i = wanted; i < have; i++)
            {
                takeOne();
            }
            return Math.Max(0, wanted - have);
        }

        /// <summary>
        /// Put the item in the selected hotbar slot in the player's hands.
        /// Inventory.SaveState.loadValues makes new item objects for the
        /// hotbar, but Player.currentItem still held the one from the host's
        /// save, which is in no slot now, so attacking with it did
        /// nothing until the item was put away and taken out again. This is
        /// what the game does after changing the hotbar itself
        /// (special_addFlamethrower): select the current hotbar slot, which
        /// goes through Player.switchToItem (put away what's held, take out the
        /// slot's item, its animations and aiming reset), without dropping
        /// anything.
        /// </summary>
        private static void Reequip(Player p)
        {
            if (p.Hotbar == null)
            {
                return;
            }
            var slot = p.getCurrentlySelectedHotbarSlot();
            InvItemClass item = null;
            if (slot != null)
            {
                item = slot.invItem;
            }
            try
            {
                if (slot != null)
                {
                    p.selectCurrentHotbarSlot();
                }
                else
                {
                    // no slot selected: bare hands rather than an item that
                    // isn't there
                    p.switchToItem(p.fists);
                }
                // An item that can't be held (food in the selected slot) leaves
                // the hands as they were, with no switch under way; if what's
                // held then is the stale item, it becomes bare hands at once.
                if (!p.switchingItem && !InvItemClass.isNull(p.currentItem)
                    && p.currentItem != p.fists && !InBags(p, p.currentItem))
                {
                    p.currentItem = p.fists;
                    p.switchAniLibrary(p.fists.baseClass.aniLibrary);
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning(
                    $"Couldn't re-equip your item: {e.Message}");
                return;
            }
            string type;
            if (InvItemClass.isNull(item))
            {
                type = "none";
            }
            else
            {
                type = item.type;
            }
            Plugin.Log.LogInfo($"[chars] re-equipped {type}");
        }

        private static bool InBags(Player p, InvItemClass item)
        {
            foreach (var bag in new[] { p.Inventory, p.Hotbar })
            {
                if (bag != null && bag.slots != null)
                {
                    foreach (var slot in bag.slots)
                    {
                        if (slot != null && slot.invItem == item)
                        {
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Someone new: the host's save gave them the host's character; empty
        /// its bags, heal it, set it beside the host.
        /// </summary>
        private static void Fresh(Player p, Vector3 spawn)
        {
            foreach (var bag in new[] { p.Inventory, p.Hotbar })
            {
                var empty = new Inventory.SaveState(bag);
                empty.invItems.Clear(); // keep the slots, not what's in them
                empty.loadValues(bag);
            }
            p.setHealth(p.maxHealth);
            // The host's skills are the host's: a new player takes the shared
            // levels at a stove and picks their own.
            Progression.ClearSkills(p);
            if (spawn != Vector3.zero)
            {
                p.transform.position = spawn;
            }
        }
    }

    [HarmonyPatch(typeof(SaveManager), nameof(SaveManager.Save))]
    static class HostSavedPatch
    {
        static void Postfix()
        {
            Characters instance = Characters.Instance;
            if (instance != null)
            {
                instance.HostSaved();
            }
        }
    }
}
