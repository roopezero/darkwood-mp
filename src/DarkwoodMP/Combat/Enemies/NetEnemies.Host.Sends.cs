using System.Collections.Generic;
using System.Linq;
using DarkwoodMP.Net;
using DarkwoodMP.Sync;
using DarkwoodMP.World;
using UnityEngine;

namespace DarkwoodMP.Combat
{
    /// <summary>
    /// The host side: the messages sent for tracked enemies. See NetEnemies.cs.
    /// </summary>
    internal sealed partial class NetEnemies
    {
        /// <summary>
        /// Whether a character wears shadow armour now (ShadowArmor.die
        /// destroys the component).
        /// </summary>
        private static bool HasArmor(Character c)
        {
            ShadowArmor a = null;
            if (c != null)
            {
                a = c.GetComponent<ShadowArmor>();
            }

            return a != null && a.enabled;
        }

        /// <summary>
        /// Host: the clients that know this enemy are told when its shadow
        /// armour is put on (CharacterSpawner adds it to a chapter 2+ night
        /// spawn after spawning it) or dies (light, or a hit). Only the host
        /// decides that: a client's copy shows the armour and never checks
        /// light or takes damage (ShadowArmorDamageMe).
        /// </summary>
        private void ArmorSync(Tracked t)
        {
            bool now = HasArmor(t.C);
            if (now == t.Armor)
            {
                return;
            }

            t.Armor = now;
            foreach (var peer in Knowing(t.Id))
            {
                var w = _one.Begin(MsgType.EnemyArmor);
                w.W.Write(t.Id);
                int armorFlag = 0;
                if (now)
                {
                    armorFlag = 1;
                }

                w.W.Write((byte)armorFlag);
                _session.SendTo(peer, w, Channel.Control, reliable: true);
            }

            string text = "gone";
            if (now)
            {
                text = "on";
            }

            Plugin.Log.LogInfo(
                $"[enemy] {t.Id} {t.C.name}: shadow armour {text}");
        }

        private void SendSpawn(ulong peer, Tracked t)
        {
            var c = t.C;
            // first, so that the others that know it were told what this one is
            // told
            ArmorSync(t);
            var save = c.GetComponent<SaveableObject>();
            string name = KindOf(c);
            // The prefab's path under Resources/Prefabs, as the save system
            // knows it (Core.AddPrefab's argument).
            var sm = Singleton<SaveManager>.Instance;
            string path;
            if (sm != null && sm.prefabDictionary.TryGetValue(name, out var p))
            {
                path = p;
            }
            else
            {
                path = "Characters/" + name;
            }

            var w = _session.Begin(MsgType.EnemySpawn);
            w.W.Write(t.Id);
            int savedId = 0;
            if (save != null)
            {
                savedId = save.uniqueId;
            }

            w.W.Write(savedId);
            w.WriteStr(path);
            w.WriteStr(name);
            w.Write(c.transform.position);
            WriteRot(w, c.transform.rotation);
            int armorFlag = 0;
            if (t.Armor)
            {
                armorFlag = 1;
            }

            w.W.Write((byte)armorFlag);
            _session.SendTo(peer, w, Channel.Control, reliable: true);
            // The loop it's playing now (one that started before it was sent,
            // e.g. an NPC's aggressive one as it turned hostile): after the
            // spawn, on the same channel, so it comes after it.
            AudioObject loop = null;
            if (c.sounds != null)
            {
                loop = c.sounds.idleAudioObject;
            }

            if (loop != null && loop.IsPlaying()
                && !string.IsNullOrEmpty(loop.audioID))
            {
                SendSound(peer, t.Id, loop.audioID, 1f, reliable: true);
            }
        }

        /// <summary>
        /// EnemySound: reliable on the control channel, or unreliable on the
        /// state channel.
        /// </summary>
        private void SendSound(
            ulong peer, uint id, string audio, float volume, bool reliable)
        {
            var w = _session.Begin(MsgType.EnemySound);
            w.W.Write(id);
            w.WriteStr(audio);
            w.W.Write(volume);
            int channel;
            if (reliable)
            {
                channel = Channel.Control;
            }
            else
            {
                channel = Channel.State;
            }

            _session.SendTo(peer, w, channel, reliable);
        }

        /// <summary>
        /// Host: the clients that have this enemy (it's near them).
        /// </summary>
        private IEnumerable<ulong> Knowing(uint id)
        {
            foreach (var kv in _known)
            {
                if (kv.Value.Contains(id))
                {
                    yield return kv.Key;
                }
            }
        }

        /// <summary>
        /// Host: a corpse the client doesn't have, sent as an enemy that is
        /// shown, dead and then kept (it settles at once there, on the host's
        /// last frames of its death), then what it holds. All on the reliable
        /// channel, so they come in this order.
        /// </summary>
        private void SendCorpse(ulong peer, Tracked t)
        {
            SendSpawn(peer, t);
            var w = _one.Begin(MsgType.EnemyState);
            w.W.Write(Time.unscaledTime);
            w.W.Write((ushort)1);
            WriteState(w, t);
            _session.SendTo(peer, w, Channel.Control, reliable: true);
            SendDespawn(peer, t.Id, keepCorpse: true);
            SendCorpseLoot(peer, t.Id);
        }

        private void SendCorpseLoot(ulong peer, uint id)
        {
            var c = CharacterOf(id);
            Inventory inv = null;
            if (c != null)
            {
                inv = c.GetComponent<Inventory>();
            }

            if (inv != null)
            {
                CoopSession coop = Coop.Current;
                if (coop != null)
                {
                    coop.Containers.SendContentsTo(peer, inv);
                }
            }
        }

        private void SendDespawn(ulong peer, uint id, bool keepCorpse)
        {
            var w = _session.Begin(MsgType.EnemyDespawn);
            w.W.Write(id);
            int corpseFlag = 0;
            if (keepCorpse)
            {
                corpseFlag = 1;
            }

            w.W.Write((byte)corpseFlag);
            _session.SendTo(peer, w, Channel.Control, reliable: true);
        }

        private void Forget(uint id)
        {
            foreach (var kv in _known)
            {
                if (kv.Value.Remove(id))
                {
                    SendDespawn(kv.Key, id, keepCorpse: false);
                }
            }

            // the host's corpse is gone, so theirs goes too
            foreach (var kv in _corpseKnown)
            {
                if (kv.Value.Remove(id))
                {
                    SendDespawn(kv.Key, id, keepCorpse: false);
                }
            }
        }

        private static void WriteState(PacketWriter w, Tracked t)
        {
            var c = t.C;
            w.W.Write(t.Id);
            w.Write(c.transform.position);
            WriteRot(w, c.transform.rotation);
            int flags = 0;
            if (InPlay(c))
            {
                flags |= 1;
            }

            if (c.sleeping)
            {
                flags |= 2;
            }

            if (c.cuttingInHalf)
            {
                flags |= 4;
            }

            w.W.Write((byte)flags);
            float health = 1f;
            if (c.maxHealth > 0)
            {
                health = Mathf.Clamp01(c.health / c.maxHealth);
            }

            w.W.Write(health);
            int n = Mathf.Min(t.Animators.Count, 8);
            w.W.Write((byte)n);
            for (int i = 0; i < n; i++)
            {
                var a = t.Animators[i];
                tk2dSpriteAnimationClip clip = null;
                if (a != null)
                {
                    clip = a.CurrentClip;
                }

                w.W.Write(t.AnimatorPaths[i]);
                int hash = 0;
                if (clip != null)
                {
                    hash = StableHash.Of(clip.name);
                }

                w.W.Write(hash);
                int frame = 0;
                if (clip != null)
                {
                    frame = Mathf.Clamp(a.CurrentFrame, 0, 65535);
                }

                w.W.Write((ushort)frame);
                int playing = 0;
                if (a != null && a.Playing)
                {
                    playing = 1;
                }

                w.W.Write((byte)playing);
            }
        }

        /// <summary>
        /// Host: an enemy was hit and bled; the clients that see it get the
        /// splat too (their hits included).
        /// </summary>
        public void HostBlood(Character c, float yRot)
        {
            if (!_byCharHost.TryGetValue(c, out var t))
            {
                return;
            }

            foreach (var peer in Knowing(t.Id))
            {
                var w = _session.Begin(MsgType.EnemyBlood);
                w.W.Write(t.Id);
                w.W.Write(yRot);
                int waterFlag = 0;
                if (c.inWater)
                {
                    waterFlag = 1;
                }

                w.W.Write((byte)waterFlag);
                _session.SendTo(peer, w, Channel.State, reliable: false);
            }
        }

        private void OnBlood(ulong from, PacketReader r)
        {
            if (from != _session.HostId || !ClientMode)
            {
                return;
            }

            uint id = r.R.ReadUInt32();
            float y = r.R.ReadSingle();
            bool water = r.R.ReadByte() != 0;
            // shown already
            if (_predictedBlood.TryGetValue(id, out float at)
                && Time.realtimeSinceStartup - at < 1f)
            {
                _predictedBlood.Remove(id);
                return;
            }

            if (_puppets.TryGetValue(id, out var p) && p.C != null)
            {
                string prefab = "FX/Bloodsplats/Shotsplat_stay";
                if (water)
                {
                    prefab = "FX/Bloodsplats/Shotsplat";
                }

                Core.AddPrefab(
                    prefab, p.C.transform.position,
                    Quaternion.Euler(90f, y, 0f), null);
            }
        }

        /// <summary>
        /// Host: the players that have this enemy (it's near them), for things
        /// only they need to see.
        /// </summary>
        public List<ulong> PeersSeeing(Character c)
        {
            if (c == null || !_byCharHost.TryGetValue(c, out var t))
            {
                return new List<ulong>();
            }

            return Knowing(t.Id).ToList();
        }

        /// <summary>
        /// Host: the net ID the clients know this enemy by (false if it isn't
        /// tracked).
        /// </summary>
        public bool IdOf(Character c, out uint id)
        {
            id = 0;
            if (c == null || !_byCharHost.TryGetValue(c, out var t))
            {
                return false;
            }

            id = t.Id;
            return true;
        }

        /// <summary>
        /// Client: our copy of the host's enemy with this net ID (null if we
        /// haven't got it).
        /// </summary>
        public Character PuppetOf(uint id)
        {
            if (_puppets.TryGetValue(id, out var p))
            {
                return p.C;
            }

            return null;
        }

        /// <summary>Client: our copies of the host's living enemies.</summary>
        public IEnumerable<Character> PuppetCharacters()
        {
            foreach (var p in _puppets.Values)
            {
                if (p.C != null && p.DiedAt < 0f)
                {
                    yield return p.C;
                }
            }
        }
    }
}
