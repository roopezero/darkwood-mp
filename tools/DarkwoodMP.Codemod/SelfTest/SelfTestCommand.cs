using DarkwoodMP.Codemod.Format;
using DarkwoodMP.Codemod.Loading;
using DarkwoodMP.Codemod.Stages;
using Microsoft.CodeAnalysis.CSharp;

namespace DarkwoodMP.Codemod.SelfTest;

/// "codemod selftest": runs every golden case in SelfTest/Golden through both
/// stages. Exits with 1 when any case fails.
static class SelfTestCommand
{
    const string GoldenFolder = "tools/DarkwoodMP.Codemod/SelfTest/Golden";

    public static int Run(string[] args)
    {
        var folder = Path.Combine(RepoPaths.Find().Root, GoldenFolder);
        var files = Directory.GetFiles(folder, "*.golden")
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();
        if (files.Count == 0)
        {
            throw new Stop("No golden cases in " + folder);
        }
        if (args.Length == 2 && args[0] == "--show")
        {
            return Show(folder, args[1]);
        }
        int failed = 0;
        foreach (var file in files)
        {
            var problems = Failures(GoldenCase.Read(file));
            var name = Path.GetFileNameWithoutExtension(file);
            if (problems.Count == 0)
            {
                Console.WriteLine("ok    " + name);
                continue;
            }
            failed++;
            Console.WriteLine("FAIL  " + name);
            foreach (var problem in problems)
            {
                Console.WriteLine(problem);
            }
        }
        Console.WriteLine(
            $"{files.Count - failed} of {files.Count} golden cases passed.");
        var accepted = BracedCodeCases.Failures();
        accepted.AddRange(LayoutCheckCases.Failures());
        foreach (var problem in accepted)
        {
            Console.WriteLine("FAIL  check\n" + problem);
        }
        int cases = BracedCodeCases.Count + LayoutCheckCases.Count;
        Console.WriteLine(
            $"{cases - accepted.Count} of {cases} gate cases passed.");
        if (failed > 0 || accepted.Count > 0)
        {
            return 1;
        }
        return 0;
    }

    /// Prints a case's file as it would look with the actual outputs, for
    /// reading in a review, or for writing an expected output once it is read.
    static int Show(string folder, string name)
    {
        var golden = GoldenCase.Read(Path.Combine(folder, name + ".golden"));
        var braces = Passes.Braces(golden.Input, SnippetCompiler.Parse);
        var layout = Passes.Layout(braces, SnippetCompiler.Parse);
        Console.Write("=== input\n" + golden.Input);
        if (braces != golden.Input)
        {
            Console.Write("=== braces\n" + braces);
        }
        Console.Write("=== layout\n" + layout);
        return 0;
    }

    /// Everything wrong with one case; empty when it passes.
    static List<string> Failures(GoldenCase golden)
    {
        var problems = new List<string>();
        var parse = SnippetCompiler.Parse;
        var braces = Passes.Braces(golden.Input, parse);
        Compare("braces", golden.Braces, braces, problems);
        var layout = Passes.Layout(braces, parse);
        Compare("layout", golden.Layout, layout, problems);

        if (Passes.Braces(braces, parse) != braces)
        {
            problems.Add("  a second braces run changed the output");
        }
        if (Passes.Layout(layout, parse) != layout)
        {
            problems.Add("  a second layout run changed the output");
        }
        var added = BracedCode.Difference(
            CSharpSyntaxTree.ParseText(golden.Input, parse).GetRoot(),
            CSharpSyntaxTree.ParseText(braces, parse).GetRoot());
        if (added != null)
        {
            problems.Add("  braces changed more than braces: " + added);
        }
        CompareComments(golden.Input, braces, "braces", problems);
        CompareComments(braces, layout, "layout", problems);
        var opened = CSharpSyntaxTree.ParseText(braces, parse).GetRoot();
        var formatted = CSharpSyntaxTree.ParseText(layout, parse).GetRoot();
        if (!SameCode.Of(opened, formatted))
        {
            problems.Add("  layout changed more than whitespace");
        }
        var moved = CommentPlaces.Difference(opened, formatted);
        if (moved != null)
        {
            problems.Add("  layout moved a comment: " + moved);
        }
        CompareDlls(golden.Input, braces, layout, problems);
        return problems;
    }

    static void Compare(
        string stage, string expected, string actual, List<string> problems)
    {
        if (expected == null)
        {
            problems.Add($"  no expected {stage} output in the golden file");
            return;
        }
        if (expected == actual)
        {
            return;
        }
        problems.Add($"  {stage}: output differs from the golden file");
        problems.Add(LineDiff.Of(expected, actual));
    }

    static void CompareComments(
        string before, string after, string stage, List<string> problems)
    {
        var difference = CommentWords.FirstDifference(
            CommentWords.Of(before, SnippetCompiler.Parse),
            CommentWords.Of(after, SnippetCompiler.Parse));
        if (difference != null)
        {
            problems.Add($"  {stage} changed the comment words at {difference}");
        }
    }

    static void CompareDlls(
        string input, string braces, string layout, List<string> problems)
    {
        var first = SnippetCompiler.Emit(input, out var inputErrors);
        var second = SnippetCompiler.Emit(braces, out var bracesErrors);
        var third = SnippetCompiler.Emit(layout, out var layoutErrors);
        if (first == null || second == null || third == null)
        {
            problems.Add(
                "  doesn't compile:\n"
                    + inputErrors + bracesErrors + layoutErrors);
            return;
        }
        if (!first.AsSpan().SequenceEqual(second))
        {
            problems.Add("  braces changed the compiled DLL");
        }
        if (!first.AsSpan().SequenceEqual(third))
        {
            problems.Add("  layout changed the compiled DLL");
        }
    }
}
