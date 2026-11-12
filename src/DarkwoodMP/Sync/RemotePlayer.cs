using System.Collections.Generic;
using DarkwoodMP.Net;
using UnityEngine;

namespace DarkwoodMP.Sync
{
    /// <summary>
    /// Visual puppet of another player: a root GameObject with one flat child per mirrored sprite.
    /// Deliberately NOT a Player/CharBase: those are singletons wired into input, UI and saving.
    /// Root motion is snapshot-interpolated; sprite frames snap to the latest snapshot.
    /// </summary>
    public sealed class RemotePlayer
    {
        public readonly ulong Owner;
        public string Name;
        public GameObject Root { get; private set; }
        public bool Alive => !_hasLast || _last.Alive;
        public bool Spectating => _hasLast && _last.Spectating;
        /// <summary>In a separate location or a dream.</summary>
        public bool Away => _hasLast && _last.Away;
        /// <summary>The latest snapshot (its state bits: invisible, wards, forest skills), or null before the first.</summary>
        public PlayerSnapshot Last => _hasLast ? _last : null;

        private readonly PlayerSnapshot _prev = new PlayerSnapshot();
        private readonly PlayerSnapshot _last = new PlayerSnapshot();
        private bool _hasPrev, _hasLast;
        /// <summary>Running covers about 500 a second; this is more than half a second of it between two updates.</summary>
        private const float JumpDistance = 300f;
        private readonly Dictionary<int, tk2dSprite> _parts = new Dictionary<int, tk2dSprite>();
        private readonly HashSet<int> _seen = new HashSet<int>();

        public RemotePlayer(ulong owner, string name)
        {
            Owner = owner;
            Name = name;
        }

        /// <summary>A snapshot said they're no longer alive, after one that said they were.</summary>
        public event System.Action<RemotePlayer> OnDied;

        public void Push(PlayerSnapshot snap)
        {
            if (_hasLast && _last.Alive && !snap.Alive) OnDied?.Invoke(this);
            // A first position, or a jump further than anyone walks between two updates (placed on joining, a
            // teleport), is shown at once rather than slid across, through whoever stands between.
            bool jump = _hasLast && new Vector2(snap.Position.x - _last.Position.x, snap.Position.z - _last.Position.z).magnitude > JumpDistance;
            if (_hasLast && !jump) { _prev.CopyFrom(_last); _hasPrev = true; }
            else _hasPrev = false;
            _last.CopyFrom(snap);
            _last.Time = Time.time;
            _hasLast = true;
            ApplySprites();
            ApplyLights();
        }

        // ---- what lights them: a torch or lamp in hand (its light and flames), the flashlight's beam ----------

        private string _heldShown = "";
        private Transform _heldLight, _heldFlames;
        private GameObject _beam;
        private bool _flamesLogged;

        private void ApplyLights()
        {
            if (Root == null) return;
            string want = _last.Alive && !_last.Spectating ? _last.HeldLight ?? "" : "";
            if (want != _heldShown || (want != "" && _heldLight == null))
            {
                RemoveHeld();
                _heldShown = want;
                if (want != "")
                {
                    try
                    {
                        var item = Singleton<ItemsDatabase>.Instance?.getItem(want, instantiate: false);
                        if (item != null && item.lightEmitter != null)
                        {
                            // As the game does for its own player (Player.onDoneSwitchingItem), on the puppet.
                            _heldLight = Core.AddPrefab(item.lightEmitter, Vector3.zero, Quaternion.Euler(90f, 0f, 0f), Root).transform;
                            if (item._particleEmitter != null) _heldFlames = Core.AddPrefab(item._particleEmitter, Vector3.zero, Quaternion.Euler(90f, 0f, 0f), Root).transform;
                            NotSolid(_heldLight); NotSolid(_heldFlames);
                        }
                    }
                    catch (System.Exception e) { Plugin.Log.LogWarning($"Couldn't show {Name}'s {want}: {e.Message}"); }
                }
            }
            // The flames go where their game put them: on the torch in hand, which moves with each animation frame and
            // facing (Player.ProcessAnims). The light itself stays at the player's centre, as the game leaves it.
            if (_heldFlames != null)
            {
                _heldFlames.localPosition = _last.HeldFlames;
                if (!_flamesLogged && _last.HeldFlames != Vector3.zero)
                {
                    _flamesLogged = true;
                    Plugin.Log.LogInfo($"[fx] {Name}'s {_heldShown}: flames on the puppet at {_heldFlames.localPosition} from its centre (on the item in hand)");
                }
            }
            bool beam = _last.Flashlight && _last.Alive && !_last.Spectating;
            if (beam && _beam == null)
            {
                var own = Player.Instance != null ? Player.Instance.transform.Find("Flashlight") : null;
                if (own != null)
                {
                    _beam = Object.Instantiate(own.gameObject, Root.transform, false);
                    _beam.name = "Flashlight";
                    NotSolid(_beam.transform);
                }
            }
            if (_beam != null && _beam.activeSelf != beam) _beam.SetActive(beam);
            ApplyLantern();
        }

        // A lantern worn on the hotbar: in their game it widens Player.lightDot and makes it a logic light
        // (InvItemClass.checkForActiveSwitches). Here a copy of our own player's light dot on the puppet does the same:
        // it's drawn, and it lights the path nodes around them (Controller.logicLights), so the host's AI and
        // Core.isInLight see the joiner's proxy, which stands where the puppet is, as lit.
        private Light2D _lantern;

        /// <summary>The radius of the lantern light shown on the puppet (0: none).</summary>
        public float LanternShown => _lantern != null ? _lantern.LightRadius : 0f;

        private void ApplyLantern()
        {
            float want = _last.Alive && !_last.Spectating && !_last.Away ? _last.Lantern : 0f;
            if (want <= 0f) { RemoveLantern(); return; }
            var ctl = Singleton<Controller>.Instance;
            if (_lantern == null)
            {
                var own = Player.Instance != null ? Player.Instance.lightDot : null;
                if (own == null || ctl == null) return;
                var go = Object.Instantiate(own.gameObject, Root.transform, false);
                go.name = "PuppetLanternDot";
                go.transform.localPosition = own.transform.localPosition;
                go.SetActive(true);
                NotSolid(go.transform);
                _lantern = go.GetComponent<Light2D>();
                if (_lantern == null) { Object.Destroy(go); return; }
                Plugin.Log.LogInfo($"[fx] {Name}'s lantern: a light of radius {want:0} on their puppet");
            }
            if (_lantern.LightRadius != want) _lantern.LightRadius = want;
            _lantern.lightsPlayer = true;
            _lantern.updateGraph = true;
            if (ctl != null && !ctl.logicLights.Contains(_lantern)) ctl.logicLights.Add(_lantern);
        }

        private void RemoveLantern()
        {
            if (_lantern == null) return;
            _lantern.unlightGraphNodes();
            var ctl = Singleton<Controller>.Instance;
            if (ctl != null) while (ctl.logicLights.Remove(_lantern)) { }
            Object.Destroy(_lantern.gameObject);
            _lantern = null;
        }

        private void RemoveHeld()
        {
            if (_heldLight != null)
            {
                var light = _heldLight.GetComponent<Light2D>();
                if (light != null) { light.unlightGraphNodes(); Singleton<Controller>.Instance?.logicLights.Remove(light); }
                Object.Destroy(_heldLight.gameObject);
            }
            if (_heldFlames != null) Object.Destroy(_heldFlames.gameObject);
            _heldLight = _heldFlames = null;
            _heldShown = "";
        }

        /// <summary>
        /// A puppet is only seen: a sprite whose collection has collider data, or a copied light, may bring a solid
        /// collider, and a puppet moved to where its player is would shove the furniture and dropped items it walks
        /// through in this game only. Their own game decides where those end up (DragSync), so it's turned off.
        /// </summary>
        private static void NotSolid(Transform t)
        {
            if (t == null) return;
            foreach (var c in t.GetComponentsInChildren<Collider>(true)) if (c != null && !c.isTrigger) c.enabled = false;
        }

        private void EnsureRoot()
        {
            if (Root != null) return; // Unity null: also true after scene unload destroyed it
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
                var coll = SpriteCollections.Get(s.CollectionHash, _last.Library);
                if (coll == null) continue;
                if (!_parts.TryGetValue(s.PathHash, out var spr) || spr == null)
                {
                    var go = new GameObject($"part_{s.PathHash:X8}");
                    go.transform.SetParent(Root.transform, false);
                    spr = tk2dBaseSprite.AddComponent<tk2dSprite>(go, coll, s.SpriteId);
                    _parts[s.PathHash] = spr;
                    NotSolid(spr.transform);
                }
                var t = spr.transform;
                if (!spr.gameObject.activeSelf) spr.gameObject.SetActive(true);
                if (spr.gameObject.layer != s.Layer) spr.gameObject.layer = s.Layer;
                if (spr.Collection != coll || spr.spriteId != s.SpriteId) { spr.SetSprite(coll, s.SpriteId); NotSolid(spr.transform); }
                t.localPosition = s.Pos;
                t.localRotation = s.Rot;
                t.localScale = s.Scale;
                if (spr.scale != s.SpriteScale) spr.scale = s.SpriteScale;
                spr.color = s.Color;
                var mr = spr.GetComponent<Renderer>();
                if (mr != null) mr.sortingOrder = s.SortingOrder;
            }
            foreach (var kv in _parts)
                if (kv.Value != null && !_seen.Contains(kv.Key) && kv.Value.gameObject.activeSelf)
                    kv.Value.gameObject.SetActive(false);
        }

        /// <summary>Call every frame.</summary>
        public void Tick()
        {
            if (!_hasLast) return;
            EnsureRoot();
            Vector3 pos; Quaternion rot;
            if (_hasPrev && _last.Time > _prev.Time)
            {
                // Render one snapshot interval behind: blend prev->last over the time it took last to arrive.
                float t = Mathf.Clamp01((Time.time - _last.Time) / (_last.Time - _prev.Time));
                pos = Vector3.Lerp(_prev.Position, _last.Position, t);
                rot = Quaternion.Slerp(_prev.Rotation, _last.Rotation, t);
            }
            else
            {
                pos = _last.Position; rot = _last.Rotation;
            }
            Root.transform.SetPositionAndRotation(pos, rot);
            // A spectator isn't seen, nor a player somewhere else: in a separate location or a dream while we aren't
            // in the same location (or the other way round), where their coordinates are in an area this game hasn't
            // loaded, or is another place. A location stands in the same place in every game (World.Locations).
            bool shown = !_last.Spectating && World.Locations.SamePlace(_last.Away, _last.Place);
            if (Root.activeSelf != shown) Root.SetActive(shown);
        }

        // ---- a burning player's flames and fire sound (see PlayerFx) ----------------------------------------

        private Transform _flames;
        private AudioObject _fireSound;

        /// <summary>
        /// Show them burning, as the game's Burn does on a burning character: pooled flames that follow them and
        /// the fire loop. Only the look: the burning itself (its damage) happens in their own game.
        /// </summary>
        public void ShowBurning(string flames, string sound)
        {
            StopBurning();
            EnsureRoot();
            if (flames != "")
            {
                try { _flames = Core.AddPooledPrefab("FX", flames, Root.transform.position, Quaternion.identity, Root.transform); }
                catch (System.Exception e) { Plugin.Log.LogWarning($"[fx] no flames \"{flames}\" here: {e.Message}"); }
            }
            if (sound != "") _fireSound = AudioController.Play(sound, Root.transform);
        }

        public void StopBurning()
        {
            if (_flames != null) Core.stopAllParticlesInChildren(_flames, includeInactive: true);
            if (_fireSound != null) _fireSound.Stop();
            _fireSound = null;
        }

        public void Destroy()
        {
            StopBurning();
            // The flames belong to the game's pool: take them off the puppet before it goes, so the pool keeps them.
            if (_flames != null) _flames.SetParent(null, true);
            _flames = null;
            RemoveHeld();
            RemoveLantern();
            if (Root != null) Object.Destroy(Root);
            Root = null;
            _parts.Clear();
        }
    }

    /// <summary>Lookup of loaded tk2d sprite collections by stable name hash.</summary>
    public static class SpriteCollections
    {
        private static readonly Dictionary<int, tk2dSpriteCollectionData> _byHash = new Dictionary<int, tk2dSpriteCollectionData>();
        private static float _lastRescan = -999f;

        private static readonly HashSet<string> _loadedLibraries = new HashSet<string>();

        /// <summary>
        /// The collection with this name hash. One that isn't loaded here may belong to the animation library the
        /// other player's body uses (their weapon, a torch in hand): that's loaded once, and looked for again.
        /// </summary>
        public static tk2dSpriteCollectionData Get(int hash, string library)
        {
            var c = Get(hash);
            if (c != null || string.IsNullOrEmpty(library) || !_loadedLibraries.Add(library)) return c;
            var lib = Resources.Load(library, typeof(tk2dSpriteAnimation)) as tk2dSpriteAnimation;
            Plugin.Log.LogInfo(lib != null ? $"Loaded animation library {library} for another player's look" : $"No animation library {library} here");
            if (lib == null) return null;
            Keep.Add(lib); // held, so its sprites stay loaded while it's in use
            _lastRescan = -999f;
            return Get(hash);
        }

        private static readonly List<Object> Keep = new List<Object>();

        public static tk2dSpriteCollectionData Get(int hash)
        {
            if (_byHash.TryGetValue(hash, out var c) && c != null) return c;
            // Collections load with scenes; rescan at most twice a second on a miss.
            if (Time.unscaledTime - _lastRescan > 0.5f)
            {
                _lastRescan = Time.unscaledTime;
                _byHash.Clear();
                foreach (var col in Resources.FindObjectsOfTypeAll<tk2dSpriteCollectionData>())
                {
                    if (string.IsNullOrEmpty(col.spriteCollectionName)) continue;
                    // A collection prefab may have a platform-specific inst; prefer the inst.
                    var inst = col.inst != null ? col.inst : col;
                    _byHash[StableHash.Of(col.spriteCollectionName)] = inst;
                }
                if (_byHash.TryGetValue(hash, out c)) return c;
            }
            return null;
        }
    }
}
