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

    public static int Run(string stage, Pass pass)
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
        return 0;
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
