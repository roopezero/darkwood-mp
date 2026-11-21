using DarkwoodMP.Codemod.Format;
using DarkwoodMP.Codemod.Stages;
using Microsoft.CodeAnalysis.CSharp;

namespace DarkwoodMP.Codemod.SelfTest;

/// The wrap stage's golden cases in SelfTest/Wrap/*.golden: an "=== input"
/// section in the layout stage's form, what the stage must make of it in
/// "=== wrap", and in "=== report" the lines it must leave over 80 columns,
/// one "line: reason" each (lines of the wrap output). Every case also
/// checks the stage's gates: a second run changes nothing, the layout pass
/// keeps the output, the comment words and the code (with split strings
/// joined back) are the same, and the DLL is byte-identical.
static class WrapGoldenCases
{
    const string Folder = "tools/DarkwoodMP.Codemod/SelfTest/Wrap";

    public static List<string> Files(string root)
    {
        var folder = Path.Combine(root, Folder);
        return Directory.GetFiles(folder, "*.golden")
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();
    }

    public static string NameOf(string file)
    {
        return "wrap/" + Path.GetFileNameWithoutExtension(file);
    }

    public static string Show(string file)
    {
        var sections = Sections(file);
        var input = sections["input"];
        var parse = SnippetCompiler.Parse;
        var wrapped = Wrap.Run(input, parse);
        var report = WidthReport.Of("golden.cs", wrapped, parse)
            .Select(l => $"{l.Line}: {l.Reason}\n");
        return "=== input\n" + input + "=== wrap\n" + wrapped
            + "=== report\n" + string.Concat(report);
    }

    public static List<string> Failures(string file)
    {
        var problems = new List<string>();
        var sections = Sections(file);
        var parse = SnippetCompiler.Parse;
        var input = sections["input"];
        var wrapped = Wrap.Run(input, parse);
        if (!sections.TryGetValue("wrap", out var expected))
        {
            problems.Add("  no expected wrap output in the golden file");
        }
        else if (expected != wrapped)
        {
            problems.Add("  wrap: output differs from the golden file");
            problems.Add(LineDiff.Of(expected, wrapped));
        }
        var report = string.Concat(WidthReport.Of("golden.cs", wrapped, parse)
            .Select(l => $"{l.Line}: {l.Reason}\n"));
        var listed = "";
        if (sections.TryGetValue("report", out var section))
        {
            listed = section;
        }
        if (listed.Trim() != report.Trim())
        {
            problems.Add("  report differs from the golden file:\n" + report);
        }
        if (Wrap.Run(wrapped, parse) != wrapped)
        {
            problems.Add("  a second wrap run changed the output");
        }
        if (Passes.Layout(wrapped, parse) != wrapped)
        {
            problems.Add("  the layout pass changes the wrap output");
        }
        var before = CSharpSyntaxTree.ParseText(input, parse).GetRoot();
        var after = CSharpSyntaxTree.ParseText(wrapped, parse).GetRoot();
        var words = CommentWords.FirstDifference(
            CommentWords.ForWrap(CommentWords.Of(before)),
            CommentWords.ForWrap(CommentWords.Of(after)));
        if (words != null)
        {
            problems.Add("  wrap changed the comment words at " + words);
        }
        if (!JoinedStrings.SameCode(before, after))
        {
            problems.Add("  wrap changed the code: "
                + TokenDiff.First(before, after));
        }
        var moved = WrapPlaces.Of(before, after, split: true);
        if (moved != null)
        {
            problems.Add("  wrap moved a comment: " + moved);
        }
        var first = SnippetCompiler.Emit(input, out var inputErrors);
        var second = SnippetCompiler.Emit(wrapped, out var wrapErrors);
        if (first == null || second == null)
        {
            problems.Add("  doesn't compile:\n" + inputErrors + wrapErrors);
        }
        else if (!first.AsSpan().SequenceEqual(second))
        {
            problems.Add("  wrap changed the compiled DLL");
        }
        return problems;
    }

    public static Dictionary<string, string> Sections(string file)
    {
        var sections = new Dictionary<string, List<string>>();
        List<string> current = null;
        foreach (var line in File.ReadAllLines(file))
        {
            if (line.StartsWith("=== ", StringComparison.Ordinal))
            {
                current = new List<string>();
                sections[line.Substring(4).Trim()] = current;
            }
            else if (current != null)
            {
                current.Add(line);
            }
        }
        if (!sections.ContainsKey("input"))
        {
            throw new Stop("Golden case " + file + " has no input section.");
        }
        return sections.ToDictionary(
            s => s.Key, s => string.Join("\n", s.Value) + "\n");
    }
}
