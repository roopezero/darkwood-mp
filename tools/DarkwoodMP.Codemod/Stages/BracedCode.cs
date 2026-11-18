using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DarkwoodMP.Codemod.Stages;

/// Whether a file is another one with nothing changed but braces added around
/// the single statement of an if, else, loop, using, lock or fixed. It walks
/// both trees side by side, so it proves what the compiled DLL can't show: an
/// else-if is still an else-if, a new block holds only the statement it was
/// added for, and every comment is still between the same two tokens of code.
sealed class BracedCode
{
    /// Where the new braces are in the tree after the stage.
    readonly HashSet<int> _added = new HashSet<int>();
    string _difference;

    /// Null when "after" is "before" with braces added; otherwise what differs
    /// first, with its line in "after".
    public static string Difference(SyntaxNode before, SyntaxNode after)
    {
        var walk = new BracedCode();
        walk.Walk(before, after);
        if (walk._difference != null)
        {
            return walk._difference;
        }
        return walk.CommentDifference(before, after);
    }

    void Walk(SyntaxNode a, SyntaxNode b)
    {
        if (_difference != null)
        {
            return;
        }
        if (a.RawKind != b.RawKind)
        {
            Differ(b, $"{a.Kind()} became {b.Kind()}");
            return;
        }
        var left = a.ChildNodesAndTokens();
        var right = b.ChildNodesAndTokens();
        if (left.Count != right.Count)
        {
            Differ(b, $"{a.Kind()} had {left.Count} parts, now {right.Count}");
            return;
        }
        for (int i = 0; i < left.Count; i++)
        {
            Pair(left[i], right[i], b);
        }
    }

    void Pair(SyntaxNodeOrToken a, SyntaxNodeOrToken b, SyntaxNode parent)
    {
        if (a.IsToken != b.IsToken)
        {
            Differ(parent, "a token and a node changed places");
            return;
        }
        if (a.IsToken)
        {
            CompareTokens(a.AsToken(), b.AsToken(), parent);
            return;
        }
        var statement = a.AsNode();
        if (b.AsNode() is BlockSyntax block
            && statement is not BlockSyntax
            && IsEmbedded(statement))
        {
            Added(statement, block);
            return;
        }
        Walk(statement, b.AsNode());
    }

    void CompareTokens(SyntaxToken a, SyntaxToken b, SyntaxNode parent)
    {
        if (a.RawKind != b.RawKind || a.Text != b.Text)
        {
            Differ(parent, $"'{a.Text}' became '{b.Text}'");
        }
    }

    /// The statement must be alone in the new block, and an "else if" must
    /// stay one: a block between them would still compile to the same code.
    void Added(SyntaxNode statement, BlockSyntax block)
    {
        if (statement is IfStatementSyntax
            && statement.Parent is ElseClauseSyntax)
        {
            Differ(block, "an else if became an else with a block");
            return;
        }
        if (block.Statements.Count != 1)
        {
            Differ(block, $"a new block holds {block.Statements.Count} statements");
            return;
        }
        _added.Add(block.OpenBraceToken.SpanStart);
        _added.Add(block.CloseBraceToken.SpanStart);
        Walk(statement, block.Statements[0]);
    }

    static bool IsEmbedded(SyntaxNode statement)
    {
        switch (statement.Parent)
        {
            case IfStatementSyntax s:
                return s.Statement == statement;
            case ElseClauseSyntax s:
                return s.Statement == statement;
            case ForStatementSyntax s:
                return s.Statement == statement;
            case CommonForEachStatementSyntax s:
                return s.Statement == statement;
            case WhileStatementSyntax s:
                return s.Statement == statement;
            case DoStatementSyntax s:
                return s.Statement == statement;
            case UsingStatementSyntax s:
                return s.Statement == statement;
            case LockStatementSyntax s:
                return s.Statement == statement;
            case FixedStatementSyntax s:
                return s.Statement == statement;
            default:
                return false;
        }
    }

    void Differ(SyntaxNode where, string what)
    {
        if (_difference == null)
        {
            var line = where.GetLocation().GetLineSpan().StartLinePosition.Line;
            _difference = $"line {line + 1}: {what}";
        }
    }

    string CommentDifference(SyntaxNode before, SyntaxNode after)
    {
        var left = Comments(before, new HashSet<int>());
        var right = Comments(after, _added);
        int shared = Math.Min(left.Count, right.Count);
        for (int i = 0; i < shared; i++)
        {
            if (left[i] != right[i])
            {
                return $"the comment \"{left[i].Words}\" moved or changed: "
                    + $"it was after {left[i].Tokens} tokens of code, and "
                    + $"\"{right[i].Words}\" is after {right[i].Tokens}";
            }
        }
        if (left.Count != right.Count)
        {
            return $"{left.Count} comments became {right.Count}";
        }
        return null;
    }

    /// Every comment with the number of code tokens in front of it. The new
    /// braces don't count, so a comment may end up on either side of one.
    static List<(int Tokens, string Words)> Comments(
        SyntaxNode root, HashSet<int> added)
    {
        var found = new List<(int Tokens, string Words)>();
        int code = 0;
        foreach (var token in root.DescendantTokens())
        {
            Collect(token.LeadingTrivia, code, found);
            if (!added.Contains(token.SpanStart))
            {
                code++;
            }
            Collect(token.TrailingTrivia, code, found);
        }
        return found;
    }

    static void Collect(
        SyntaxTriviaList trivia,
        int code,
        List<(int Tokens, string Words)> found)
    {
        foreach (var item in trivia)
        {
            if (Format.Lines.IsComment(item))
            {
                var words = item.ToFullString().Split(
                    (char[])null, StringSplitOptions.RemoveEmptyEntries);
                found.Add((code, string.Join(" ", words)));
            }
        }
    }
}
