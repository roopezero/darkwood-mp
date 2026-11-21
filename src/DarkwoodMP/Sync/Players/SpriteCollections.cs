using System.Collections.Generic;
using DarkwoodMP.Net;
using UnityEngine;

namespace DarkwoodMP.Sync.Players
{
    /// <summary>
    /// Lookup of loaded tk2d sprite collections by stable name hash.
    /// </summary>
    public sealed class SpriteCollections
    {
        private readonly Dictionary<int, tk2dSpriteCollectionData>
            _byHash = new Dictionary<int, tk2dSpriteCollectionData>();
        private Seconds _lastRescan =
            new Seconds(SpriteCacheRules.NeverScanned);

        private readonly HashSet<string> _loadedLibraries =
            new HashSet<string>();

        /// <summary>
        /// The collection with this name hash. One that isn't loaded here may
        /// belong to the animation library the other player's body uses (their
        /// weapon, a torch in hand): that's loaded once, and looked for again.
        /// </summary>
        public tk2dSpriteCollectionData Get(int hash, string library)
        {
            var c = Get(hash);
            bool tried = library != null && _loadedLibraries.Contains(library);
            if (!SpriteCacheRules.LoadsLibrary(c != null, library, tried))
            {
                return c;
            }

            _loadedLibraries.Add(library);

            var lib =
                Resources.Load(library, typeof(tk2dSpriteAnimation))
                    as tk2dSpriteAnimation;
            string data;
            if (lib != null)
            {
                data =
                    $"Loaded animation library {library} for another player's "
                        + "look";
            }
            else
            {
                data = $"No animation library {library} here";
            }

            Plugin.Log.LogInfo(data);
            if (lib == null)
            {
                return null;
            }

            // held, so its sprites stay loaded while it's in use
            _keep.Add(lib);
            _lastRescan = new Seconds(SpriteCacheRules.NeverScanned);
            return Get(hash);
        }

        private readonly List<Object> _keep = new List<Object>();

        public tk2dSpriteCollectionData Get(int hash)
        {
            if (_byHash.TryGetValue(hash, out var c) && c != null)
            {
                return c;
            }

            // Collections load with scenes; rescan at most twice a second on a
            // miss.
            var now = new Seconds(Time.unscaledTime);
            if (SpriteCacheRules.Rescans(now, _lastRescan))
            {
                _lastRescan = now;
                _byHash.Clear();
                foreach (var col in Resources
                    .FindObjectsOfTypeAll<tk2dSpriteCollectionData>())
                {
                    if (string.IsNullOrEmpty(col.spriteCollectionName))
                    {
                        continue;
                    }

                    // A collection prefab may have a platform-specific inst;
                    // prefer the inst.
                    tk2dSpriteCollectionData inst;
                    if (col.inst != null)
                    {
                        inst = col.inst;
                    }
                    else
                    {
                        inst = col;
                    }

                    _byHash[StableHash.Of(col.spriteCollectionName)] = inst;
                }

                if (_byHash.TryGetValue(hash, out c))
                {
                    return c;
                }
            }

            return null;
        }
    }
}
