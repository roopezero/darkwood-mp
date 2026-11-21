using System.Collections.Generic;
using System.Linq;
using DarkwoodMP.Combat.Enemies;
using DarkwoodMP.Net;
using DarkwoodMP.Sync;
using DarkwoodMP.World;
using DarkwoodMP.World.Dreaming;
using UnityEngine;

namespace DarkwoodMP.Combat
{
    /// <summary>
    /// The host's enemies, shown on its clients. The host runs every enemy; a
    /// client runs none of its own.
    ///
    /// Host: every non-NPC Character gets a net id. Each client is told about
    /// the ones near it (within RelevantRange of its proxy) with EnemySpawn,
    /// then gets EnemyState for them twenty times a second (the host's clock,
    /// position, rotation, alive, and each sprite animator's clip and frame),
    /// EnemySound for the sounds they make, and EnemyDespawn when one goes out
    /// of its range, or a while after it died (its corpse then stays put).
    /// A corpse is sent to each client once, when it first comes within its
    /// range (one that was already dead when they joined, or that they walked
    /// away from before it settled): EnemySpawn, one EnemyState that says it's
    /// dead, and EnemyDespawn keeping the corpse, then what it holds. The
    /// client's corpse stays until the host's is gone.
    ///
    /// Client: its own non-NPC characters are frozen as they start (their AI,
    /// pathfinding and physics off, their animators' frame events cleared so
    /// they can't attack or make sounds) and hidden. A spawned enemy is the
    /// client's own copy of it from the save, found by its save id, or else
    /// made from its prefab; it's then shown and driven by the host's states,
    /// playing the same animation clips. Damage the client deals it (melee,
    /// bullets, fire...) goes to the host as EnemyHit instead of being applied,
    /// and the host applies it with the client's proxy as the attacker, so the
    /// enemy reacts to, and gains threat for, that player.
    ///
    /// NPCs (traders, quest characters) are each player's own while they're
    /// friendly. One that turns on a player is run by the host as one of these
    /// enemies until it calms down (Combat.HostileNpcs).
    /// </summary>
    internal sealed partial class NetEnemies
    {
        public const float RelevantRange = EnemyRelayRules.RelevantRange;
        public const float StateInterval = EnemyRelayRules.StateInterval;

        /// <summary>
        /// The session's NetEnemies (CoopSession.Enemies), or null when
        /// there's no session.
        /// </summary>
        public static NetEnemies Instance
        {
            get
            {
                CoopSession coop = Coop.Current;
                if (coop == null)
                {
                    return null;
                }

                return coop.Enemies;
            }
        }

        private readonly Session _session;

        internal NetEnemies(CoopSession coop)
        {
            _session = coop.Net;
            coop.Handle(MsgType.EnemySpawn, OnSpawn);
            coop.Handle(MsgType.EnemyState, OnState);
            coop.Handle(MsgType.EnemyDespawn, OnDespawn);
            coop.Handle(MsgType.EnemySound, OnSound);
            coop.Handle(MsgType.EnemyHit, OnHit);
            coop.Handle(MsgType.EnemyBlood, OnBlood);
            coop.Handle(MsgType.EnemyArmor, OnArmor);
            coop.Net.OnPeerDisconnected += PeerLeft;
            coop.Hook(() => coop.Net.OnPeerDisconnected -= PeerLeft);
        }

        private void PeerLeft(ulong id)
        {
            _known.Remove(id);
            _corpseKnown.Remove(id);
        }

        /// <summary>
        /// One of the characters the host runs and its clients show: every
        /// creature, and an NPC while it's hostile (Combat.HostileNpcs: the
        /// host's copy turned on a player, or a client's copy is one the host
        /// runs).
        /// </summary>
        public static bool Eligible(Character c)
        {
            if (c == null)
            {
                return false;
            }

            return EnemyTrackRules.Eligible(
                c.npc != null, HostileNpcs.Running(c));
        }

        /// <summary>
        /// Still in the fight: alive, or in a pre-death stage
        /// (Character.hasPreDeath: alive is false, but it crawls with health
        /// left until a hit finishes it or it bleeds out; getHit and the
        /// player's melee still hit it).
        /// </summary>
        public static bool InPlay(Character c)
        {
            if (c == null)
            {
                return false;
            }

            return EnemyTrackRules.InPlay(c.alive, c.hasPreDeath, c.health);
        }

        /// <summary>
        /// Client: a character of an area only this player is in: a dream of
        /// its own (the shared level dream is the host's,
        /// Whereabouts.SharedDream), or a separate location the host didn't
        /// place for us (Whereabouts.HostRunsHere), or anything still in a
        /// scene of its own. The host doesn't run it, so it runs here, as in
        /// single player. A separate location the host placed is the host's to
        /// run, as the world is (it loads it at the same spot even when its
        /// player isn't there): our characters there are frozen and the host's
        /// are shown.
        /// </summary>
        public static bool LocalOnly(Character c) =>
            c != null && LocalOnly(c.gameObject);

        /// <summary>
        /// Client: a thing in an area only this player is in (as for a
        /// character, above).
        /// </summary>
        public static bool LocalOnly(GameObject go)
        {
            if (go == null)
            {
                return false;
            }

            if (go.scene
                != UnityEngine.SceneManagement.SceneManager.GetActiveScene())
            {
                return true;
            }

            // In the dream's own place: also while our player is still on the
            // way in (a stage's start event fires before the player stands
            // there), when the distance below doesn't tell yet. Not a shared
            // level dream's (or the epilogue's): the host runs those
            // (Whereabouts.SharedDream); a prologue stage each game runs.
            var d = Singleton<Dreams>.Instance;
            if (d != null && d.dreamLocation != null
                && (d.dreaming || d.dreamPrepared || d.switchingDream)
                && go.transform.IsChildOf(d.dreamLocation.transform)
                && (DreamSync.SharedNow == null || World.Prologue.IsStage(
                    DreamSync.SharedNow)))
            {
                return true;
            }

            if (World.Places.Whereabouts.HostRunsHere())
            {
                return false;
            }

            var p = Player.Instance;
            return p != null
                && (go.transform.position - p.transform.position).sqrMagnitude
                    < 4000f * 4000f;
        }

        /// <summary>
        /// Client: undo Freeze, for a character that turned out to be in an
        /// area of our own.
        /// </summary>
        public static void Thaw(Character c)
        {
            NetEnemies enemies = Instance;
            if (enemies != null)
            {
                enemies.ThawHere(c);
            }
        }

        /// <summary>
        /// Set while ThawHere starts a character itself, so the Character.Start
        /// patch lets it start.
        /// </summary>
        public bool Starting { get; private set; }

        private void ThawHere(Character c)
        {
            if (c == null || !_frozen.Remove(c))
            {
                return;
            }

            _hidden.Remove(c);
            SetVisible(c, true);
            var rb = c.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = false;
            }

            if (c.AIpath != null)
            {
                c.AIpath.enabled = true;
            }

            foreach (var mb in c.GetComponents<MonoBehaviour>())
            {
                if (mb is AILerp)
                {
                    mb.enabled = true;
                }
            }

            c.enabled = true; // its OnEnable hooks its animators back up
            Starting = true;
            try
            {
                c.Start();
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning(
                    $"{c.name} didn't start cleanly: {e.Message}");
            }
            finally
            {
                Starting = false;
            }

            PlayIdleLoop(c);
        }

        /// <summary>
        /// Client: start a shown character's idle loop again
        /// (CharacterSounds.OnEnable's), stopped while it was hidden: its
        /// sleeping one while the host's is asleep (Character.sleeping, sent in
        /// EnemyState), as enableComponents picks.
        /// </summary>
        private static void PlayIdleLoop(Character c)
        {
            if (c == null || !c.alive || c.sounds == null)
            {
                return;
            }

            try
            {
                string loopName;
                if (c.sleeping)
                {
                    loopName = c.sounds.sleeping;
                }
                else
                {
                    loopName = c.sounds.idleLoop;
                }

                c.sounds.playIdleLoop(loopName);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning(
                    $"Couldn't start {c.name}'s idle sound: {e.Message}");
            }
        }

        public void Update()
        {
            if (EnemyRelayRules.HostRuns(
                _session.IsHost, _session.Peers.Count, WorldShare.InWorld))
            {
                HostUpdate();
            }

            if (ClientMode)
            {
                ClientSweep();
                MovePuppets();
            }
        }

        /// <summary>
        /// Client, every frame: each of the host's enemies drawn where the host
        /// has it now, by the host's clock and its last states (EnemyMotion),
        /// so they move at the host's speed whenever its states come.
        /// </summary>
        private void MovePuppets()
        {
            float now = Time.unscaledTime, dt = Time.unscaledDeltaTime;
            var coop = Coop.Current;
            if (coop == null)
            {
                return;
            }

            var motion = coop.Motion;
            motion.Frame(dt);
            foreach (var p in _puppets.Values)
            {
                if (p.C != null && p.DiedAt >= 0f && !p.Settled
                    && now - p.DiedAt > DeathAnimTime)
                {
                    Settle(p);
                }

                if (!p.Has || p.C == null)
                {
                    continue;
                }

                if (!p.C.gameObject.activeSelf && !p.Settled)
                {
                    Plugin.Log.LogInfo(
                        $"[enemy] the host's {p.C.name} #{p.Id} was switched "
                            + "off here: on again");
                    p.C.gameObject.SetActive(true);
                }

                if (!motion.Where(p.Motion, now, out var pos, out var rot))
                {
                    continue;
                }

                p.C.transform.SetPositionAndRotation(pos, rot);
                motion.Drawn(p.Motion, pos, dt);
                AlignShadow(p.C);
            }
        }

        /// <summary>
        /// Client: a copy's shadow, placed as Character.Update places it (the
        /// copy's Update doesn't run): not turned with the body unless
        /// rotateShadow, at shadow height, lowered once it's dead. A flier's
        /// shadow is offset by its altitude, which the host doesn't send, so
        /// it's left as it is.
        /// </summary>
        private static void AlignShadow(Character c)
        {
            var sh = c.shadow;
            if (sh == null || c.flier != null)
            {
                return;
            }

            if (!c.rotateShadow)
            {
                sh.eulerAngles = new Vector3(90f, 0f, 0f);
            }

            var at = c.transform.position + new Vector3(
                c.shadowOffset.x, 0f, c.shadowOffset.z);
            PosType posType;
            if (c.alive)
            {
                posType = PosType.characterShadow;
            }
            else
            {
                posType = PosType.low1;
            }

            sh.position =
                Core.getYPos(at, posType, randomize: false) + c.posSeed;
        }
    }
}
