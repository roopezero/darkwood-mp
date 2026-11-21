using DarkwoodMP.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DarkwoodMP.Codemod.Format;

/// Stage 12: the blank lines rule DW0007 asks for. The places come from the
/// analyzer's own BlankLines.Find (linked into this tool), so the stage and the
/// rule can't disagree. Each blank line is an empty line put in front of the
/// line that follows the end of the statement, member or accessor: in front of
/// the next code, or of a comment or a directive line between them. Nothing
/// else in the file changes.
static class Blank
{
    /// A place the stage leaves alone: the line after the end is the middle
    /// of a /* */ comment that starts on the line of the end, so an empty line
    /// there would go into the comment.
    public readonly record struct Left(int Line, string Reason);

    public static string Run(string source, CSharpParseOptions parse)
    {
        var tree = CSharpSyntaxTree.ParseText(source, parse);
        var at = Places(tree, out _);
        if (at.Count == 0)
        {
            return source;
        }
        var text = new System.Text.StringBuilder(source.Length + at.Count);
        int from = 0;
        foreach (var position in at)
        {
            text.Append(source, from, position - from);
            text.Append('\n');
            from = position;
        }
        text.Append(source, from, source.Length - from);
        return text.ToString();
    }

    /// The places the rule still reports in source that the stage can't
    /// fill, with 1-based line numbers.
    public static List<Left> LeftAlone(string source, CSharpParseOptions parse)
    {
        var tree = CSharpSyntaxTree.ParseText(source, parse);
        Places(tree, out var left);
        return left;
    }

    /// Where each empty line goes: the start of the line after each end the
    /// rule reports, in the order of the file.
    static List<int> Places(SyntaxTree tree, out List<Left> left)
    {
        var lines = tree.GetText().Lines;
        var root = tree.GetRoot();
        var at = new List<int>();
        left = new List<Left>();
        foreach (var last in BlankLines.Find(tree))
        {
            var line = lines.IndexOf(last.Span.End);
            var start = lines[line + 1].Start;
            var trivia = root.FindTrivia(start);
            if (trivia.Span.Start < start && trivia.Span.End > start)
            {
                left.Add(new Left(
                    line + 1, "the next line is inside a comment that starts "
                        + "on this one"));
                continue;
            }
            if (at.Count == 0 || at[^1] != start)
            {
                at.Add(start);
            }
        }
        return at;
    }
}
