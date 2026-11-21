using DarkwoodMP.Codemod.Interpolated;
using DarkwoodMP.Codemod.Loading;
using Microsoft.CodeAnalysis;

namespace DarkwoodMP.Codemod.DiffTest;

/// "codemod difftest interpolated": stage 11 over each case in
/// DiffTest/Cases/interpolated.cases, the original and the split run side by
/// side by the harness. The cases are compiled against the game's mscorlib,
/// System and System.Core, as the plugin is: that mscorlib has no
/// interpolated string handler, so both forms become string.Format and
/// string.Concat calls, and the order in which holes are evaluated and made
/// into text is the plugin's.
static class InterpolatedCases
{
    static readonly string[] Files =
    {
        "mscorlib.dll",
        "System.dll",
        "System.Core.dll",
    };

    static List<MetadataReference> _references;

    public static DiffOutcome Run(DiffCase diffCase)
    {
        return DiffHarness.Run(diffCase, Split, false, References());
    }

    static (string After, string Why) Split(
        Microsoft.CodeAnalysis.CSharp.CSharpCompilation compilation,
        SyntaxTree tree)
    {
        var result = InterpolatedCommand.Split(compilation, new[] { tree }, ".");
        if (result.Rewritten.Count == 0)
        {
            var reasons = result.Whole.Select(w => w.Reason);
            return (null, "no string split; " + string.Join("; ", reasons));
        }
        InterpolatedGates.Run(compilation, result);
        return (result.Rewritten[0].After, null);
    }

    static List<MetadataReference> References()
    {
        if (_references != null)
        {
            return _references;
        }
        var managed = Path.Combine(RepoPaths.GameDir, "Darkwood_Data/Managed");
        _references = new List<MetadataReference>();
        foreach (var name in Files)
        {
            var file = Path.Combine(managed, name);
            if (!File.Exists(file))
            {
                throw new Stop("difftest interpolated compiles against the "
                    + "game's " + name + ", and there is none at " + file
                    + ". Install the game or set GameDir.");
            }
            _references.Add(MetadataReference.CreateFromFile(file));
        }
        return _references;
    }
}
