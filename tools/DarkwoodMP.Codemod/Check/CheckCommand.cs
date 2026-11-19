using DarkwoodMP.Codemod.Loading;
using DarkwoodMP.Codemod.Stages;
using Microsoft.CodeAnalysis;

namespace DarkwoodMP.Codemod.Check;

/// "codemod check [--braces] dirA dirB": two copies of the plugin's sources
/// compared file by file. Each file must hold the same comment words in the
/// same order, and the same code: apart from whitespace, or with --braces apart
/// from the braces stage's new blocks. Every comment must still be between the
/// same two tokens of code, apart from the moves the stage itself makes (see
/// CommentPlaces and BracedCode). Exits with 1 on any difference.
static class CheckCommand
{
    const string BracesOption = "--braces";
    const int Shown = 10;

    public static int Run(string[] args)
    {
        bool braces = args.Length > 0 && args[0] == BracesOption;
        if (braces)
        {
            args = args.Skip(1).ToArray();
        }
        if (args.Length != 2)
        {
            throw new Stop("Usage: codemod check [--braces] <dirA> <dirB>");
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

        int differentCode = 0;
        int differentWords = 0;
        long words = 0;
        foreach (var (name, tree) in left.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            if (!right.TryGetValue(name, out var other))
            {
                continue;
            }
            var rootA = tree.GetRoot();
            var rootB = other.GetRoot();
            var code = CodeDifference(rootA, rootB, braces);
            if (code != null)
            {
                differentCode++;
                if (differentCode <= Shown)
                {
                    Console.WriteLine($"  differs in {name}: {code}");
                }
            }
            var wordsA = CommentWords.Of(rootA);
            words += wordsA.Count;
            var difference = CommentWords.FirstDifference(
                wordsA, CommentWords.Of(rootB));
            if (difference != null)
            {
                differentWords++;
                if (differentWords <= Shown)
                {
                    Console.WriteLine(
                        $"  comment words differ in {name} at {difference}");
                }
            }
        }

        Console.WriteLine(
            $"{left.Count} files against {right.Count}, compared one by one: "
                + $"{missing.Count} only on one side, "
                + $"{differentCode} with different code or comment places "
                + $"({CodeRule(braces)}), "
                + $"{differentWords} with different comment words "
                + $"({words} words compared)");
        if (missing.Count > 0 || differentCode > 0 || differentWords > 0)
        {
            return 1;
        }
        return 0;
    }

    static string CodeRule(bool braces)
    {
        if (braces)
        {
            return "new braces aside";
        }
        return "whitespace aside";
    }

    /// What differs between the two files' code, or null when nothing does.
    static string CodeDifference(SyntaxNode a, SyntaxNode b, bool braces)
    {
        if (braces)
        {
            return BracedCode.Difference(a, b);
        }
        if (SameCode.Of(a, b))
        {
            return CommentPlaces.Difference(a, b);
        }
        return TokenDiff.First(a, b);
    }

    static Dictionary<string, SyntaxTree> ByRelativePath(PluginProject project)
    {
        return project.Sources.ToDictionary(
            t => Path.GetRelativePath(project.SourceDir, t.FilePath));
    }
}
