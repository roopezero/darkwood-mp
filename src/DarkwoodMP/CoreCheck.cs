using System;
using System.IO;
using System.Reflection;

namespace DarkwoodMP
{
    /// Checks DarkwoodMP.Core.dll before anything loads it. Names no type
    /// from that assembly: it has to work when the file is missing.
    internal static class CoreCheck
    {
        private const string FileName = "DarkwoodMP.Core.dll";

        /// What's wrong with the install, or null if it's fine.
        public static string Problem(string pluginPath)
        {
            var dir = Path.GetDirectoryName(pluginPath);
            var path = Path.Combine(dir, FileName);
            if (!File.Exists(path))
            {
                return $"{FileName} is missing from {dir}";
            }
            Assembly core;
            try
            {
                // Loading this copy first also pins it: BepInEx's resolver
                // reuses a loaded assembly before it searches the folders.
                core = Assembly.LoadFile(path);
            }
            catch (Exception e)
            {
                return $"{FileName} can't be loaded: {e.Message}";
            }
            var mine = VersionOf(typeof(CoreCheck).Assembly);
            var theirs = VersionOf(core);
            if (theirs != mine)
            {
                return $"{FileName} is {theirs}, DarkwoodMP.dll is {mine}";
            }
            return null;
        }

        /// From the name, not GetName(): BepInEx notes the native call can
        /// fail on paths with unusual characters.
        private static Version VersionOf(Assembly assembly) =>
            new AssemblyName(assembly.FullName).Version;
    }
}
