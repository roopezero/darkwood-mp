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
    /// Each player keeps their own character in the host's world, as goasis's servers keep one character for each
    /// player key. A character is what the game saves of its player: Player.SaveState (where they are, health,
    /// stamina, experience and level, upgrades, skills, recipes, effects, lives) and the player's inventory and
    /// hotbar (Inventory.SaveState each), as SaveManager.loadPlayer reads them.
    ///
    /// The host keeps the characters of the players who joined its world, by key, and writes them whenever its
    /// game saves, beside its save: prof&lt;id&gt;/darkwoodmp_players/&lt;hash of key&gt;.json. A client asks for its
    /// character once it has loaded the world (CharacterRequest) and gets it (CharacterLoad); someone new gets a
    /// fresh one (no items, full health, beside the host), or a copy of the host's if the host's NewCharacters
    /// setting says so. The client sends its character back every CharacterInterval seconds, as it leaves, and as
    /// the game quits (CharacterSave). The essence and the number of levels are shared (World.Progression); the
    /// level a character has taken and its skills are its own.
    ///
    /// A character carries over into the host's next chapter as the host's own does (its level, skills and items;
    /// the chapter's ChapterPreset.initInventory and initPlayer are only for a chapter started without one): each
    /// record says which chapter it was sent in, and one from an earlier chapter starts beside the host (World.Chapters).
    /// </summary>
    public sealed class Characters
    {
        public const float CharacterInterval = 15f;

        public sealed class Record
        {
            public string Name;
            public string Player;  // Player.SaveState
            public string Inv;     // Inventory.SaveState of the player's inventory
            public string Hotbar;  // Inventory.SaveState of the hotbar
            public long Saved;     // unix time
            public int Chapter;    // the host's chapter when it was sent (0: not known, from before it was kept)
            public bool Dead;      // died for good (CharacterDead): it comes back only to watch the others
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
            session.OnSessionEnded += () => { _asked = false; _have = false; _carried.Clear(); };
            session.OnPeerDisconnected += peer => _carried.Remove(peer);
        }

        // =====================================================================
        // Host
        // =====================================================================

        private readonly Dictionary<string, Record> _records = new Dictionary<string, Record>();
        private string _recordsDir; // which world _records belong to

        private static string KeyHash(string key)
        {
            using (var sha = SHA256.Create())
                return WorldFiles.Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(key ?? ""))).Substring(0, 32);
        }

        private string RecordsDir()
        {
            var sm = Singleton<SaveManager>.Instance;
            if (sm == null || Core.currentProfile == null) return null;
            return Path.Combine(Path.Combine(sm.baseSaveDirectory, "prof" + Core.currentProfile.id), "darkwoodmp_players");
        }

        /// <summary>
        /// A new world in a save folder that had one (a new game in the slot, a deleted slot, a reset test world):
        /// what was read from that folder is dropped, so it's read again. Records() only notices a different folder,
        /// and a new world in the same one used to get the old world's characters, a character that died for good too.
        /// </summary>
        public void Forget(string why)
        {
            if (_recordsDir == null && _records.Count == 0) return;
            if (_records.Count > 0) Plugin.Log.LogInfo($"Players' characters of the last world in this folder forgotten ({why})");
            _records.Clear();
            _recordsDir = null;
        }

        /// <summary>The characters of this world: loaded from beside its save the first time they're needed.</summary>
        private Dictionary<string, Record> Records()
        {
            string dir = RecordsDir();
            if (dir == null || dir == _recordsDir) return _records;
            _records.Clear();
            _recordsDir = dir;
            try
            {
                if (Directory.Exists(dir))
                    foreach (var f in Directory.GetFiles(dir, "*.json"))
                    {
                        var rec = JsonConvert.DeserializeObject<Record>(File.ReadAllText(f));
                        if (rec != null) _records[Path.GetFileNameWithoutExtension(f)] = rec;
                    }
                if (_records.Count > 0) Plugin.Log.LogInfo($"This world keeps {_records.Count} other players' characters");
            }
            catch (Exception e) { Plugin.Log.LogWarning($"Couldn't read players' characters in {dir}: {e.Message}"); }
            return _records;
        }

        /// <summary>Host: the game saved; write the characters beside it.</summary>
        public void HostSaved()
        {
            if (WorldShare.Snapshotting || NetEnemies.ClientMode) return; // a snapshot for a joiner, or a client's copy
            var recs = Records();
            if (recs.Count == 0 || _recordsDir == null) return;
            try
            {
                Directory.CreateDirectory(_recordsDir);
                foreach (var kv in recs) File.WriteAllText(Path.Combine(_recordsDir, kv.Key + ".json"), JsonConvert.SerializeObject(kv.Value));
            }
            catch (Exception e) { Plugin.Log.LogWarning($"Couldn't write players' characters: {e.Message}"); }
        }

        private void OnRequest(ulong from, PacketReader r)
        {
            if (!_session.IsHost || !_session.Keys.TryGetValue(from, out var key)) return;
            Records().TryGetValue(KeyHash(key), out var rec);
            var host = Player.Instance;
            var w = _session.Begin(MsgType.CharacterLoad);
            bool copy = rec == null && Plugin.Cfg.NewCharacters.Value == "copy";
            // A character from an earlier chapter keeps what it is and carries, as the game carries the host's own
            // over (its chapter save), but where it stood was on the last chapter's map: it starts beside the host.
            int chapter = Chapters.HostChapter;
            bool earlier = rec != null && rec.Chapter != 0 && chapter != 0 && rec.Chapter != chapter;
            bool dead = rec != null && rec.Dead;
            // 1: yours, 2: a copy of the host's (keep what you loaded), 0: new, 3: yours, beside the host, 4: yours, dead for good
            w.W.Write((byte)(dead ? 4 : earlier ? 3 : rec != null ? 1 : copy ? 2 : 0));
            w.WriteBigStr(rec?.Player ?? ""); w.WriteBigStr(rec?.Inv ?? ""); w.WriteBigStr(rec?.Hotbar ?? "");
            // Where the host stands: the joiner's game finds a free spot beside it (Sync.SpawnSpot), off the host.
            w.Write(host != null ? host.transform.position : Vector3.zero);
            _session.SendTo(from, w, Channel.Control, reliable: true);
            Progression.HostSendTo(from); // after the character: the shared essence and level (World.Progression)
            DragSync.Instance?.JoinerInWorld(from); // loose things that moved since the snapshot it loaded
            if (dead) { Plugin.Log.LogInfo($"{_session.NameOf(from)}'s character died for good in this world: they watch the others"); return; }
            Plugin.Log.LogInfo($"{_session.NameOf(from)}'s character: " + (rec != null ? $"theirs, from {DateTimeOffset.FromUnixTimeSeconds(rec.Saved).LocalDateTime}" + (earlier ? $" in chapter {rec.Chapter}: beside you" : "") : copy ? "a copy of yours" : "a new one"));
        }

        private void OnSave(ulong from, PacketReader r)
        {
            if (!_session.IsHost || !_session.Keys.TryGetValue(from, out var key)) return;
            var rec = new Record { Name = _session.NameOf(from), Player = r.ReadBigStr(), Inv = r.ReadBigStr(), Hotbar = r.ReadBigStr(), Saved = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), Chapter = Chapters.HostChapter };
            if (rec.Player.Length == 0 || rec.Inv.Length == 0) return;
            _carried[from] = new Carried { Inv = rec.Inv, Hotbar = rec.Hotbar };
            // Once the host's next chapter is being built, a joiner's character still stands in the last one; the one
            // it sent at ChapterStart is the one kept.
            if (Chapters.Generating) return;
            // A character that died for good stays dead, whatever its game sends after (it goes on sending while it watches).
            if (Records().TryGetValue(KeyHash(key), out var was) && was.Dead) rec.Dead = true;
            Records()[KeyHash(key)] = rec; // written with the world, next time the host's game saves
        }

        /// <summary>What a client's player carried, by the character it sent last this session, and what it placed since.</summary>
        private sealed class Carried { public string Inv, Hotbar; public int Placed; }
        private readonly Dictionary<ulong, Carried> _carried = new Dictionary<ulong, Carried>();

        /// <summary>
        /// Host: how many items of these types this client's player carries (bags and hotbar), by the character it
        /// last sent this session, less the things it has placed since; null: not known (nothing sent yet).
        /// A client sends its character as its player begins placing something (PlacingPatch), so this is current then.
        /// </summary>
        public int? Carries(ulong peer, ICollection<string> types)
        {
            if (!_session.IsHost || types == null || !_carried.TryGetValue(peer, out var c)) return null;
            try { return Count(c.Inv, types) + Count(c.Hotbar, types) - c.Placed; }
            catch (Exception e) { Plugin.Log.LogWarning($"Couldn't read what {_session.NameOf(peer)} carries: {e.Message}"); return null; }
        }

        /// <summary>Host: this client's player has placed one more thing since its last character.</summary>
        public void Placed(ulong peer) { if (_carried.TryGetValue(peer, out var c)) c.Placed++; }

        private static int Count(string inv, ICollection<string> types)
        {
            if (string.IsNullOrEmpty(inv)) return 0;
            var state = JsonConvert.DeserializeObject<Inventory.SaveState>(inv);
            int n = 0;
            if (state?.invItems != null)
                foreach (var it in state.invItems)
                    if (it != null && it.type != null && types.Contains(it.type)) n += Math.Max(1, it.amount);
            return n;
        }

        /// <summary>Host: a joiner's character died for good. Kept with it, so it doesn't come back alive when they rejoin.</summary>
        private void OnDead(ulong from, PacketReader r)
        {
            if (!_session.IsHost || !_session.Keys.TryGetValue(from, out var key)) return;
            var recs = Records();
            if (!recs.TryGetValue(KeyHash(key), out var rec)) recs[KeyHash(key)] = rec = new Record { Name = _session.NameOf(from), Saved = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), Chapter = Chapters.HostChapter };
            rec.Dead = true;
            Plugin.Log.LogInfo($"{_session.NameOf(from)}'s character died for good: kept dead in this world");
        }

        /// <summary>Client: tell the host our character died for good (World.Spectator).</summary>
        public void SendDead()
        {
            if (!NetEnemies.ClientMode || !_session.Connected) return;
            _session.SendToHost(_session.Begin(MsgType.CharacterDead), Channel.Control, reliable: true);
        }

        /// <summary>
        /// Host: whether this player's character has the electrician skill (its generators use half the fuel), from
        /// the character it last sent; null: not known yet.
        /// </summary>
        public bool? Electrician(ulong peer)
        {
            if (!_session.IsHost || !_session.Keys.TryGetValue(peer, out var key)) return null;
            if (!Records().TryGetValue(KeyHash(key), out var rec) || string.IsNullOrEmpty(rec.Player)) return null;
            if (_electrician.TryGetValue(peer, out var known) && ReferenceEquals(known.Item1, rec.Player)) return known.Item2;
            bool? has = null;
            try
            {
                var skills = JsonConvert.DeserializeObject<Player.SaveState>(rec.Player)?.skillS?.skillsStates;
                if (skills != null) has = skills.Exists(k => k != null && k.type == "electrician");
            }
            catch (Exception e) { Plugin.Log.LogWarning($"Couldn't read {rec.Name}'s skills: {e.Message}"); }
            _electrician[peer] = (rec.Player, has);
            return has;
        }
        private readonly Dictionary<ulong, (string, bool?)> _electrician = new Dictionary<ulong, (string, bool?)>();

        // =====================================================================
        // Client
        // =====================================================================

        private bool _asked, _have;
        private float _nextSave, _readyAt = -1f;

        /// <summary>How long a joiner's game waits for its character before it shows its player anyway.</summary>
        private const float PlaceWait = 6f;

        /// <summary>
        /// Client: in the host's world, but our character isn't placed yet. Our player still stands where the host's
        /// save had the host's player, which is often where the host is now, so PlayerSync doesn't send where we
        /// are until then (while the world loads, and at most PlaceWait seconds after): the others first see us where we're put.
        /// </summary>
        public bool Placing => NetEnemies.ClientMode && !_have && WorldShare.InWorld
                               && (_readyAt < 0f || Time.unscaledTime - _readyAt < PlaceWait);

        public void Update()
        {
            if (!NetEnemies.ClientMode || !WorldShare.InWorld) { _readyAt = -1f; return; }
            if (!WorldShare.WorldReady) return;
            if (_readyAt < 0f) _readyAt = Time.unscaledTime; // the wait starts once the world has loaded
            if (!_asked)
            {
                _asked = true;
                _session.SendToHost(_session.Begin(MsgType.CharacterRequest), Channel.Control, reliable: true);
                return;
            }
            if (_have && Time.unscaledTime >= _nextSave) SaveNow();
        }

        /// <summary>Client: send our character to the host now (also as we leave, and as the game quits).</summary>
        public void SaveNow()
        {
            if (!_have || !NetEnemies.ClientMode || !WorldShare.InWorld) return;
            _nextSave = Time.unscaledTime + CharacterInterval;
            var p = Player.Instance;
            try
            {
                var w = _session.Begin(MsgType.CharacterSave);
                w.WriteBigStr(JsonConvert.SerializeObject(new Player.SaveState(p)));
                w.WriteBigStr(JsonConvert.SerializeObject(new Inventory.SaveState(p.Inventory)));
                w.WriteBigStr(JsonConvert.SerializeObject(new Inventory.SaveState(p.Hotbar)));
                _session.SendToHost(w, Channel.Control, reliable: true);
            }
            catch (Exception e) { Plugin.Log.LogWarning($"Couldn't send your character: {e.Message}"); }
        }

        private void OnLoad(ulong from, PacketReader r)
        {
            if (from != _session.HostId || !NetEnemies.ClientMode) return;
            byte kind = r.R.ReadByte();
            string ps = r.ReadBigStr(), inv = r.ReadBigStr(), hot = r.ReadBigStr();
            Vector3 spawn = r.ReadVector3();
            var p = Player.Instance;
            if (p == null) return;
            if (kind == 4)
            {
                // Our character died for good here: as the game doesn't bring it back, we watch the others again.
                _have = true;
                _nextSave = Time.unscaledTime + CharacterInterval;
                Plugin.Log.LogInfo("Your character in this world died for good: you watch the others");
                Spectator.BeginDead();
                return;
            }
            try
            {
                if (kind == 1 || kind == 3) ApplyRecord(p, ps, inv, hot);
                if (kind == 3 && spawn != Vector3.zero) p.transform.position = spawn; // the last chapter's place isn't on this map
                else if (kind == 0) Fresh(p, spawn);
                // A copy of the host's: keep it, beside them. Our own (kind 1) stays where it was saved: this used
                // to apply to it too, which put a returning player on the host (or at 0,0 with no position given).
                else if (kind == 2 && spawn != Vector3.zero) p.transform.position = spawn;
                PlaceFree(p, spawn == Vector3.zero ? (Vector3?)null : spawn);
                p.refreshRecipes();
                Reequip(p);
                Progression.AfterCharacter(p);
                Plugin.Log.LogInfo(kind == 1 ? "Your character in this world is back" : kind == 3 ? "Your character from the last chapter is back, beside the host" : kind == 0 ? "A new character for you in this world" : "You start as a copy of the host's character");
            }
            catch (Exception e) { Plugin.Log.LogError($"Couldn't load your character: {e}"); }
            _have = true;
            _nextSave = Time.unscaledTime + CharacterInterval;
        }

        /// <summary>
        /// Move our player to a free spot near where it was just put (Sync.SpawnSpot), so it doesn't appear on top
        /// of another player or in a wall. host: where the host stands, kept clear too (we may not have heard where
        /// it is yet).
        /// </summary>
        private static void PlaceFree(Player p, Vector3? host)
        {
            var want = p.transform.position;
            var at = Sync.SpawnSpot.Find(p, want, host.HasValue ? new[] { host.Value } : null);
            if ((at - want).sqrMagnitude < 1f) { Plugin.Log.LogInfo($"[spawn] placed at {want}: free"); return; }
            p.transform.position = at;
            var body = p.GetComponent<Rigidbody>();
            if (body != null) { body.position = at; body.velocity = Vector3.zero; }
            Plugin.Log.LogInfo($"[spawn] placed at {at}, {new Vector2(at.x - want.x, at.z - want.z).magnitude:0} from {want}, which wasn't free");
        }

        private static void ApplyRecord(Player p, string ps, string inv, string hot)
        {
            var state = JsonConvert.DeserializeObject<Player.SaveState>(ps);
            // loadValues applies upgrades by repeating them, as for a player just loaded with none; ours already has
            // the host's (we loaded the host's save), so apply only the difference, and take back any extra.
            state.healthUpgrades = Delta(state.healthUpgrades, p.healthUpgrades, () => { p.healthUpgrades--; p.maxHealth -= 25f; });
            state.staminaUpgrades = Delta(state.staminaUpgrades, p.staminaUpgrades, () => { p.staminaUpgrades--; p.maxStamina -= 25f; });
            int hotbarUpgrades = state.hotbarUpgrades, inventoryUpgrades = state.inventoryUpgrades;
            state.hotbarUpgrades = 0;    // these only add slots; the inventory and hotbar below come with their own
            state.inventoryUpgrades = 0; // slot counts (loadValues adds or removes slots to match), fewer included
            state.loadValues(onlyState: false); // position too
            JsonConvert.DeserializeObject<Inventory.SaveState>(inv).loadValues(p.Inventory);
            JsonConvert.DeserializeObject<Inventory.SaveState>(hot).loadValues(p.Hotbar);
            p.hotbarUpgrades = hotbarUpgrades;
            p.inventoryUpgrades = inventoryUpgrades;
            p.setHealth(Mathf.Min(state.health, p.maxHealth));
        }

        private static int Delta(int wanted, int have, Action takeOne)
        {
            for (int i = wanted; i < have; i++) takeOne();
            return Math.Max(0, wanted - have);
        }

        /// <summary>
        /// Put the item in the selected hotbar slot in the player's hands. Inventory.SaveState.loadValues makes new
        /// item objects for the hotbar, but Player.currentItem still held the one from the host's save, which is in
        /// no slot now, so attacking with it did nothing until the item was put away and taken out again. This is
        /// what the game does after changing the hotbar itself (special_addFlamethrower): select the current
        /// hotbar slot, which goes through Player.switchToItem (put away what's held, take out the slot's item,
        /// its animations and aiming reset), without dropping anything.
        /// </summary>
        private static void Reequip(Player p)
        {
            if (p.Hotbar == null) return;
            var slot = p.getCurrentlySelectedHotbarSlot();
            InvItemClass item = slot != null ? slot.invItem : null;
            try
            {
                if (slot != null) p.selectCurrentHotbarSlot();
                else p.switchToItem(p.fists); // no slot selected: bare hands rather than an item that isn't there
                // An item that can't be held (food in the selected slot) leaves the hands as they were, with no
                // switch under way; if what's held then is the stale item, it becomes bare hands at once.
                if (!p.switchingItem && !InvItemClass.isNull(p.currentItem) && p.currentItem != p.fists && !InBags(p, p.currentItem))
                {
                    p.currentItem = p.fists;
                    p.switchAniLibrary(p.fists.baseClass.aniLibrary);
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning($"Couldn't re-equip your item: {e.Message}"); return; }
            Plugin.Log.LogInfo($"[chars] re-equipped {(InvItemClass.isNull(item) ? "none" : item.type)}");
        }

        private static bool InBags(Player p, InvItemClass item)
        {
            foreach (var bag in new[] { p.Inventory, p.Hotbar })
                if (bag != null && bag.slots != null)
                    foreach (var slot in bag.slots) if (slot != null && slot.invItem == item) return true;
            return false;
        }

        /// <summary>Someone new: the host's save gave them the host's character; empty its bags, heal it, set it beside the host.</summary>
        private static void Fresh(Player p, Vector3 spawn)
        {
            foreach (var bag in new[] { p.Inventory, p.Hotbar })
            {
                var empty = new Inventory.SaveState(bag);
                empty.invItems.Clear(); // keep the slots, not what's in them
                empty.loadValues(bag);
            }
            p.setHealth(p.maxHealth);
            // The host's skills are the host's: a new player takes the shared levels at a stove and picks their own.
            Progression.ClearSkills(p);
            if (spawn != Vector3.zero) p.transform.position = spawn;
        }
    }

    [HarmonyPatch(typeof(SaveManager), nameof(SaveManager.Save))]
    static class HostSavedPatch
    {
        static void Postfix() => Characters.Instance?.HostSaved();
    }
}
