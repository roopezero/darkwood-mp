using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;

namespace DarkwoodMP.Codemod.Loading;

/// The references the real build passes to the compiler, and no others. The
/// list was read from the csc command line of a real build (27 references):
/// the framework assemblies the SDK adds by default, netstandard (the core
/// targets netstandard2.0), the game's DLLs from src/GameReferences.props and
/// the core DLL.
static class ReferenceList
{
    static readonly string[] Framework =
    {
        "mscorlib",
        "System",
        "System.Core",
        "System.Data",
        "System.Drawing",
        "System.IO.Compression.FileSystem",
        "System.Numerics",
        "System.Runtime.Serialization",
        "System.Xml",
        "System.Xml.Linq",
    };

    const string HintPath =
        @"<HintPath>\$\((Managed|BepCore)\)/([^<]+)</HintPath>";

    public static List<MetadataReference> Read(RepoPaths paths)
    {
        var files = new List<string>();
        var pack = RepoPaths.ReferencePack;
        foreach (var name in Framework)
        {
            files.Add(Path.Combine(pack, name + ".dll"));
        }
        files.Add(Path.Combine(pack, "Facades/netstandard.dll"));
        files.AddRange(GameFiles(paths));
        files.Add(CoreDll(paths));

        var references = new List<MetadataReference>();
        foreach (var file in files)
        {
            if (!File.Exists(file))
            {
                throw new Stop(
                    "A reference of the real build is missing: " + file
                        + "\nRestore the plugin's packages and check that the "
                        + "game is installed (or set GameDir).");
            }
            references.Add(MetadataReference.CreateFromFile(file));
        }
        return references;
    }

    /// Every HintPath in src/GameReferences.props, the same list the build uses.
    static List<string> GameFiles(RepoPaths paths)
    {
        var managed = Path.Combine(RepoPaths.GameDir, "Darkwood_Data/Managed");
        var bepCore = Path.Combine(RepoPaths.GameDir, "BepInEx/core");
        var props = File.ReadAllText(paths.GameReferences);
        var files = new List<string>();
        foreach (Match match in Regex.Matches(props, HintPath))
        {
            var folder = bepCore;
            if (match.Groups[1].Value == "Managed")
            {
                folder = managed;
            }
            files.Add(Path.Combine(folder, match.Groups[2].Value));
        }
        if (files.Count == 0)
        {
            throw new Stop("No <HintPath> found in " + paths.GameReferences);
        }
        return files;
    }

    static string CoreDll(RepoPaths paths)
    {
        if (!File.Exists(paths.CoreDll))
        {
            throw new Stop(
                "Missing " + paths.CoreDll + "\n"
                    + "The compilation needs the core DLL as a reference, so "
                    + "build the plugin first, without deploying:\n"
                    + "  dotnet build src/DarkwoodMP -c Release "
                    + "-p:DeployToGame=false\n"
                    + "Then run this command again.");
        }
        return paths.CoreDll;
    }
}
