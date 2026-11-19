using System.Text;
using DarkwoodMP.Codemod.Il;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DarkwoodMP.Codemod.Rules;

/// A rewrite a rule has planned but not yet proposed. Its text is made only
/// when every edit is known, because edits can sit inside each other: a
/// lambda whose body is rewritten can be in a branch of a ?: that is
/// rewritten too, or a rewritten statement in the body of an if whose
/// condition is rewritten.
sealed class PlannedEdit
{
    /// What the edit replaces in the original file.
    public TextSpan Span;

    /// The operator sites the edit removes.
    public List<SyntaxNode> Sites;

    /// Makes the new text. Pieces of the original go through the EditText,
    /// which puts the texts of the edits inside them in place.
    public Func<EditText, string> Build;

    /// Set when the edit replaces this statement, and maybe the comment
    /// behind it on its line.
    public StatementSyntax Statement;

    /// Set when the edit replaces this member, and maybe the comment behind
    /// it on its line. With neither set, the span is replaced as it is.
    public MemberDeclarationSyntax Member;

    /// Set when the edit replaces this expression where it is, such as a
    /// comparison that becomes "x != null && x.F == v". Its old and new
    /// texts are checked as expressions.
    public ExpressionSyntax Expression;

    /// String constants the new text holds once more than the old, such as
    /// the key of a target "e[\"key\"]" written in each branch. The IL diff
    /// lets a member's strings change only with such a record.
    public List<string> AddedStrings = new();

    internal readonly List<PlannedEdit> Inner = new();
}

/// The original text of a span, with the new texts of the edits inside it.
sealed class EditText
{
    readonly string _source;
    readonly List<(PlannedEdit Edit, string Text)> _inner;
    readonly HashSet<PlannedEdit> _used = new();

    public EditText(string source, List<(PlannedEdit Edit, string Text)> inner)
    {
        _source = source;
        _inner = inner;
    }

    public string Of(SyntaxNode node)
    {
        return Of(node.Span);
    }

    public string Of(int start, int end)
    {
        return Of(TextSpan.FromBounds(start, end));
    }

    public string Of(TextSpan span)
    {
        var output = new StringBuilder();
        int position = span.Start;
        foreach (var (edit, text) in _inner)
        {
            if (!span.OverlapsWith(edit.Span))
            {
                continue;
            }
            if (!span.Contains(edit.Span) || !_used.Add(edit))
            {
                throw new Stop(
                    "A rule took apart or copied the text of an edit inside "
                        + "its own: " + edit.Span);
            }
            output.Append(_source, position, edit.Span.Start - position);
            output.Append(text);
            position = edit.Span.End;
        }
        output.Append(_source, position, span.End - position);
        return output.ToString();
    }

    /// Every inner edit must be in the text exactly once, or its sites would
    /// be counted as rewritten without being so.
    public void RequireAllUsed()
    {
        foreach (var (edit, _) in _inner)
        {
            if (!_used.Contains(edit))
            {
                throw new Stop(
                    "A rule dropped the text of an edit inside its own: "
                        + edit.Span);
            }
        }
    }
}

/// Turns planned edits into proposals: only the outermost edits are
/// proposed, each with its text made from the texts of the edits inside it
/// and with all of their sites. If the outer one doesn't compile, the inner
/// ones go to the queue with it.
static class NestedEdits
{
    public static void Propose(RulePlan plan, List<PlannedEdit> edits)
    {
        var source = plan.Tree.GetText().ToString();
        foreach (var root in Forest(edits))
        {
            var text = Text(root, source);
            var sites = new List<SyntaxNode>();
            var added = new List<string>();
            Collect(root, sites, added);
            var strings = Strings(plan, sites[0], added);
            if (root.Statement != null)
            {
                plan.ProposeStatement(
                    sites, root.Statement, root.Span, text, strings);
            }
            else if (root.Member != null)
            {
                plan.ProposeMember(
                    sites, root.Member, root.Span, text, strings);
            }
            else if (root.Expression != null)
            {
                plan.ProposeExpression(sites, root.Expression, text, strings);
            }
            else
            {
                plan.ProposeSpan(sites, root.Span, text, strings);
            }
        }
    }

    static StringChange Strings(
        RulePlan plan, SyntaxNode site, List<string> added)
    {
        if (added.Count == 0)
        {
            return null;
        }
        return new StringChange
        {
            Member = MemberKeys.Of(site, plan.Model),
            Reason = plan.RuleName + ": a target is written once per branch, "
                + "so its string constants are there once per branch",
            Added = added,
        };
    }

    /// The outermost edits, with the others under them. Two edits either
    /// hold one another or don't touch.
    static List<PlannedEdit> Forest(List<PlannedEdit> edits)
    {
        var sorted = edits
            .OrderBy(e => e.Span.Start)
            .ThenByDescending(e => e.Span.Length)
            .ToList();
        var roots = new List<PlannedEdit>();
        var open = new Stack<PlannedEdit>();
        foreach (var edit in sorted)
        {
            while (open.Count > 0 && !open.Peek().Span.Contains(edit.Span))
            {
                if (open.Peek().Span.OverlapsWith(edit.Span))
                {
                    throw new Stop(
                        "Two planned edits overlap: " + open.Peek().Span
                            + " and " + edit.Span);
                }
                open.Pop();
            }
            if (open.Count > 0)
            {
                if (open.Peek().Span == edit.Span)
                {
                    throw new Stop(
                        "Two planned edits replace the same text: "
                            + edit.Span);
                }
                open.Peek().Inner.Add(edit);
            }
            else
            {
                roots.Add(edit);
            }
            open.Push(edit);
        }
        return roots;
    }

    static string Text(PlannedEdit edit, string source)
    {
        var inner = new List<(PlannedEdit Edit, string Text)>();
        foreach (var child in edit.Inner)
        {
            inner.Add((child, Text(child, source)));
        }
        var text = new EditText(source, inner);
        var result = edit.Build(text);
        text.RequireAllUsed();
        // The span ends before the line break that follows it, which stays:
        // one more at the end of the new text would leave a blank line.
        return result.TrimEnd('\n');
    }

    static void Collect(
        PlannedEdit edit, List<SyntaxNode> sites, List<string> added)
    {
        sites.AddRange(edit.Sites);
        added.AddRange(edit.AddedStrings);
        foreach (var child in edit.Inner)
        {
            Collect(child, sites, added);
        }
    }
}
