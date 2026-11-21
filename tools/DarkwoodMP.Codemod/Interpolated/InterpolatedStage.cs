using DarkwoodMP.Codemod.Format;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DarkwoodMP.Codemod.Interpolated;

/// One cut the stage planned: the file, its line in the text the pass read,
/// the member and the string as it was before the cut.
sealed class CutSite
{
    public string File { get; set; }
    public int Line { get; set; }
    public string Member { get; set; }
    public string Before { get; set; }
}

/// A long line the stage left with an interpolated string whole, in the
/// text it wrote.
sealed class WholeString
{
    public string File { get; set; }
    public int Line { get; set; }
    public string Rule { get; set; } = "interpolated";
    public string Reason { get; set; }
    public string Snippet { get; set; }
}

/// Stage 11 over one file: the wrap stage's passes with Cutter as the rule
/// for interpolated strings. Each pass's tree replaces the file's tree in
/// the compilation, so every cut is planned with a semantic model of the
/// text it cuts.
sealed class InterpolatedStage
{
    public readonly List<CutSite> Cuts = new();
    public readonly SortedSet<string> Members = new(StringComparer.Ordinal);
    public readonly List<WholeString> Whole = new();

    readonly string _file;
    CSharpCompilation _compilation;
    SyntaxTree _current;

    InterpolatedStage(
        CSharpCompilation compilation, SyntaxTree tree, string file)
    {
        _compilation = compilation;
        _current = tree;
        _file = file;
    }

    public CSharpCompilation Compilation => _compilation;

    /// The file's new text (the same text when nothing changed), with the
    /// cuts, the members they are in and the strings left whole.
    public static (string After, InterpolatedStage Stage) Run(
        CSharpCompilation compilation, SyntaxTree tree, string file)
    {
        var stage = new InterpolatedStage(compilation, tree, file);
        var parse = (CSharpParseOptions)tree.Options;
        var before = tree.GetText().ToString();
        var after = Wrap.Run(before, parse, stage.Splitter);
        stage.Report(after, parse);
        return (after, stage);
    }

    IInterpolatedSplit Splitter(SyntaxTree tree)
    {
        _compilation = _compilation.ReplaceSyntaxTree(_current, tree);
        _current = tree;
        var model = _compilation.GetSemanticModel(tree);
        return new Cutter(model, (node, member) => Record(tree, node, member));
    }

    void Record(SyntaxTree tree, SyntaxNode node, string member)
    {
        var line = tree.GetLineSpan(node.Span).StartLinePosition.Line + 1;
        var before = node.ToString();
        if (Cuts.Any(c => c.Before == before && c.Member == member))
        {
            // A plan the pass put off because it touched another plan's
            // line: the next pass plans it again.
            return;
        }
        Cuts.Add(new CutSite
        {
            File = _file,
            Line = line,
            Member = member,
            Before = before,
        });
        if (member != null)
        {
            Members.Add(member);
        }
    }

    /// The lines still over the limit because of a string Cutter left whole.
    void Report(string after, CSharpParseOptions parse)
    {
        var tree = CSharpSyntaxTree.ParseText(after, parse);
        var lines = tree.GetText().Lines;
        foreach (var plan in Wrap.Plan(tree, Splitter))
        {
            if (plan.Reason == null
                || !plan.Reason.StartsWith(Cutter.Whole, StringComparison.Ordinal))
            {
                continue;
            }
            Whole.Add(new WholeString
            {
                File = _file,
                Line = plan.Line + 1,
                Reason = plan.Reason.Substring(Cutter.Whole.Length),
                Snippet = lines[plan.Line].ToString().Trim(),
            });
        }
    }
}
