using DarkwoodMP.Codemod.Loading;
using DarkwoodMP.Codemod.Rules;
using Microsoft.CodeAnalysis.CSharp;

namespace DarkwoodMP.Codemod.Census;

/// "codemod census": counts what the formatting pass has to change, per
/// construct and context. It writes nothing.
static class CensusCommand
{
    public static int Run(string[] args)
    {
        var project = PluginLoader.Load(RepoPaths.Find());
        if (args.Contains("--operators"))
        {
            return PrintOperators(project);
        }
        var compilation = project.Compilation;
        var unity = compilation.GetTypeByMetadataName("UnityEngine.Object");
        var samples = new Samples();
        var braces = new BraceCensus();
        var lines = new LineCensus();
        var operators = new OperatorCensus(unity, samples);
        foreach (var tree in project.Sources)
        {
            var root = tree.GetRoot();
            braces.Count(root, tree.FilePath);
            lines.Count(tree, root, tree.FilePath);
            operators.Count(
                root, compilation.GetSemanticModel(tree), tree.FilePath);
        }

        var parse = project.ParseOptions;
        Console.WriteLine($"Source folder: {project.SourceDir}");
        Console.WriteLine(
            $"Files: {project.Sources.Count}, lines: {lines.Lines}, "
                + $"references: {compilation.References.Count()}, "
                + $"language version: {parse.LanguageVersion.ToDisplayString()}");
        Console.WriteLine(
            $"Compilation: 0 errors, {project.Warnings} warnings, "
                + "UnityEngine.Object resolves");
        PrintTotals(braces, lines, operators);
        braces.Print();
        lines.OverLimit.Print();
        operators.Print();
        if (args.Contains("--samples"))
        {
            samples.Print();
        }
        return 0;
    }

    /// One line per operator: the number of sites left in the sources, for a
    /// stage to check census before = rewritten + queued + left for later.
    static int PrintOperators(PluginProject project)
    {
        var totals = new int[OperatorCount.All.Length];
        foreach (var tree in project.Sources)
        {
            var counts = OperatorCount.Of(tree.GetRoot());
            for (int i = 0; i < totals.Length; i++)
            {
                totals[i] += counts[i];
            }
        }
        foreach (var op in OperatorCount.All)
        {
            Console.WriteLine(
                $"{OperatorCount.Label(op)} {totals[(int)op]}");
        }
        return 0;
    }

    static void PrintTotals(
        BraceCensus braces, LineCensus lines, OperatorCensus operators)
    {
        Console.WriteLine("\n=== Totals");
        Total("braceless embedded statements", braces.Braceless);
        Total("one-line blocks", braces.OneLineBlocks);
        Total("one-line switches, types, namespaces", braces.OneLineBodies);
        Console.WriteLine(
            $"  {braces.OpenBracesEndingALine,6}  opening braces of blocks "
                + "not on their own line");
        Total("lines over 80 columns", lines.OverLimit);
        Total("?:", operators.Ternary);
        Total("?. chains", operators.Conditional);
        Total("??", operators.Coalesce);
        Total("??=", operators.CoalesceAssign);
    }

    static void Total(string name, Tally tally)
    {
        Console.WriteLine(
            $"  {tally.Total,6}  {name} "
                + $"(prod {tally.Production} / test {tally.Test})");
    }
}
