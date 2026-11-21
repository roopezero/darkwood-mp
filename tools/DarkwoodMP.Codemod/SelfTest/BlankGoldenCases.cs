using DarkwoodMP.Analyzers;
using DarkwoodMP.Codemod.Format;
using DarkwoodMP.Codemod.Stages;
using Microsoft.CodeAnalysis.CSharp;

namespace DarkwoodMP.Codemod.SelfTest;

/// Stage 12's golden cases in SelfTest/Blank/*.golden: an "=== input"
/// section in the layout stage's form, what the stage must make of it in
/// "=== blank", and in "=== left" the places it must leave alone, one
/// "line: reason" each (lines of the output). Every case also checks the
/// stage's gates: only empty lines are added, the code and the comments stay
/// where they were, a second run changes nothing, the layout pass keeps the
/// output, rule DW0007 reports only the places left alone, and the DLL is
/// byte-identical.
static class BlankGoldenCases
{
    const string Folder = "tools/DarkwoodMP.Codemod/SelfTest/Blank";

    public static List<string> Files(string root)
    {
        return Directory.GetFiles(Path.Combine(root, Folder), "*.golden")
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();
    }

    public static string NameOf(string file)
    {
        return "blank/" + Path.GetFileNameWithoutExtension(file);
    }

    public static string Show(string file)
    {
        var input = WrapGoldenCases.Sections(file)["input"];
        var output = Blank.Run(input, SnippetCompiler.Parse);
        return "=== input\n" + input + "=== blank\n" + output
            + "=== left\n" + LeftOf(output);
    }

    public static List<string> Failures(string file)
    {
        var problems = new List<string>();
        var sections = WrapGoldenCases.Sections(file);
        var parse = SnippetCompiler.Parse;
        var input = sections["input"];
        var output = Blank.Run(input, parse);
        if (!sections.TryGetValue("blank", out var expected))
        {
            problems.Add("  no expected blank output in the golden file");
        }
        else if (expected != output)
        {
            problems.Add("  blank: output differs from the golden file");
            problems.Add(LineDiff.Of(expected, output));
        }
        var left = LeftOf(output);
        var listed = "";
        if (sections.TryGetValue("left", out var section))
        {
            listed = section;
        }
        if (listed.Trim() != left.Trim())
        {
            problems.Add("  left differs from the golden file:\n" + left);
        }
        if (Blank.Run(output, parse) != output)
        {
            problems.Add("  a second blank run changed the output");
        }
        var formatted = Passes.Layout(output, parse);
        if (formatted != output)
        {
            problems.Add("  the layout pass changes the blank output");
            problems.Add(LineDiff.Of(output, formatted));
        }
        var extra = Gates.OnlyEmptyLinesAdded(input, output);
        if (extra != null)
        {
            problems.Add("  blank changed a line: " + extra);
        }
        var before = CSharpSyntaxTree.ParseText(input, parse).GetRoot();
        var after = CSharpSyntaxTree.ParseText(output, parse);
        if (!SameCode.Of(before, after.GetRoot()))
        {
            problems.Add("  blank changed the code: "
                + TokenDiff.First(before, after.GetRoot()));
        }
        var moved = CommentPlaces.Difference(before, after.GetRoot());
        if (moved != null)
        {
            problems.Add("  blank moved a comment: " + moved);
        }
        var found = BlankLines.Find(after).Length;
        var alone = Blank.LeftAlone(output, parse).Count;
        if (found != alone)
        {
            problems.Add($"  DW0007 reports {found} places, {alone} left alone");
        }
        var first = SnippetCompiler.Emit(input, out var inputErrors);
        var second = SnippetCompiler.Emit(output, out var outputErrors);
        if (first == null || second == null)
        {
            problems.Add("  doesn't compile:\n" + inputErrors + outputErrors);
        }
        else if (!first.AsSpan().SequenceEqual(second))
        {
            problems.Add("  blank changed the compiled DLL");
        }
        return problems;
    }

    static string LeftOf(string output)
    {
        var left = Blank.LeftAlone(output, SnippetCompiler.Parse);
        return string.Concat(left.Select(l => $"{l.Line}: {l.Reason}\n"));
    }
}
