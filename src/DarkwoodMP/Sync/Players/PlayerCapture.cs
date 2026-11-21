using System.Collections.Generic;
using DarkwoodMP.Net;
using UnityEngine;

namespace DarkwoodMP.Sync.Players
{
    /// <summary>Captures the local player's look into a snapshot.</summary>
    public sealed class PlayerCapture
    {
        private GameObject _cachedRoot;
        private GameObject _flashlight;
        private Player _flashlightOf;
        private tk2dBaseSprite[] _sprites;
        private int[] _pathHashes;
        private Seconds _nextRescan;
        private bool _dumped;

        public bool Capture(PlayerSnapshot snap)
        {
            var player = Player.Instance;
            if (player == null)
            {
                return false;
            }

            var root = player.transform;

            // Children (held items, effects) come and go; rescan periodically.
            var now = new Seconds(UnityEngine.Time.unscaledTime);
            if (PlayerLookRules.Rescans(
                _cachedRoot == player.gameObject, now, _nextRescan))
            {
                _cachedRoot = player.gameObject;
                var all = player.GetComponentsInChildren<tk2dBaseSprite>(true);
                var keep = new List<tk2dBaseSprite>();
                var hashes = new List<int>();
                bool dump = Plugin.LogPlayerSprites.Value && !_dumped;
                foreach (var s in all)
                {
                    string path = PathOf(s.transform, root);
                    bool ok = PlayerLookRules.IsCharacterArt(path);
                    if (dump)
                    {
                        string text = "skip";
                        if (ok)
                        {
                            text = "SYNC";
                        }

                        var log = Plugin.Log;
                        var layer = LayerMask.LayerToName(s.gameObject.layer);
                        var active = s.gameObject.activeInHierarchy;
                        var collection = s.Collection;
                        string collectionName = null;
                        if (collection != null)
                        {
                            collectionName = collection.spriteCollectionName;
                        }

                        var definition = s.CurrentSprite;
                        string spriteName = null;
                        if (definition != null)
                        {
                            spriteName = definition.name;
                        }

                        log.LogInfo(
                            $"[sprite] {text} path={path} layer={layer} " +
                                    $"active={active} coll={collectionName} "
                                + $"sprite={spriteName}");
                    }

                    if (!ok)
                    {
                        continue;
                    }

                    keep.Add(s);
                    hashes.Add(StableHash.Of(path));
                }

                _dumped |= dump;
                _sprites = keep.ToArray();
                _pathHashes = hashes.ToArray();
                _nextRescan = PlayerLookRules.NextRescan(now);
            }

            var invRot = Quaternion.Inverse(root.rotation);
            snap.Position = root.position;
            snap.Rotation = root.rotation;
            snap.Alive = PlayerLookRules.Alive(player.alive, player.dying);
            snap.Spectating = World.Spectator.Active;
            if (player.torsoAnimator != null
                && player.torsoAnimator.Library != null)
            {
                snap.Library = player.torsoAnimator.Library.name;
            }
            else
            {
                snap.Library = "";
            }

            snap.HeldLight = PlayerLookRules.HeldLight(HeldOf(player));
            var flames = player.itemParticleEmitter;
            Vec3 offset = default;
            if (flames != null)
            {
                offset = VecOf(invRot * (flames.position - root.position));
            }

            snap.HeldFlames = VectorOf(PlayerLookRules.Flames(
                snap.HeldLight, flames != null, offset));
            snap.Lantern = PlayerLookRules.Lantern(LanternOf(player));

            if (_flashlight == null || _flashlightOf != player)
            {
                _flashlightOf = player;
                Transform transform = root.Find("Flashlight");
                if (transform != null)
                {
                    _flashlight = transform.gameObject;
                }
                else
                {
                    _flashlight = null;
                }
            }

            snap.Flashlight = _flashlight != null && _flashlight.activeSelf;
            BeamFacts beam = BeamOf(snap.Flashlight, _flashlight);
            snap.BeamRadius = PlayerLookRules.BeamRadius(beam);
            snap.BeamBright = PlayerLookRules.BeamGrey(beam);

            snap.Invisible = player.invisible;
            var fx = player.effects;
            snap.ShadowWard =
                fx != null && fx.hasEffectType(CharacterEffectType.shadowWard);
            snap.ForestSpiritWard = fx != null
                && fx.hasEffectType(CharacterEffectType.forestSpiritWard);
            snap.FriendOfTheForest =
                player.skills != null && player.skills.FriendOfTheForest;
            snap.EnemyOfTheForest =
                player.skills != null && player.skills.EnemyOfTheForest;
            snap.Away = LocalAway();
            snap.AfterNight = World.Mornings.AfterNight.Local;
            snap.Place = World.Places.Whereabouts.Current();
            snap.Health = PlayerLookRules.HealthPercent(
                player.health, player.maxHealth);
            snap.Sprites.Clear();
            for (int i = 0; i < _sprites.Length
                && snap.Sprites.Count < PlayerLookRules.MaxSprites; i++)
            {
                var s = _sprites[i];
                Renderer mr = null;
                if (s != null)
                {
                    mr = s.GetComponent<Renderer>();
                }

                if (!PlayerLookRules.SendsSprite(SpriteOf(s, mr)))
                {
                    continue;
                }

                var t = s.transform;
                snap.Sprites.Add(new PlayerSnapshot.SpriteState
                {
                    PathHash = _pathHashes[i],
                    CollectionHash = StableHash.Of(
                        s.Collection.spriteCollectionName),
                    SpriteId = s.spriteId,
                    SortingOrder = mr.sortingOrder,
                    Layer = s.gameObject.layer,
                    Pos = invRot * (t.position - root.position),
                    Rot = invRot * t.rotation,
                    Scale = t.lossyScale,
                    SpriteScale = s.scale,
                    Color = s.color,
                });
            }

            return true;
        }

        /// <summary>
        /// This game's player is in a separate location or a dream.
        /// </summary>
        public static bool LocalAway()
        {
            var o = Singleton<OutsideLocations>.Instance;
            var d = Singleton<Dreams>.Instance;
            return PlayerLookRules.Away(
                o != null && o.playerInOutsideLocation,
                d != null && d.dreaming);
        }

        private static HeldFacts HeldOf(Player player)
        {
            var held = player.currentItem;
            bool valid = !InvItemClass.isNull(held);
            bool hasBase = valid && held.baseClass != null;
            string type = null;
            if (hasBase)
            {
                type = held.type;
            }

            return new HeldFacts
            {
                HasLightEmitter = player.itemLightEmitter != null,
                HeldValid = valid,
                HasBaseClass = hasBase,
                HeldType = type,
            };
        }

        private static LanternFacts LanternOf(Player player)
        {
            var dot = player.lightDot;
            var ctl = Singleton<Controller>.Instance;
            if (dot == null)
            {
                return default;
            }

            return new LanternFacts
            {
                HasDot = true,
                LightsPlayer = dot.lightsPlayer,
                HasController = ctl != null,
                IsLogicLight = ctl != null && ctl.logicLights.Contains(dot),
                Radius = dot.LightRadius,
            };
        }

        private static BeamFacts BeamOf(bool on, GameObject flashlight)
        {
            Light2D light = null;
            if (on)
            {
                light = flashlight.GetComponent<Light2D>();
            }

            if (light == null)
            {
                return new BeamFacts { On = on };
            }

            return new BeamFacts
            {
                On = on,
                HasLight = true,
                Radius = light.LightRadius,
                Grey = light.LightColor.r,
            };
        }

        private static SpriteFacts SpriteOf(tk2dBaseSprite s, Renderer mr)
        {
            if (s == null)
            {
                return default;
            }

            return new SpriteFacts
            {
                Exists = true,
                Active = s.gameObject.activeInHierarchy,
                HasCollection = s.Collection != null,
                HasRenderer = mr != null,
                RendererEnabled = mr != null && mr.enabled,
            };
        }

        private static Vec3 VecOf(Vector3 v) => new Vec3(v.x, v.y, v.z);

        private static Vector3 VectorOf(Vec3 v) => new Vector3(v.X, v.Y, v.Z);

        private static string PathOf(Transform t, Transform root)
        {
            if (t == root)
            {
                return ".";
            }

            string p = t.name;
            for (var c = t.parent; c != null && c != root; c = c.parent)
            {
                p = c.name + "/" + p;
            }

            return p;
        }
    }
}
