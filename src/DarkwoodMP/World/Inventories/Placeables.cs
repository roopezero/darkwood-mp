using System.Collections.Generic;
using UnityEngine;

namespace DarkwoodMP.World.Inventories
{
    /// <summary>
    /// What a player can place, by prefab: each item that canBePlaced
    /// (InvItem.item, which Player.progressBarCompleted makes with the
    /// Object overload of Core.AddPrefab), with its path under
    /// Resources/Prefabs ("Traps/beartrap"), which is what ObjSpawn names.
    /// Built once from the item database, the first time it's asked once
    /// the database is loaded. It lives as long as the game
    /// (ModHost.Placeables): the database doesn't change.
    /// </summary>
    internal sealed class Placeables
    {
        private Dictionary<Object, string> _paths;
        private HashSet<string> _all;
        /// <summary>
        /// The item types that place each path (more than one item may place
        /// the same trap).
        /// </summary>
        private Dictionary<string, HashSet<string>> _types;

        /// <summary>
        /// The path (under Resources/Prefabs) of a prefab a player can place,
        /// or null.
        /// </summary>
        public string PathOf(Object prefab)
        {
            Ensure();
            if (prefab != null && _paths != null
                && _paths.TryGetValue(prefab, out var path))
            {
                return path;
            }

            return null;
        }

        /// <summary>
        /// Whether a player can place the prefab at this path (under
        /// Resources/Prefabs).
        /// </summary>
        public bool Has(string path)
        {
            Ensure();
            return _all != null && _all.Contains(path);
        }

        /// <summary>
        /// The item types a player places the prefab at this path with, or
        /// null.
        /// </summary>
        public HashSet<string> TypesOf(string path)
        {
            Ensure();
            if (_types != null && _types.TryGetValue(path, out var types))
            {
                return types;
            }

            return null;
        }

        private void Ensure()
        {
            if (_paths != null)
            {
                return;
            }

            var db = Singleton<ItemsDatabase>.Instance;
            if (db == null || db.itemsDict == null || db.itemsDict.Count == 0)
            {
                return;
            }

            _paths = new Dictionary<Object, string>();
            _all = new HashSet<string>();
            _types = new Dictionary<string, HashSet<string>>();
            foreach (var kv in db.itemsDict)
            {
                GameObject gameObject = Resources.Load(kv.Value) as GameObject;
                InvItem item = null;
                if (gameObject != null)
                {
                    item = gameObject.GetComponent<InvItem>();
                }

                if (item == null || !item.canBePlaced || item.item == null)
                {
                    continue;
                }

                string path = "Traps/" + item.item.name;
                if (Resources.Load("Prefabs/" + path) != item.item)
                {
                    Plugin.Log.LogWarning(
                        $"Placeable {kv.Key}: its prefab {item.item.name} "
                            + "isn't in Prefabs/Traps; placing it won't be "
                            + "shared");
                    continue;
                }

                _paths[item.item] = path;
                _all.Add(path);
                if (!_types.TryGetValue(path, out var types))
                {
                    _types[path] = types = new HashSet<string>();
                }

                types.Add(kv.Key);
            }

            Plugin.Log.LogInfo(
                $"Placeable things: {string.Join(", ", _all)}");
        }
    }
}
