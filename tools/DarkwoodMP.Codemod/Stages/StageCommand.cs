using System.Text;
using DarkwoodMP.Codemod.Census;
using DarkwoodMP.Codemod.Loading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace DarkwoodMP.Codemod.Stages;

/// Runs one whitespace stage over the plugin's sources: loads the compilation
/// (stopping on any existing error), rewrites every file in memory, proves the
/// result (see Gates) and only then writes the files (see OutputFiles).
static class StageCommand
{
    public delegate string Pass(string source, CSharpParseOptions parse);

    /// What a stage reports once its files are written (or found already
    /// done): every source file's path and its text as it now stands.
    public delegate void Report(
        IReadOnlyList<(string File, string Text)> sources,
        CSharpParseOptions parse);

    public static int Run(string stage, Pass pass)
    {
        return Run(stage, pass, null);
    }

    public static int Run(string stage, Pass pass, Report report)
    {
        var project = PluginLoader.Load(RepoPaths.Find());
        var parse = project.ParseOptions;
        var rewritten = new List<(SyntaxTree Tree, string After)>();
        foreach (var tree in project.Sources)
        {
            var before = tree.GetText().ToString();
            RequireLf(tree.FilePath, before);
            var after = pass(before, parse);
            if (after != before)
            {
                rewritten.Add((tree, after));
            }
        }
        if (rewritten.Count == 0)
        {
            Console.WriteLine($"{stage}: no file changes.");
            Finish(project, rewritten, report);
            return 0;
        }
        var gates = new Gates(project, stage, pass);
        gates.Run(rewritten);
        OutputFiles.WriteAll(
            rewritten.Select(r => (r.Tree.FilePath, r.After)).ToList());
        Console.WriteLine(
            $"{stage}: {rewritten.Count} of {project.Sources.Count} files "
                + "rewritten. The compiled DLL is byte-identical in memory; "
                + "the proof that counts is tools/verify-identical.sh.");
        Finish(project, rewritten, report);
        return 0;
    }

    static void Finish(
        PluginProject project,
        List<(SyntaxTree Tree, string After)> rewritten,
        Report report)
    {
        if (report == null)
        {
            return;
        }
        var changed = rewritten.ToDictionary(
            r => r.Tree.FilePath, r => r.After);
        var sources = new List<(string File, string Text)>();
        foreach (var tree in project.Sources)
        {
            if (!changed.TryGetValue(tree.FilePath, out var text))
            {
                text = tree.GetText().ToString();
            }
            var file = Path.GetRelativePath(project.SourceDir, tree.FilePath);
            sources.Add((file, text));
        }
        report(sources, project.ParseOptions);
    }

    static void RequireLf(string file, string text)
    {
        if (text.Contains('\r'))
        {
            throw new Stop("Carriage return in " + file + ": the repo is LF only.");
        }
        if (File.ReadAllBytes(file).AsSpan().StartsWith(Encoding.UTF8.Preamble))
        {
            throw new Stop("Byte order mark in " + file + ": remove it first.");
        }
    }
}
