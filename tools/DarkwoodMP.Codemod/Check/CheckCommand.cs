using DarkwoodMP.Codemod.Loading;
using DarkwoodMP.Codemod.Stages;

namespace DarkwoodMP.Codemod.Check;

/// "codemod check dirA dirB": two copies of the plugin's sources compared:
/// the same code once whitespace is ignored (SyntaxFactory.AreEquivalent) and
/// the same comment words in the same order. Exits with 1 on any difference.
static class CheckCommand
{
    const int Shown = 10;

    public static int Run(string[] args)
    {
        if (args.Length != 2)
        {
            throw new Stop("Usage: codemod check <dirA> <dirB>");
        }
        var paths = RepoPaths.Find();
        var a = PluginLoader.Load(paths, args[0]);
        var b = PluginLoader.Load(paths, args[1]);
        var left = ByRelativePath(a);
        var right = ByRelativePath(b);

        var missing = left.Keys.Except(right.Keys)
            .Concat(right.Keys.Except(left.Keys))
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();
        foreach (var name in missing.Take(Shown))
        {
            Console.WriteLine("  only in one folder: " + name);
        }

        int notEquivalent = 0;
        int comments = 0;
        foreach (var (name, tree) in left.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            if (!right.TryGetValue(name, out var other))
            {
                continue;
            }
            var rootA = tree.GetRoot();
            var rootB = other.GetRoot();
            if (!SameCode.Of(rootA, rootB))
            {
                notEquivalent++;
                if (notEquivalent <= Shown)
                {
                    Console.WriteLine(
                        $"  not equivalent: {name}: "
                            + TokenDiff.First(rootA, rootB));
                }
            }
            var difference = CommentWords.FirstDifference(
                CommentWords.Of(rootA), CommentWords.Of(rootB));
            if (difference != null)
            {
                comments++;
                if (comments <= Shown)
                {
                    Console.WriteLine($"  comment words differ in {name} at {difference}");
                }
            }
        }

        Console.WriteLine(
            $"{left.Count} files against {right.Count}: "
                + $"{missing.Count} only on one side, "
                + $"{notEquivalent} not equivalent (whitespace aside), "
                + $"{comments} with different comment words");
        if (missing.Count > 0 || notEquivalent > 0 || comments > 0)
        {
            return 1;
        }
        return 0;
    }

    static Dictionary<string, Microsoft.CodeAnalysis.SyntaxTree> ByRelativePath(
        PluginProject project)
    {
        return project.Sources.ToDictionary(
            t => Path.GetRelativePath(project.SourceDir, t.FilePath));
    }
}
