using System;
using System.Collections.Generic;
using System.IO;

namespace DarkwoodMP.World.Places
{
    /// The locations of the build (the plugin's Locations._real): the
    /// game loads each as a scene of its name, and all of them, dreams
    /// too, are scenes under Assets/Locations/.
    public sealed class LocationNames
    {
        /// What a location's scene path holds.
        public const string Folder = "/Locations/";

        readonly HashSet<string> _names =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// From the build's scene paths
        /// (SceneUtility.GetScenePathByBuildIndex); null paths skipped.
        public LocationNames(IEnumerable<string> paths)
        {
            foreach (var path in paths)
            {
                if (path != null && path.Replace('\\', '/').Contains(Folder))
                {
                    _names.Add(Path.GetFileNameWithoutExtension(path));
                }
            }
        }

        /// How many were found, for the log.
        public int Count => _names.Count;

        /// Whether a location of this name exists, ignoring case. With
        /// none found every name does: the build lists them differently.
        public bool IsReal(string name)
        {
            return _names.Count == 0 || _names.Contains(name);
        }
    }
}
