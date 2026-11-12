using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMP.Sync
{
    /// <summary>
    /// Other players on the map. The game's map (Map.open) has no marker for the player: it puts an icon for each
    /// known place under Map.iconHolder, at the place's world position divided by the map type's scale, and tints the
    /// place the player is in red; Map.close destroys the icons. The mod adds the game's "generic_marker" icon the
    /// same way for each other player in the main world, tinted blue, moves it while the map is open, and draws their
    /// name next to it. Only on the world's map, and only for players in the main world: a player in a separate
    /// location or a dream is somewhere the map doesn't show.
    /// </summary>
    public static class MapMarkers
    {
        private static readonly Dictionary<ulong, Transform> Markers = new Dictionary<ulong, Transform>();
        private static readonly AccessTools.FieldRef<Map, Map.Type> CurrentType = AccessTools.FieldRefAccess<Map, Map.Type>("currentType");
        private static readonly Color Tint = new Color(0.35f, 0.75f, 1f);

        private static Map.Type WorldType(Map map)
        {
            if (map == null || !map.opened) return null;
            var t = CurrentType(map);
            return t != null && t.name == "World" ? t : null;
        }

        /// <summary>After the map opens: an icon for each other player in the main world.</summary>
        internal static void OnOpened(Map map)
        {
            Markers.Clear();
            var type = WorldType(map);
            var sync = PlayerSync.Instance;
            if (type == null || sync == null || map.iconHolder == null) return;
            foreach (var r in sync.Remotes.Values)
            {
                if (r.Owner == PlayerSync.DummyId || r.Root == null || r.Away || r.Spectating) continue;
                try
                {
                    var go = Core.AddPrefab("UI/UIMapElement", Vector3.zero, Quaternion.Euler(90f, 0f, 0f), map.iconHolder);
                    var spr = go.GetComponent<tk2dSprite>();
                    if (spr != null) { spr.SetSprite("generic_marker"); spr.color = Tint; }
                    go.transform.localScale = Vector3.one;
                    var col = go.GetComponent<Collider>();
                    if (col != null) col.enabled = false; // nothing to show when hovered
                    Markers[r.Owner] = go.transform;
                    Place(type, go.transform, r.Root.transform.position);
                }
                catch (System.Exception e) { Plugin.Log.LogWarning($"No map marker for {r.Name}: {e.Message}"); }
            }
            if (Markers.Count > 0) Plugin.Log.LogInfo($"Map: marked {Markers.Count} other player(s)");
        }

        // As Map.open places an icon: local position = world position / scale, raised above the background, then the
        // type's icon offset on screen.
        private static void Place(Map.Type type, Transform t, Vector3 world)
        {
            t.localPosition = world / type.scale + new Vector3(0f, 15f, 0f);
            t.position += new Vector3(type.iconOffset.x * Core.ResolutionWidthModifier, 0f, type.iconOffset.y * Core.ResolutionHeightModifier);
        }

        /// <summary>Each frame while the map is open: move the markers with their players.</summary>
        public static void Tick()
        {
            if (Markers.Count == 0) return;
            var type = WorldType(Singleton<Map>.Instance);
            var sync = PlayerSync.Instance;
            if (type == null || sync == null) { Markers.Clear(); return; }
            foreach (var kv in Markers)
            {
                if (kv.Value == null) continue;
                bool there = sync.Remotes.TryGetValue(kv.Key, out var r) && r.Root != null && !r.Away && !r.Spectating;
                if (kv.Value.gameObject.activeSelf != there) kv.Value.gameObject.SetActive(there);
                if (there) Place(type, kv.Value, r.Root.transform.position);
            }
        }

        /// <summary>True while the world's map is open (name tags above puppets aren't drawn over it).</summary>
        public static bool MapOpen => Singleton<Map>.Instance != null && Singleton<Map>.Instance.opened;

        /// <summary>IMGUI: each marked player's name beside their marker.</summary>
        public static void DrawNames()
        {
            if (Markers.Count == 0 || Core.CamUI == null || Plugin.NameTagStyle == null) return;
            var cam = Core.CamUI.GetComponent<Camera>();
            var sync = PlayerSync.Instance;
            if (cam == null || sync == null) return;
            foreach (var kv in Markers)
            {
                if (kv.Value == null || !kv.Value.gameObject.activeInHierarchy) continue;
                if (!sync.Remotes.TryGetValue(kv.Key, out var r)) continue;
                var sp = cam.WorldToScreenPoint(kv.Value.position);
                GUI.Label(new Rect(sp.x - 100, Screen.height - sp.y - 34, 200, 22), r.Name, Plugin.NameTagStyle);
            }
        }
    }

    [HarmonyPatch(typeof(Map), nameof(Map.open))]
    public static class MapOpenPatch
    {
        private static void Postfix(Map __instance) => MapMarkers.OnOpened(__instance);
    }
}
