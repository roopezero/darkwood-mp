using System.Collections.Generic;
using System.Linq;
using DarkwoodMP.Combat.Enemies;
using DarkwoodMP.Net;
using DarkwoodMP.Sync;
using DarkwoodMP.World;
using UnityEngine;

namespace DarkwoodMP.Combat
{
    /// <summary>
    /// The client side: spawned copies and the host's states. See
    /// NetEnemies.cs.
    /// </summary>
    internal sealed partial class NetEnemies
    {
        /// <summary>
        /// Client: the renderers and colliders SetVisible(false) switched off,
        /// per character, so showing it again switches on just those. Switching
        /// on everything under a character woke parts the game keeps off (and a
        /// corpse's parts its die2 destroys on the host): a collider on the
        /// view's shadow layers then cut the player's view to nothing beside
        /// the corpse.
        /// </summary>
        private readonly Dictionary<Character, List<Component>>
            _switchedOff = new Dictionary<Character, List<Component>>();

        private void SetVisible(Character c, bool on)
        {
            if (!on)
            {
                if (!_switchedOff.TryGetValue(c, out var off))
                {
                    _switchedOff[c] = off = new List<Component>();
                }

                foreach (var r in c.GetComponentsInChildren<Renderer>(true))
                {
                    if (r != null && r.enabled)
                    {
                        r.enabled = false;
                        off.Add(r);
                    }
                }

                foreach (var col in c.GetComponentsInChildren<Collider>(true))
                {
                    if (col != null && col.enabled)
                    {
                        col.enabled = false;
                        off.Add(col);
                    }
                }

                return;
            }

            if (!_switchedOff.TryGetValue(c, out var list))
            {
                return;
            }

            _switchedOff.Remove(c);
            foreach (var x in list)
            {
                if (x is Renderer r && r != null)
                {
                    r.enabled = true;
                }
                else if (x is Collider col && col != null)
                {
                    col.enabled = true;
                }
            }
        }

        /// <summary>
        /// Catch characters that started before we were in client mode (e.g.
        /// rejoining in the same world).
        /// </summary>
        private void ClientSweep()
        {
            if (Time.unscaledTime < _nextSweep)
            {
                return;
            }

            _nextSweep = Time.unscaledTime + FreezeRules.SweepInterval;
            foreach (var c in Plugin.Instance.Host.Characters.Active())
            {
                var step = FreezeRules.Sweep(SweepOf(c));
                if (step == SweepStep.Thaw)
                {
                    Thaw(c);
                }
                else if (step == SweepStep.Freeze)
                {
                    Freeze(c, hide: true);
                }
            }
        }

        /// <summary>
        /// One of our characters as the sweep sees it. Whether it's in an
        /// area of our own is read only for an eligible one that isn't a
        /// copy, as before.
        /// </summary>
        private SweepFacts SweepOf(Character c)
        {
            bool eligible = Eligible(c);
            bool copy = _byChar.ContainsKey(c);
            if (!eligible || copy)
            {
                return new SweepFacts { Eligible = eligible, Copy = copy };
            }

            return new SweepFacts
            {
                Eligible = true,
                Local = LocalOnly(c),
                Frozen = _frozen.Contains(c),
            };
        }

        private static string KindOfOrNull(Character c)
        {
            if (c == null)
            {
                return null;
            }

            return KindOf(c);
        }

        private void OnSpawn(ulong from, PacketReader r)
        {
            if (!FreezeRules.FromHost(from == _session.HostId, ClientMode))
            {
                return;
            }

            var spawned = EnemySpawnMessage.Read(r);
            uint id = spawned.Id;
            int uid = spawned.SavedId;
            string prefab = spawned.Prefab;
            string name = spawned.Kind;
            Vector3 pos = VectorOf(spawned.At);
            Quaternion rot = QuaternionOf(spawned.Rot);
            bool armor = spawned.Armor;
            if (_puppets.ContainsKey(id))
            {
                return;
            }

            Character c = null;
            bool ours = false;
            var sm = Singleton<SaveManager>.Instance;
            // Our copy of it from the save, when the host's save id names the
            // same character here: only ids from the save we loaded are the
            // same in both games (SharedIds), and even then it has to be the
            // same kind of creature. The host sends an NPC only while it's
            // hostile there (HostileNpcs): ours is then frozen and shown as the
            // host's.
            Transform tr = null;
            bool found = uid > 0 && sm != null
                && sm.uniqueIdDict.TryGetValue(uid, out tr) && tr != null;
            if (found)
            {
                c = tr.GetComponent<Character>();
            }

            var saved = CopyRules.FromSave(new SavedFacts
            {
                SaveId = uid,
                Limit = World.SharedIds.ResolveLimit,
                Found = found,
                HasCharacter = c != null,
                Driven = c != null && _byChar.ContainsKey(c),
                Kind = KindOfOrNull(c),
                HostKind = name,
            });

            if (saved != SavedCopy.Use)
            {
                if (saved == SavedCopy.OtherKind)
                {
                    var log = Plugin.Log;
                    var kind = KindOf(c);
                    string npcNote = " (an NPC)";
                    if (c.npc == null)
                    {
                        npcNote = "";
                    }

                    log.LogInfo(
                        $"Save id {uid} is our {kind}{npcNote}, not the host's "
                            + $"{name}: making it from its prefab");
                }

                // none, or already driven by another id
                c = null;
            }

            // An NPC made while playing (a scripted spawn runs in every game)
            // has a save id of its own in each game: ours is the one of that
            // kind near where the host's is.
            if (c == null)
            {
                c = HostileNpcs.OurCopy(
                    name, pos, x => _byChar.ContainsKey(x), KindOf);
            }

            if (c == null)
            {
                GameObject go = null;
                foreach (var candidate in CopyRules.Prefabs(prefab, name))
                {
                    var res = Resources.Load(candidate);
                    if (res != null)
                    {
                        go = Core.AddPrefab(
                            res, pos, rot, null, worldSpace: true);
                        break;
                    }
                }

                if (go != null)
                {
                    c = go.GetComponent<Character>();
                }
                else
                {
                    c = null;
                }

                ours = c != null;
                if (c == null)
                {
                    if (go != null)
                    {
                        Object.Destroy(go);
                    }

                    Plugin.Log.LogWarning(
                        $"Couldn't make the host's enemy {name} ({prefab})");
                    return;
                }

                var save = c.GetComponent<SaveableObject>();
                if (save != null)
                {
                    // it's the host's; don't write it into this world's save
                    save.dontSave = true;
                }
            }

            var p = new Puppet { Id = id, C = c, Ours = ours };
            bool wasOff = !c.gameObject.activeInHierarchy;
            Uncull(p);
            Freeze(c, hide: false);
            SetVisible(c, true);
            // shown as the host's now: its idle loop plays again
            _hidden.Remove(c);
            PlayIdleLoop(c);
            c.transform.SetPositionAndRotation(pos, rot);
            Animators(c.transform, out var list, out var paths);
            for (int i = 0; i < list.Count; i++)
            {
                p.Animators[paths[i]] = list[i];
            }

            _puppets[id] = p;
            _byChar[c] = p;
            if (c.npc != null)
            {
                HostileNpcs.ClientStarted(c, id);
            }

            SetArmor(c, armor, fade: false);
            string text = "our copy from the save";
            if (ours)
            {
                text = "made from its prefab";
            }

            string switched = "";
            if (wasOff)
            {
                switched = ", switched on";
            }

            Plugin.Log.LogInfo(
                $"Showing the host's {name} #{id} ({text}{switched}); "
                    + $"{_frozen.Count} of our own characters are frozen");
            LogFovBlockers(c, $"#{id} shown");
        }

        private void OnState(ulong from, PacketReader r)
        {
            if (!FreezeRules.FromHost(from == _session.HostId, ClientMode))
            {
                return;
            }

            var states = EnemyStateMessage.Read(r);
            float sent = states.Sent;
            float now = Time.unscaledTime;
            var coop = Coop.Current;
            if (coop != null)
            {
                coop.Motion.Arrived(sent, now);
            }

            foreach (var frame in states.Frames)
            {
                uint id = frame.Id;
                Vector3 pos = VectorOf(frame.At);
                Quaternion rot = QuaternionOf(frame.Rot);
                int bits = frame.Flags;
                bool alive = (bits & EnemyStateMessage.Alive) != 0,
                    asleep = (bits & EnemyStateMessage.Asleep) != 0,
                    cut = (bits & EnemyStateMessage.Cut) != 0;
                float healthFraction = frame.Health;
                _puppets.TryGetValue(id, out var p);
                foreach (var anim in frame.Anims)
                {
                    if (p != null && !p.Settled
                        && p.Animators.TryGetValue(anim.Animator, out var a)
                        && a != null)
                    {
                        ApplyAnim(a, anim.Clip, anim.Frame, anim.Playing);
                    }
                }

                if (p == null || p.C == null)
                {
                    continue;
                }

                // Drawn by the host's time (EnemyMotion), frame by frame
                // (MovePuppets).
                EnemyMotion.Add(p.Motion, sent, pos, rot);
                p.Has = true;
                if (alive)
                {
                    SetHealth(p.C, healthFraction);
                }

                if (CopyStateRules.SleepChanges(alive, p.C.sleeping, asleep))
                {
                    // Asleep or awake as the host's is, with the idle loop to
                    // match (a mimic's sleeping one).
                    p.C.sleeping = asleep;
                    // Waking, the host's own loop (idle, or aggressive as it
                    // comes at someone) comes as an EnemySound, maybe before
                    // this: only the sleeping one is replaced.
                    AudioObject loop = null;
                    if (p.C.sounds != null)
                    {
                        loop = p.C.sounds.idleAudioObject;
                    }

                    if (CopyStateRules.ReplaysIdle(SleepOf(asleep, p.C, loop)))
                    {
                        PlayIdleLoop(p.C);
                    }
                }

                if (CopyStateRules.MarksCut(alive, cut))
                {
                    p.Cut = true;
                }

                if (CopyStateRules.FirstDeath(alive, p.DiedAt >= 0f))
                {
                    // Out of play as on the host: a character with a pre-death
                    // stage would still count as in it here
                    // (Character.hasPreDeath with health left).
                    p.C.health = CopyStateRules.DeathHealth(
                        p.C.maxHealth, p.C.health, healthFraction);
                    p.C.alive = false;
                    p.DiedAt = Time.unscaledTime;
                    StopSounds(p.C, keepDeathSound: true);
                    // What's attached to it (the human spider's sound_loop)
                    // goes as it dies, as die2 does on the host.
                    try
                    {
                        p.C.removeAttachedGameObjects();
                    }
                    catch (System.Exception e)
                    {
                        Plugin.Log.LogWarning(
                            $"Couldn't remove what's attached to {p.C.name}: "
                                + $"{e.Message}");
                    }

                    // The human spiders' sound_loop child (an Item with
                    // ItemSounds) is gone from the host's copy as it dies (seen
                    // in the map test, 0.5 s after); a copy's frozen scripts
                    // never remove it.
                    foreach (var it in p.C.GetComponentsInChildren<Item>(true))
                    {
                        if (it != null && it.gameObject != p.C.gameObject
                            && it.GetComponent<ItemSounds>() != null)
                        {
                            Object.Destroy(it.gameObject);
                        }
                    }

                    if (p.Cut)
                    {
                        CutLooks(p.C);
                    }
                    else
                    {
                        DeathLooks(p.C);
                    }

                    string text = "";
                    if (p.Cut)
                    {
                        text = " (cut in half)";
                    }

                    Plugin.Log.LogInfo(
                        $"[enemy] {p.Id} died{text}: sounds stopped");
                }
            }
        }

        /// <summary>
        /// Client: silence a copy of the host's enemy, as Character.die2 does
        /// on the host (sounds.destroySounds). The copy never runs die2, so
        /// without this its own idle loop (started by CharacterSounds.OnEnable)
        /// and the loops the host relayed (a dog's barking, which the host
        /// stops in its die2) went on playing over the corpse; and Settle's
        /// destroyComponents destroys CharacterSounds without stopping its
        /// loop, which left it playing for good. Its coroutines are stopped
        /// too, as die2 does.
        /// </summary>
        private static void StopSounds(Character c, bool keepDeathSound)
        {
            if (c == null)
            {
                return;
            }

            string death = null;
            if (c.sounds != null)
            {
                death = c.sounds.death;
            }

            if (c.sounds != null)
            {
                try
                {
                    c.sounds.destroySounds();
                }
                catch (System.Exception e)
                {
                    Plugin.Log.LogWarning(
                        $"Couldn't stop {c.name}'s sounds: {e.Message}");
                }
            }

            // Sounds played on it (the relayed ones and its own) are
            // AudioObjects under it.
            foreach (var ao in c.GetComponentsInChildren<AudioObject>(true))
            {
                if (ao == null || CopyStateRules.KeepsSound(
                    keepDeathSound, death, ao.audioID))
                {
                    continue;
                }

                ao.Stop(0.3f);
            }

            if (!keepDeathSound)
            {
                return; // only hidden, not dead
            }

            c.StopAllCoroutines();
        }

        /// <summary>
        /// Client: the host's health for a copy, and its health bar as the game
        /// does for a hit not by our player: shown if the player has it
        /// selected, and refreshed if it's the one shown.
        /// </summary>
        private static void SetHealth(Character c, float fraction)
        {
            var change = CopyStateRules.Health(c.maxHealth, c.health, fraction);
            if (!change.Changes)
            {
                return;
            }

            c.health = change.Health;
            HealthBar(
                c,
                show: change.Lost && Player.Instance != null
                    && Player.Instance.selectedObject == c.transform);
        }

        /// <summary>
        /// A copy's sleep and idle loop as CopyStateRules.ReplaysIdle sees
        /// them.
        /// </summary>
        private static SleepFacts SleepOf(
            bool asleep,
            Character c,
            AudioObject loop)
        {
            if (loop == null)
            {
                return new SleepFacts { Asleep = asleep };
            }

            return new SleepFacts
            {
                Asleep = asleep,
                Playing = loop.IsPlaying(),
                SleepingLoop = loop.audioID == c.sounds.sleeping,
            };
        }

        /// <summary>
        /// Show an enemy's health bar (as Character.getHit does for a hit by
        /// the player), or refresh it if it's showing.
        /// </summary>
        private static void HealthBar(Character c, bool show)
        {
            EnemyHealthBar bar = null;
            if (Singleton<UI>.Instance != null)
            {
                bar = Singleton<UI>.Instance.enemyHealthBar;
            }

            if (bar == null || c == null)
            {
                return;
            }

            try
            {
                if (show)
                {
                    bar.show(c.gameObject);
                }
                else if (bar.currentObj == c.gameObject)
                {
                    bar.show(c.gameObject, onlyRefresh: true);
                }
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning(
                    $"Couldn't show {c.name}'s health bar: {e.Message}");
            }
        }
    }
}
