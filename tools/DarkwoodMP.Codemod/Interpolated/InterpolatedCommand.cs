using System.Text.Json;
using DarkwoodMP.Codemod.Il;
using DarkwoodMP.Codemod.Loading;
using DarkwoodMP.Codemod.Rules;
using DarkwoodMP.Codemod.Stages;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DarkwoodMP.Codemod.Interpolated;

/// "codemod interpolated --out DIR [--src DIR] [--dry-run]": stage 11 over
/// the plugin's sources (or another copy of them). Each interpolated string
/// on a line over 80 columns is split at spaces into parts joined with +
/// (Cutter), and the stage proves the result (Gates) before it writes the
/// manifest, the sites and the strings left whole to DIR, and the sources
/// unless --dry-run is given.
static class InterpolatedCommand
{
    const string Usage =
        "codemod interpolated --out DIR [--src DIR] [--dry-run]";

    public const string Reason =
        "stage 11: an interpolated string split into parts joined with +";

    public static int Run(string[] args)
    {
        string output = null;
        string source = null;
        bool dryRun = false;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--out" && i + 1 < args.Length)
            {
                output = args[++i];
            }
            else if (args[i] == "--src" && i + 1 < args.Length)
            {
                source = args[++i];
            }
            else if (args[i] == "--dry-run")
            {
                dryRun = true;
            }
            else
            {
                throw new Stop(Usage);
            }
        }
        if (output == null)
        {
            throw new Stop(Usage);
        }
        var project = PluginLoader.Load(RepoPaths.Find(), source);
        var result = Split(project.Compilation, project.Sources, project.SourceDir);
        InterpolatedGates.Run(project.Compilation, result);
        Directory.CreateDirectory(output);
        result.Manifest.Write(Path.Combine(output, "manifest.json"));
        WriteLines(Path.Combine(output, "sites.jsonl"), result.Cuts);
        WriteLines(Path.Combine(output, "queue.jsonl"), result.Whole);
        Print(result);
        if (dryRun)
        {
            Console.WriteLine("--dry-run: no source was changed.");
            return 0;
        }
        OutputFiles.WriteAll(
            result.Rewritten.Select(r => (r.Tree.FilePath, r.After)).ToList());
        return 0;
    }

    /// The stage over every file, each from the original compilation.
    public static SplitResult Split(
        CSharpCompilation compilation,
        IReadOnlyList<SyntaxTree> sources,
        string sourceDir)
    {
        var result = new SplitResult();
        foreach (var tree in sources)
        {
            var file = Path.GetRelativePath(sourceDir, tree.FilePath);
            var (after, stage) = InterpolatedStage.Run(compilation, tree, file);
            result.Whole.AddRange(stage.Whole);
            if (after == tree.GetText().ToString())
            {
                continue;
            }
            result.Rewritten.Add((tree, after));
            result.Cuts.AddRange(stage.Cuts);
            foreach (var member in stage.Members)
            {
                if (!result.Manifest.Members.Contains(member))
                {
                    result.Manifest.Members.Add(member);
                }
            }
        }
        result.Manifest.Rule = "interpolated";
        result.Manifest.Members.Sort(StringComparer.Ordinal);
        return result;
    }

    static void WriteLines<T>(string path, List<T> entries)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };
        File.WriteAllLines(
            path, entries.Select(e => JsonSerializer.Serialize(e, options)));
    }

    static void Print(SplitResult result)
    {
        Console.WriteLine(
            $"interpolated: {result.Cuts.Count} cuts in "
                + $"{result.Rewritten.Count} files, "
                + $"{result.Manifest.Members.Count} members in the manifest, "
                + $"{result.Manifest.StringChanges.Count} with new string "
                + $"constants; {result.Whole.Count} long lines with a string "
                + "left whole:");
        foreach (var group in result.Whole.GroupBy(w => w.Reason)
            .OrderByDescending(g => g.Count()))
        {
            Console.WriteLine($"  {group.Count(),5}  {group.Key}");
        }
    }
}

/// What the stage made of the sources.
sealed class SplitResult
{
    public readonly List<(SyntaxTree Tree, string After)> Rewritten = new();
    public readonly List<CutSite> Cuts = new();
    public readonly List<WholeString> Whole = new();
    public readonly Manifest Manifest = new();
}
