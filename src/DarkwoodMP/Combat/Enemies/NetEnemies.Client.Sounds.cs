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
    /// The client side: animation, despawns, sounds and our hits. See
    /// NetEnemies.cs.
    /// </summary>
    internal sealed partial class NetEnemies
    {
        /// <summary>
        /// Client: Character.setDeathCollider stopped part way on a copy (it
        /// threw). Its first step, the Item that makes the corpse searchable,
        /// is done by then; the rest is done here, each part only if what it
        /// needs is there: the body becomes a trigger (or our player bumps into
        /// a corpse the host's player walks through), takes the death collider
        /// of the clip it ended on, stops counting as active, and lies at a
        /// corpse's height. An NPC's death (its flags, its death inventory)
        /// comes from the host.
        /// </summary>
        private static void DeathColliderRest(Character c, System.Exception why)
        {
            try
            {
                if (c.GetComponent<Item>() == null)
                {
                    c.gameObject.AddComponent<Item>().name =
                        c.name.ToLower() + "_corpse";
                }

                if (!c.dontSwitchColliderTriggerOnDeath && c.collider != null)
                {
                    c.collider.isTrigger = true;
                }

                var a = c.GetComponent<tk2dSpriteAnimator>();
                string clip = null;
                if (a != null && a.CurrentClip != null)
                {
                    clip = a.CurrentClip.name;
                }

                if (clip != null && c.capsuleCollider != null
                    && c.deathColliders != null)
                {
                    foreach (var dc in c.deathColliders)
                    {
                        if (dc != null && dc.identifier == clip)
                        {
                            c.capsuleCollider.center = dc.center;
                            c.capsuleCollider.radius = dc.radius;
                            c.capsuleCollider.height = dc.height;
                        }
                    }
                }

                c.isActive = false;
                System.Reflection.MethodInfo yPosAfterDeath = YPosAfterDeath;
                if (yPosAfterDeath != null)
                {
                    yPosAfterDeath.Invoke(c, null);
                }

                string text = "none";
                if (a != null)
                {
                    text = "there";
                }

                string colliderState = "none";
                if (c.collider != null)
                {
                    colliderState = "there";
                }

                string capsuleState = "none";
                if (c.capsuleCollider != null)
                {
                    capsuleState = "there";
                }

                string shownClip = clip;
                if (shownClip == null)
                {
                    shownClip = "none";
                }

                List<ColliderInfo> deathColliders = c.deathColliders;
                int count = -1;
                if (deathColliders != null)
                {
                    count = deathColliders.Count;
                }

                string stackTrace = why.StackTrace;
                string trimmedLine = null;
                if (stackTrace != null)
                {
                    string line = stackTrace.Split('\n')[0];
                    if (line != null)
                    {
                        trimmedLine = line.Trim();
                    }
                }

                Plugin.Log.LogInfo(
                    $"{c.name}'s corpse settled here by the mod: the game's "
                        + $"setDeathCollider stopped ({why.Message}; " +
                                   $"animator {text}, clip {shownClip}, "
                        + $"collider {colliderState}, " +
                                   $"capsule {capsuleState}, death colliders "
                        + $"{count}; at {trimmedLine}); trigger now "
                        + $"{(c.collider != null && c.collider.isTrigger)}");
            }
            catch (System.Exception e)
            {
                System.Exception innerException = e.InnerException;
                string message = null;
                if (innerException != null)
                {
                    message = innerException.Message;
                }

                if (message == null)
                {
                    message = e.Message;
                }

                Plugin.Log.LogWarning(
                    $"Couldn't settle {c.name}'s corpse: {message}");
            }
        }

        private static void ApplyAnim(
            tk2dSpriteAnimator a, int clipHash, int frame, bool playing)
        {
            // never let a puppet's frames act (attack, sound) here
            a.AnimationEventTriggered = CopyFrameEvent;
            a.AnimationCompleted = null;
            if (clipHash == 0 || a.Library == null)
            {
                return;
            }

            var cur = a.CurrentClip;
            if (cur == null || StableHash.Of(cur.name) != clipHash)
            {
                foreach (var clip in a.Library.clips)
                {
                    if (clip != null && StableHash.Of(clip.name) == clipHash)
                    {
                        a.PlayFromFrame(
                            clip,
                            Mathf.Clamp(
                                frame, 0,
                                Mathf.Max(0, clip.frames.Length - 1)));
                        break;
                    }
                }
            }
            // More than 2 frames off: move the copy's clip time to the host's
            // frame. (SetFrame would change only the sprite shown; the next
            // animator update goes back to the copy's own clip time, so the
            // copy flicked to the host's frame and back on every update and
            // stayed off, as a world rabbit did 3 frames ahead.)
            else if (Mathf.Abs(a.CurrentFrame - frame) > 2
                && cur.frames != null && frame < cur.frames.Length)
            {
                a.PlayFromFrame(cur, frame);
            }

            if (!playing && a.Playing)
            {
                a.Pause();
            }
            else if (playing && a.Paused)
            {
                a.Resume();
            }
        }

        /// <summary>
        /// Client: a copy's animation events. Of what Character.OnAniEvent
        /// does, only "TweenShadows" (fading the character's extra shadows out
        /// or in with the animation) is played here: it changes how the copy
        /// looks and nothing else. Attacks, sounds and the rest are the host's.
        /// </summary>
        private static void CopyFrameEvent(
            tk2dSpriteAnimator a, tk2dSpriteAnimationClip clip, int frame)
        {
            try
            {
                tk2dSpriteAnimationFrame f = null;
                if (clip != null)
                {
                    f = clip.GetFrame(frame);
                }

                if (f == null || f.eventInfo != "TweenShadows")
                {
                    return;
                }

                Character c = null;
                if (a != null)
                {
                    c = a.GetComponentInParent<Character>();
                }

                if (c != null)
                {
                    c.tweenShadows(hide: f.eventInt == 0);
                }
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning(
                    $"Couldn't play a copy's TweenShadows: {e.Message}");
            }
        }

        private void OnDespawn(ulong from, PacketReader r)
        {
            if (from != _session.HostId)
            {
                return;
            }

            var mark = EnemyMarkMessage.Read(r);
            uint id = mark.Id;
            bool keepCorpse = mark.On;
            if (!_puppets.TryGetValue(id, out var p))
            {
                // The host's corpse is gone (it was destroyed there): so is
                // ours.
                if (keepCorpse || !_corpses.TryGetValue(id, out p))
                {
                    return;
                }

                _corpses.Remove(id);
                if (p.C == null)
                {
                    return;
                }

                _corpseIds.Remove(p.C);
                Plugin.Log.LogInfo(
                    $"The host's corpse #{id} ({p.C.name}) is gone there: "
                        + "removed here");
                // Destroyed, our copy from the save too: the host's is out of
                // its world for good, and a hidden copy would still be in ours
                // (its body searchable by name, saved with our world).
                World.Inventories.Containers.CloseIfOpen(p.C.gameObject);
                Object.Destroy(p.C.gameObject);
                return;
            }

            _puppets.Remove(id);
            if (p.C == null)
            {
                return;
            }

            _byChar.Remove(p.C);
            if (keepCorpse) // stays where it fell
            {
                if (!p.Settled)
                {
                    Settle(p);
                }

                _corpses[id] = p;
                _corpseIds[p.C] = id;
                Recull(p);
                return;
            }

            StopSounds(p.C, keepDeathSound: false);
            // An NPC the host calmed down (or one out of our range): our copy
            // of it is ours to run again.
            if (p.C.npc != null
                && HostileNpcs.ClientStopped(p.C, id, ours: p.Ours))
            {
                Recull(p);
                return;
            }

            // its window, if our player has its corpse open: gone or hidden, it
            // is gone for us
            World.Inventories.Containers.CloseIfOpen(p.C.gameObject);
            if (p.Ours)
            {
                Object.Destroy(p.C.gameObject);
            }
            else
            {
                Hide(p.C);
                Recull(p);
            }
        }

        private void OnSound(ulong from, PacketReader r)
        {
            if (from != _session.HostId)
            {
                return;
            }

            var sound = EnemySoundMessage.Read(r);
            uint id = sound.Id;
            string audio = sound.Audio;
            float volume = sound.Volume;
            if (!_puppets.TryGetValue(id, out var p) || p.C == null)
            {
                return;
            }

            // Once it's dead here, only its death cry still plays (it can
            // arrive just after the state that said so); a bark or a loop from
            // before the host's die2, arriving late, would go on over the
            // corpse.
            if (p.DiedAt >= 0f
                && (p.C.sounds == null || audio != p.C.sounds.death))
            {
                return;
            }

            // One of its loops (CharacterSounds.playIdleLoop: idle, aggressive
            // while it chases, defensive, sleeping, escaping) takes the place
            // of the copy's loop, as on the host. Played as a sound it would go
            // on beside the copy's idle loop, and nothing would stop it. "" is
            // the host's loop stopping (CharacterSoundsPlayIdleLoopStop).
            var snd = p.C.sounds;
            if (string.IsNullOrEmpty(audio))
            {
                if (snd != null)
                {
                    try
                    {
                        snd.playIdleLoop("");
                    }
                    catch (System.Exception e)
                    {
                        Plugin.Log.LogWarning(
                            $"Couldn't stop {p.C.name}'s loop: {e.Message}");
                    }
                }

                return;
            }

            if (IsLoop(snd, audio))
            {
                try
                {
                    snd.playIdleLoop(audio);
                }
                catch (System.Exception e)
                {
                    Plugin.Log.LogWarning(
                        $"Couldn't play {p.C.name}'s loop {audio}: "
                            + $"{e.Message}");
                }

                return;
            }

            AudioController.Play(audio, p.C.transform, volume);
        }

        /// <summary>
        /// Client: we hit a host's enemy; the host applies it.
        /// </summary>
        public void ClientHit(
            uint id, float damage, bool cut, bool byPlayer, bool interrupt,
            bool normal, List<InvItemEffect> effects)
        {
            int flags = 0;
            if (cut)
            {
                flags |= EnemyHitMessage.Cut;
            }

            if (byPlayer)
            {
                flags |= EnemyHitMessage.ByPlayer;
            }

            if (interrupt)
            {
                flags |= EnemyHitMessage.Interrupt;
            }

            if (normal)
            {
                flags |= EnemyHitMessage.Normal;
            }

            InvItemClass item = null;
            if (Player.Instance != null)
            {
                item = Player.Instance.currentItem;
            }

            string s;
            if (InvItemClass.isNull(item))
            {
                s = "";
            }
            else
            {
                s = Newtonsoft.Json.JsonConvert.SerializeObject(
                    new InvItemClass.SaveState(item, 0));
            }

            var w = _session.Begin(MsgType.EnemyHit);
            EnemyHitMessage.Write(w, new EnemyHitRequest
            {
                Id = id,
                Damage = damage,
                Flags = (byte)flags,
                Effects = PlayerDamage.SentOf(effects),
                Weapon = s,
            });

            _puppets.TryGetValue(id, out var p);
            Character hit = null;
            if (p != null)
            {
                hit = p.C;
            }

            Coop.Current.Authority.Request(
                w, $"hit on enemy #{id} for {damage:0}",
                (ok, reason, result) =>
            {
                if (ok)
                {
                    ScriptedEvents.HitApplied(hit, result);
                }
            });

            // Our own hit shows its health bar, as getHit does; the host's
            // health for it follows in its next state.
            if (p == null || p.C == null)
            {
                return;
            }

            if (damage > 0f)
            {
                HealthBar(p.C, show: true);
            }

            // Predicted: the blood shows at once; the host's for this hit (a
            // moment later) isn't shown again.
            Core.AddPrefab(
                "FX/Bloodsplats/Shotsplat_stay", p.C.transform.position,
                Quaternion.Euler(90f, UnityEngine.Random.Range(0f, 360f), 0f),
                null);
            _predictedBlood[id] = Time.realtimeSinceStartup;
        }

        private readonly Dictionary<uint, float> _predictedBlood =
            new Dictionary<uint, float>();

        /// <summary>
        /// Client: the effects of the weapon hitting right now (set around a
        /// player's MeleeSensor hit), the session's: with no session it reads
        /// null and keeps nothing.
        /// </summary>
        public static List<InvItemEffect> HitEffects
        {
            get
            {
                NetEnemies enemies = Instance;
                if (enemies == null)
                {
                    return null;
                }

                return enemies._hitEffects;
            }

            set
            {
                NetEnemies enemies = Instance;
                if (enemies != null)
                {
                    enemies._hitEffects = value;
                }
            }
        }

        private List<InvItemEffect> _hitEffects;

        // ---- shared helpers -------------------------------------------------

        private static void Animators(
            Transform root, out List<tk2dSpriteAnimator> list,
            out List<int> paths)
        {
            list = new List<tk2dSpriteAnimator>();
            paths = new List<int>();
            foreach (var a in root.GetComponentsInChildren<tk2dSpriteAnimator>(
                true))
            {
                string p;
                if (a.transform == root)
                {
                    p = ".";
                }
                else
                {
                    p = a.name;
                }

                if (a.transform != root)
                {
                    for (var t = a.transform.parent; t != null
                        && t != root; t = t.parent)
                    {
                        p = t.name + "/" + p;
                    }
                }

                list.Add(a);
                paths.Add(StableHash.Of(p));
            }
        }

        private static Vec3 VecOf(Vector3 v) => new Vec3(v.x, v.y, v.z);

        private static Vector3 VectorOf(Vec3 v) => new Vector3(v.X, v.Y, v.Z);

        private static Quat QuatOf(Quaternion q) =>
            new Quat(q.x, q.y, q.z, q.w);

        private static Quaternion QuaternionOf(Quat q) =>
            new Quaternion(q.X, q.Y, q.Z, q.W);
    }
}
