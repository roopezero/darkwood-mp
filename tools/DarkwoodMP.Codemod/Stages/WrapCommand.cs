using DarkwoodMP.Codemod.Format;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DarkwoodMP.Codemod.Stages;

/// "codemod wrap [--report FILE | --explain FILE LINE]": stage 10 over the
/// plugin's sources, then the list of lines still over 80 columns with the
/// reason for each, on the console or in FILE.
static class WrapCommand
{
    public static int Run(string[] args)
    {
        string reportFile = null;
        if (args.Length == 3 && args[0] == "--explain")
        {
            return Explain(args[1], int.Parse(args[2]));
        }
        if (args.Length == 2 && args[0] == "--report")
        {
            reportFile = args[1];
        }
        else if (args.Length != 0)
        {
            throw new Stop(
                "Usage: codemod wrap [--report FILE | --explain FILE LINE]");
        }
        return StageCommand.Run(
            "wrap",
            Wrap.Run,
            (sources, parse) => Report(sources, parse, reportFile));
    }

    /// What one pass plans for one line of a file (its 1-based number): the
    /// rule and its edits, or the reason it leaves the line. For finding out
    /// why a line stays long. Parses with the latest language version.
    static int Explain(string file, int number)
    {
        var parse = new CSharpParseOptions(
            LanguageVersion.Latest, DocumentationMode.Parse);
        var text = File.ReadAllText(file);
        var tree = CSharpSyntaxTree.ParseText(text, parse);
        var plans = Wrap.Plan(tree).Where(p => p.Lines.Contains(number - 1));
        foreach (var plan in plans)
        {
            Console.WriteLine($"rule: {plan.Rule}  reason: {plan.Reason}");
            foreach (var edit in plan.Edits)
            {
                Console.WriteLine($"  edit {edit.Span}: '{edit.NewText}'");
            }
        }
        return 0;
    }

    static void Report(
        IReadOnlyList<(string File, string Text)> sources,
        CSharpParseOptions parse,
        string reportFile)
    {
        var left = new List<WidthReport.Left>();
        foreach (var (file, text) in sources)
        {
            left.AddRange(WidthReport.Of(file, text, parse));
        }
        if (reportFile == null)
        {
            WidthReport.Print(left, Console.Out);
            return;
        }
        using (var output = new StreamWriter(reportFile))
        {
            WidthReport.Print(left, output);
        }
        Console.WriteLine(
            $"wrap: {left.Count} lines still over {Wrap.Max} columns, "
                + "listed in " + reportFile);
    }
}
