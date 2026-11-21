using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMP.Sync.Players
{
    /// <summary>
    /// Other players on the map. The game's map (Map.open) has no marker for
    /// the player: it puts an icon for each known place under Map.iconHolder,
    /// at the place's world position divided by the map type's scale, and tints
    /// the place the player is in red; Map.close destroys the icons. The mod
    /// adds the game's "generic_marker" icon the same way for each other player
    /// in the main world, tinted blue, moves it while the map is open, and
    /// draws their name next to it. Only on the world's map, and only for
    /// players in the main world: a player in a separate location or a dream is
    /// somewhere the map doesn't show.
    /// </summary>
    public sealed class MapMarkers
    {
        private readonly PlayerSync _sync;

        public MapMarkers(PlayerSync sync)
        {
            _sync = sync;
        }

        private readonly Dictionary<ulong, Transform> _markers =
            new Dictionary<ulong, Transform>();
        private static readonly
            AccessTools.FieldRef<Map, Map.Type> CurrentType =
                AccessTools.FieldRefAccess<Map, Map.Type>("currentType");
        private static readonly Color Tint = new Color(0.35f, 0.75f, 1f);

        private static Map.Type WorldType(Map map)
        {
            if (map == null)
            {
                return null;
            }

            Map.Type t = null;
            string name = null;
            if (map.opened)
            {
                t = CurrentType(map);
            }

            if (t != null)
            {
                name = t.name;
            }

            if (MapMarkerRules.OnWorldMap(map.opened, name))
            {
                return t;
            }

            return null;
        }

        /// <summary>
        /// Whether this player is shown on the world's map: in the main world,
        /// and not spectating.
        /// </summary>
        private static bool OnMap(RemotePlayer r) =>
            MapMarkerRules.OnMap(FactsOf(r));

        private static MarkerFacts FactsOf(RemotePlayer r)
        {
            return new MarkerFacts
            {
                Dummy = r.Owner == PlayerSync.DummyId,
                HasRoot = r.Root != null,
                Away = r.Away,
                Spectating = r.Spectating,
            };
        }

        /// <summary>
        /// After the map opens: an icon for each other player in the main
        /// world.
        /// </summary>
        public void Opened(Map map)
        {
            _markers.Clear();
            var type = WorldType(map);
            if (type == null || map.iconHolder == null)
            {
                return;
            }

            foreach (var r in _sync.Remotes.Values)
            {
                if (!MapMarkerRules.Marks(FactsOf(r)))
                {
                    continue;
                }

                try
                {
                    var go = Core.AddPrefab(
                        "UI/UIMapElement", Vector3.zero,
                        Quaternion.Euler(90f, 0f, 0f), map.iconHolder);
                    var spr = go.GetComponent<tk2dSprite>();
                    if (spr != null)
                    {
                        spr.SetSprite("generic_marker");
                        spr.color = Tint;
                    }

                    go.transform.localScale = Vector3.one;
                    var col = go.GetComponent<Collider>();
                    if (col != null)
                    {
                        col.enabled = false; // nothing to show when hovered
                    }

                    _markers[r.Owner] = go.transform;
                    Place(type, go.transform, r.Root.transform.position);
                }
                catch (System.Exception e)
                {
                    Plugin.Log.LogWarning(
                        $"No map marker for {r.Name}: {e.Message}");
                }
            }

            if (_markers.Count > 0)
            {
                Plugin.Log.LogInfo(
                    $"Map: marked {_markers.Count} other player(s)");
            }
        }

        // As Map.open places an icon: local position = world position / scale,
        // raised above the background, then the type's icon offset on screen.
        private static void Place(Map.Type type, Transform t, Vector3 world)
        {
            Vec3 spot = MapMarkerRules.LocalSpot(
                new Vec3(world.x, world.y, world.z), type.scale);
            t.localPosition = new Vector3(spot.X, spot.Y, spot.Z);
            Vec3 shift = MapMarkerRules.IconShift(
                new Vec2(type.iconOffset.x, type.iconOffset.y),
                new Vec2(
                    Core.ResolutionWidthModifier,
                    Core.ResolutionHeightModifier));
            t.position += new Vector3(shift.X, shift.Y, shift.Z);
        }

        /// <summary>
        /// Each frame while the map is open: move the markers with their
        /// players.
        /// </summary>
        public void Tick()
        {
            if (_markers.Count == 0)
            {
                return;
            }

            var type = WorldType(Singleton<Map>.Instance);
            if (type == null)
            {
                _markers.Clear();
                return;
            }

            foreach (var kv in _markers)
            {
                if (kv.Value == null)
                {
                    continue;
                }

                bool there =
                    _sync.Remotes.TryGetValue(kv.Key, out var r) && OnMap(r);
                if (kv.Value.gameObject.activeSelf != there)
                {
                    kv.Value.gameObject.SetActive(there);
                }

                if (there)
                {
                    Place(type, kv.Value, r.Root.transform.position);
                }
            }
        }

        /// <summary>
        /// True while the world's map is open (name tags above puppets aren't
        /// drawn over it).
        /// </summary>
        public static bool MapOpen =>
            Singleton<Map>.Instance != null && Singleton<Map>.Instance.opened;

        /// <summary>
        /// IMGUI: each marked player's name beside their marker.
        /// </summary>
        public void DrawNames()
        {
            if (_markers.Count == 0 || Core.CamUI == null
                || Plugin.NameTagStyle == null)
            {
                return;
            }

            var cam = Core.CamUI.GetComponent<Camera>();
            if (cam == null)
            {
                return;
            }

            foreach (var kv in _markers)
            {
                if (kv.Value == null || !kv.Value.gameObject.activeInHierarchy)
                {
                    continue;
                }

                if (!_sync.Remotes.TryGetValue(kv.Key, out var r))
                {
                    continue;
                }

                var sp = cam.WorldToScreenPoint(kv.Value.position);
                GUI.Label(
                    new Rect(sp.x - 100, Screen.height - sp.y - 34, 200, 22),
                    r.Name, Plugin.NameTagStyle);
            }
        }
    }
}
