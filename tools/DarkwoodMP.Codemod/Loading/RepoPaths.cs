namespace DarkwoodMP.Codemod.Loading;

/// Where the tool finds the repo's files. It runs from anywhere inside the
/// repo, a worktree included: the root is the nearest folder above the current
/// one that holds src/GameReferences.props.
sealed class RepoPaths
{
    public readonly string Root;

    RepoPaths(string root)
    {
        Root = root;
    }

    public string PluginSource => Path.Combine(Root, "src/DarkwoodMP");

    public string GameReferences =>
        Path.Combine(Root, "src/GameReferences.props");

    public string BuildProps => Path.Combine(Root, "Directory.Build.props");

    /// The core DLL the plugin's own build copies next to the plugin. The
    /// compilation needs it as a reference, so the plugin must be built first.
    public string CoreDll =>
        Path.Combine(Root, "src/DarkwoodMP/bin/Release/DarkwoodMP.Core.dll");

    /// The same default as src/GameReferences.props. The environment variable
    /// GameDir overrides it, as -p:GameDir=... does for the real build.
    public static string GameDir
    {
        get
        {
            var over = Environment.GetEnvironmentVariable("GameDir");
            if (!string.IsNullOrEmpty(over))
            {
                return over;
            }
            var home = Environment.GetEnvironmentVariable("HOME");
            return home + "/.local/share/Steam/steamapps/common/Darkwood";
        }
    }

    /// The .NET Framework 4.7.2 reference pack in the NuGet package folder.
    public static string ReferencePack
    {
        get
        {
            var packages = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
            if (string.IsNullOrEmpty(packages))
            {
                var home = Environment.GetEnvironmentVariable("HOME");
                packages = home + "/.nuget/packages";
            }
            return Path.Combine(
                packages,
                "microsoft.netframework.referenceassemblies.net472/1.0.3",
                "build/.NETFramework/v4.7.2");
        }
    }

    public static RepoPaths Find()
    {
        var dir = Directory.GetCurrentDirectory();
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir, "src/GameReferences.props")))
            {
                return new RepoPaths(dir);
            }
            dir = Path.GetDirectoryName(dir);
        }
        throw new Stop(
            "Run this inside the repo: no folder above "
                + Directory.GetCurrentDirectory()
                + " holds src/GameReferences.props.");
    }
}
