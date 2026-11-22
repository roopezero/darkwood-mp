using System.Collections.Generic;
using UnityEngine;

namespace DarkwoodMP.Sync.Players
{
    /// <summary>
    /// Visual puppet of another player: a root GameObject with one flat child
    /// per mirrored sprite. Deliberately NOT a Player/CharBase: those are
    /// singletons wired into input, UI and saving. Root motion is
    /// snapshot-interpolated; sprite frames snap to the latest snapshot.
    /// </summary>
    public sealed partial class RemotePlayer
    {
        public readonly ulong Owner;
        public string Name;
        public GameObject Root { get; private set; }
        public bool Alive => PuppetRules.IsAlive(Facts);
        public bool Spectating => PuppetRules.IsSpectating(Facts);
        /// <summary>In a separate location or a dream.</summary>
        public bool Away => PuppetRules.IsAway(Facts);
        /// <summary>
        /// The latest snapshot (its state bits: invisible, wards, forest
        /// skills), or null before the first.
        /// </summary>
        public PlayerSnapshot Last
        {
            get
            {
                if (_hasLast)
                {
                    return _last;
                }

                return null;
            }
        }

        private readonly PlayerSnapshot _prev = new PlayerSnapshot();
        private readonly PlayerSnapshot _last = new PlayerSnapshot();
        private bool _hasPrev, _hasLast;
        private readonly Dictionary<int, tk2dSprite> _parts =
            new Dictionary<int, tk2dSprite>();
        private readonly HashSet<int> _seen = new HashSet<int>();

        /// What the latest snapshot says, for PuppetRules.
        private PuppetFacts Facts => new PuppetFacts
        {
            HasLast = _hasLast,
            Alive = _last.Alive,
            Spectating = _last.Spectating,
            Away = _last.Away,
            Flashlight = _last.Flashlight,
            HeldLight = _last.HeldLight,
            Lantern = _last.Lantern,
            BeamRadius = _last.BeamRadius,
        };

        private static Vec2 Flat(Vector3 v) => new Vec2(v.x, v.z);

        public RemotePlayer(ulong owner, string name)
        {
            Owner = owner;
            Name = name;
        }

        /// <summary>
        /// A snapshot said they're no longer alive, after one that said they
        /// were.
        /// </summary>
        public event System.Action<RemotePlayer> OnDied;

        public void Push(PlayerSnapshot snap)
        {
            if (PuppetRules.Died(_hasLast, _last.Alive, snap.Alive))
            {
                System.Action<RemotePlayer> handler = OnDied;
                if (handler != null)
                {
                    handler(this);
                }
            }

            // A first position, or a jump further than anyone walks between two
            // updates (placed on joining, a teleport), is shown at once rather
            // than slid across, through whoever stands between.
            if (PuppetRules.KeepsPrevious(
                _hasLast, Flat(_last.Position), Flat(snap.Position)))
            {
                _prev.CopyFrom(_last);
                _hasPrev = true;
            }
            else
            {
                _hasPrev = false;
            }

            _last.CopyFrom(snap);
            _last.Time = Time.time;
            _hasLast = true;
            ApplySprites();
            ApplyLights();
        }

        private void EnsureRoot()
        {
            if (Root != null)
            {
                return; // Unity null: also true after scene unload destroyed it
            }

            Root = new GameObject($"DarkwoodMP_Remote_{Owner}");
            _parts.Clear();
        }

        private void ApplySprites()
        {
            EnsureRoot();
            _seen.Clear();
            foreach (var s in _last.Sprites)
            {
                _seen.Add(s.PathHash);
                var coll =
                    Plugin.Instance.Host.Sprites.Get(
                        s.CollectionHash, _last.Library);
                if (coll == null)
                {
                    continue;
                }

                if (!_parts.TryGetValue(s.PathHash, out var spr) || spr == null)
                {
                    var go = new GameObject($"part_{s.PathHash:X8}");
                    go.transform.SetParent(Root.transform, false);
                    spr = tk2dBaseSprite.AddComponent<tk2dSprite>(
                        go, coll, s.SpriteId);
                    _parts[s.PathHash] = spr;
                    NotSolid(spr.transform);
                }

                var t = spr.transform;
                if (!spr.gameObject.activeSelf)
                {
                    spr.gameObject.SetActive(true);
                }

                if (spr.gameObject.layer != s.Layer)
                {
                    spr.gameObject.layer = s.Layer;
                }

                if (spr.Collection != coll || spr.spriteId != s.SpriteId)
                {
                    spr.SetSprite(coll, s.SpriteId);
                    NotSolid(spr.transform);
                }

                t.localPosition = s.Pos;
                t.localRotation = s.Rot;
                t.localScale = s.Scale;
                if (spr.scale != s.SpriteScale)
                {
                    spr.scale = s.SpriteScale;
                }

                spr.color = s.Color;
                var mr = spr.GetComponent<Renderer>();
                if (mr != null)
                {
                    mr.sortingOrder = s.SortingOrder;
                }
            }

            foreach (var kv in _parts)
            {
                if (kv.Value != null && !_seen.Contains(kv.Key)
                    && kv.Value.gameObject.activeSelf)
                {
                    kv.Value.gameObject.SetActive(false);
                }
            }
        }

        /// <summary>Call every frame.</summary>
        public void Tick()
        {
            if (!_hasLast)
            {
                return;
            }

            EnsureRoot();
            Vector3 pos;
            Quaternion rot;
            PuppetBlend blend = PuppetRules.Blend(new BlendFacts
            {
                HasPrevious = _hasPrev,
                Previous = new Seconds(_prev.Time),
                Last = new Seconds(_last.Time),
                Now = new Seconds(Time.time),
            });

            if (blend.Blends)
            {
                // Render one snapshot interval behind: blend prev->last over
                // the time it took last to arrive.
                float t = blend.Fraction;
                pos = Vector3.Lerp(_prev.Position, _last.Position, t);
                rot = Quaternion.Slerp(_prev.Rotation, _last.Rotation, t);
            }
            else
            {
                pos = _last.Position;
                rot = _last.Rotation;
            }

            Root.transform.SetPositionAndRotation(pos, rot);
            // A spectator isn't seen, nor a player somewhere else: in a
            // separate location or a dream while we aren't in the same location
            // (or the other way round), where their coordinates are in an area
            // this game hasn't loaded, or is another place. A location stands
            // in the same place in every game (World.Places.Locations).
            bool shown = PuppetRules.Shown(
                _last.Spectating,
                World.Places.Whereabouts.SamePlace(_last.Away, _last.Place));
            if (Root.activeSelf != shown)
            {
                Root.SetActive(shown);
            }
        }

        // ---- a burning player's flames and fire sound (see PlayerFx) --------

        private Transform _flames;
        private AudioObject _fireSound;

        /// <summary>
        /// Show them burning, as the game's Burn does on a burning character:
        /// pooled flames that follow them and the fire loop. Only the look: the
        /// burning itself (its damage) happens in their own game.
        /// </summary>
        public void ShowBurning(string flames, string sound)
        {
            StopBurning();
            EnsureRoot();
            if (flames != "")
            {
                try
                {
                    _flames = Core.AddPooledPrefab(
                        "FX", flames, Root.transform.position,
                        Quaternion.identity, Root.transform);
                }
                catch (System.Exception e)
                {
                    Plugin.Log.LogWarning(
                        $"[fx] no flames \"{flames}\" here: {e.Message}");
                }
            }

            if (sound != "")
            {
                _fireSound = AudioController.Play(sound, Root.transform);
            }
        }

        public void StopBurning()
        {
            if (_flames != null)
            {
                Core.stopAllParticlesInChildren(_flames, includeInactive: true);
            }

            if (_fireSound != null)
            {
                _fireSound.Stop();
            }

            _fireSound = null;
        }

        public void Destroy()
        {
            StopBurning();
            // The flames belong to the game's pool: take them off the puppet
            // before it goes, so the pool keeps them.
            if (_flames != null)
            {
                _flames.SetParent(null, true);
            }

            _flames = null;
            RemoveHeld();
            RemoveLantern();
            if (Root != null)
            {
                Object.Destroy(Root);
            }

            Root = null;
            _parts.Clear();
        }
    }
}
