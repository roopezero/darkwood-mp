using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace DarkwoodMP.Codemod.Format;

/// Stage 10: every line at most 80 columns, comments included. Each pass
/// plans at most one break for each line over the limit, on the tree of the
/// text it was given, applies them all and runs the layout pass over the
/// result, so what it writes is what the Formatter keeps. Passes repeat until
/// one changes nothing. A line no rule can shorten is left as it is, and
/// Leftovers says why.
static class Wrap
{
    public const int Max = 80;
    public const int Step = 4;

    /// Enough for the deepest nesting in the plugin; a text still changing
    /// after this many passes means two rules undo each other.
    const int MaxPasses = 40;

    /// The line tools/tests/worldtest.py reads the protocol version from.
    public static readonly Regex VersionLine =
        new(@"ushort Version = (\d+);");

    public static string Run(string source, CSharpParseOptions parse)
    {
        var text = source;
        for (int pass = 0; pass < MaxPasses; pass++)
        {
            var next = Pass(text, parse);
            if (next == text)
            {
                return text;
            }
            text = next;
        }
        throw new Stop(
            $"wrap: the text still changes after {MaxPasses} passes. Two rules "
                + "undo each other; run selftest and look for the line.");
    }

    /// One pass: the planned breaks, then the layout pass. Unchanged text
    /// when no line has a break to make.
    static string Pass(string text, CSharpParseOptions parse)
    {
        var tree = CSharpSyntaxTree.ParseText(text, parse);
        var plans = Plan(tree);
        var lines = tree.GetText().Lines;
        var edits = new List<TextChange>();
        int end = -1;
        foreach (var plan in plans.Where(p => p.Edits.Count > 0))
        {
            var first = lines.GetLineFromPosition(
                plan.Edits.Min(e => e.Span.Start)).LineNumber;
            if (first <= end)
            {
                // Touches a line of the plan before it: the next pass
                // plans it again on the new text.
                continue;
            }
            edits.AddRange(plan.Edits);
            end = lines.GetLineFromPosition(
                plan.Edits.Max(e => e.Span.End)).LineNumber;
        }
        if (edits.Count == 0)
        {
            return text;
        }
        var changed = tree.GetText().WithChanges(edits).ToString();
        return Format.Passes.Layout(changed, parse);
    }

    /// One plan per line over the limit, in the order of the lines. A plan
    /// with no edits carries the reason the line stays long.
    public static List<LinePlan> Plan(SyntaxTree tree)
    {
        var root = tree.GetRoot();
        var text = tree.GetText();
        var plans = new List<LinePlan>();
        var done = new HashSet<int>();
        foreach (var line in text.Lines)
        {
            if (line.Span.Length <= Max || done.Contains(line.LineNumber))
            {
                continue;
            }
            var plan = PlanLine(new WrapLine(root, text, line));
            plans.Add(plan);
            foreach (var number in plan.Lines)
            {
                done.Add(number);
            }
        }
        return plans;
    }

    static LinePlan PlanLine(WrapLine line)
    {
        if (VersionLine.IsMatch(line.Text))
        {
            return line.Leave("the protocol version line, read by "
                + "tools/tests/worldtest.py");
        }
        var inside = line.InsideToken();
        if (inside != null)
        {
            return line.Leave(inside);
        }
        if (line.IsCommentOnly(out var comment))
        {
            return WrapComments.Plan(line, comment);
        }
        if (line.IsDirectiveOrDisabled())
        {
            return line.Leave("a preprocessor directive or disabled code");
        }
        var trailing = WrapComments.MoveTrailing(line);
        if (trailing != null)
        {
            return trailing;
        }
        return WrapCode.Plan(line);
    }
}

/// What a pass does to one long line: the edits (the same break may need
/// several, as when an argument list is filled), the lines it covers, or the
/// reason the line stays as it is.
sealed class LinePlan
{
    public readonly int Line;
    public readonly List<TextChange> Edits;
    public readonly List<int> Lines;
    public readonly string Rule;
    public readonly string Reason;

    public LinePlan(int line, string rule, List<TextChange> edits)
    {
        Line = line;
        Rule = rule;
        Edits = edits;
        Lines = new List<int> { line };
    }

    public LinePlan(int line, string reason)
    {
        Line = line;
        Edits = new List<TextChange>();
        Lines = new List<int> { line };
        Reason = reason;
    }
}
