using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DarkwoodMP.Codemod.Format;

/// One line over the limit, with the tree it is in: its text, indentation,
/// tokens and the helpers the width rules share.
sealed class WrapLine
{
    public readonly SyntaxNode Root;
    public readonly SourceText Source;
    public readonly TextLine Line;
    public readonly string Text;
    public readonly int Indent;

    public WrapLine(SyntaxNode root, SourceText source, TextLine line)
    {
        Root = root;
        Source = source;
        Line = line;
        Text = line.ToString();
        Indent = Text.Length - Text.TrimStart().Length;
    }

    public int Number => Line.LineNumber;
    public int Start => Line.Start;
    public int End => Line.End;
    public string Pad => new string(' ', Indent);

    /// A line break and the indentation one level in from this line.
    public string Inner => "\n" + new string(' ', Indent + Wrap.Step);

    public int Col(int position)
    {
        return position - Line.Start;
    }

    public LinePlan Leave(string reason)
    {
        return new LinePlan(Number, reason);
    }

    public LinePlan Break(string rule, params TextChange[] edits)
    {
        return new LinePlan(Number, rule, edits.ToList());
    }

    public LinePlan Break(string rule, List<TextChange> edits)
    {
        return new LinePlan(Number, rule, edits);
    }

    /// Why the line can't be touched because it starts inside a token or a
    /// comment that began on an earlier line; null when it doesn't.
    public string InsideToken()
    {
        var token = Root.FindToken(Start);
        if (token.SpanStart < Start && token.Span.End > Start)
        {
            return "inside a string that spans lines";
        }
        var trivia = Root.FindTrivia(Start);
        if (trivia.SpanStart < Start && trivia.Span.End > Start)
        {
            if (trivia.IsKind(SyntaxKind.MultiLineCommentTrivia)
                || trivia.IsKind(
                    SyntaxKind.MultiLineDocumentationCommentTrivia))
            {
                return "inside a block comment";
            }
            if (trivia.IsKind(SyntaxKind.DisabledTextTrivia))
            {
                return "a preprocessor directive or disabled code";
            }
        }
        var interpolated = token.Parent;
        if (interpolated != null
            && token.SpanStart < Start
            && interpolated.FirstAncestorOrSelf<
                InterpolatedStringExpressionSyntax>() != null)
        {
            return "inside a string that spans lines";
        }
        return null;
    }

    /// True when the line holds a comment and nothing else: one // or
    /// /* */ comment, or one /// line of a doc comment (the parser makes the
    /// whole /// block one piece of trivia).
    public bool IsCommentOnly(out SyntaxTrivia comment)
    {
        comment = Root.FindTrivia(Start + Indent);
        if (comment.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia))
        {
            // The trivia's span starts behind its first "///".
            return comment.FullSpan.Start <= Start + Indent
                && comment.FullSpan.End >= End
                && Text.TrimStart().StartsWith("///", StringComparison.Ordinal);
        }
        if (comment.SpanStart != Start + Indent || comment.Span.End != End)
        {
            return false;
        }
        return comment.IsKind(SyntaxKind.SingleLineCommentTrivia)
            || comment.IsKind(SyntaxKind.MultiLineCommentTrivia);
    }

    public bool IsDirectiveOrDisabled()
    {
        var trivia = Root.FindTrivia(Start + Indent);
        return trivia.IsDirective
            || trivia.IsKind(SyntaxKind.DisabledTextTrivia);
    }

    /// The tokens that start and end on this line, outside interpolated
    /// strings, whose holes are never broken.
    public List<SyntaxToken> Tokens()
    {
        return Root.DescendantTokens(Line.Span)
            .Where(t => t.SpanStart >= Start && t.Span.End <= End)
            .Where(t => t.Span.Length > 0)
            .Where(t => !InInterpolation(t))
            .ToList();
    }

    public static bool InInterpolation(SyntaxToken token)
    {
        return token.Parent != null
            && token.Parent.FirstAncestorOrSelf<
                InterpolatedStringExpressionSyntax>() != null;
    }

    /// Where the code that would stay glued to this token ends: the token
    /// and any ";", ")", "]" or "," right behind it on the line.
    public int GluedEnd(SyntaxToken token)
    {
        var end = token.Span.End;
        var next = token.GetNextToken();
        while (next.Span.End <= End && next.SpanStart == end
            && (next.IsKind(SyntaxKind.SemicolonToken)
                || next.IsKind(SyntaxKind.CloseParenToken)
                || next.IsKind(SyntaxKind.CloseBracketToken)
                || next.IsKind(SyntaxKind.CommaToken)))
        {
            end = next.Span.End;
            next = next.GetNextToken();
        }
        return end;
    }

    /// The break before a binary operator, at the column its chain takes
    /// (ContinuationIndent).
    public string BreakFor(SyntaxToken op)
    {
        return "\n" + new string(' ', ContinuationIndent(op));
    }

    /// The break inside a string the stage splits: the new "+" joins the
    /// string's own + chain when it is in one.
    public string BreakForString(SyntaxToken literal)
    {
        if (literal.Parent?.Parent is BinaryExpressionSyntax chain
            && chain.IsKind(SyntaxKind.AddExpression))
        {
            return BreakFor(chain.OperatorToken);
        }
        return "\n" + new string(' ', ContinuationIndent(literal));
    }

    /// The column a continuation line that starts with token takes from the
    /// syntax, not from the line it was cut from: one level in from the line
    /// where the expression it continues starts, one more for each bracket
    /// around that expression opened on that same line, and one more for an
    /// operator that binds tighter than the chain it sits in. So the
    /// operands of one && chain, or the links of one call chain, share a
    /// column, and a nested group goes deeper than the chain around it.
    public int ContinuationIndent(SyntaxToken token)
    {
        return ContinuationIndent(token, n => IndentOf(Source.Lines[n]));
    }

    public static int IndentOf(TextLine line)
    {
        var text = line.ToString();
        return text.Length - text.TrimStart().Length;
    }

    /// ContinuationIndent with the indentation of each earlier line given
    /// by indentOf, for a pass that re-indents several lines at once.
    public int ContinuationIndent(SyntaxToken token, Func<int, int> indentOf)
    {
        int level = 0;
        SyntaxNode anchor = token.Parent;
        if (anchor is BinaryExpressionSyntax binary)
        {
            while (binary.Parent is BinaryExpressionSyntax outer)
            {
                if (Rank(outer.OperatorToken) != Rank(binary.OperatorToken))
                {
                    level++;
                }
                binary = outer;
            }
            anchor = binary;
        }
        else
        {
            while (anchor.Parent is MemberAccessExpressionSyntax
                    or InvocationExpressionSyntax
                    or ElementAccessExpressionSyntax
                && ChildIsHead(anchor))
            {
                anchor = anchor.Parent;
            }
        }
        var lines = Source.Lines;
        int home = lines.GetLineFromPosition(anchor.SpanStart).LineNumber;
        for (var node = anchor.Parent; node != null; node = node.Parent)
        {
            if (node is StatementSyntax || node is MemberDeclarationSyntax)
            {
                break;
            }
            var open = OpenOf(node);
            if (open == default)
            {
                continue;
            }
            var openLine = lines.GetLineFromPosition(open.SpanStart);
            if (openLine.LineNumber != home)
            {
                break;
            }
            // A bracket that starts its line holds what follows it at
            // that line's own level.
            if (open.SpanStart > openLine.Start + IndentOf(openLine))
            {
                level++;
            }
        }
        // A home line that itself continues a chain sits at that chain's
        // column already: what a bracket on it holds goes one level in from
        // it, not two.
        var lead = Root.FindToken(lines[home].Start + IndentOf(lines[home]));
        if (level > 0 && StartsContinuation(lead))
        {
            level--;
        }
        return indentOf(home) + Wrap.Step * (1 + level);
    }

    /// True when token, first on its line, continues an expression: a
    /// binary operator or a member access's dot.
    public static bool StartsContinuation(SyntaxToken token)
    {
        if (token.Parent is BinaryExpressionSyntax binary)
        {
            return binary.OperatorToken == token;
        }
        return token.IsKind(SyntaxKind.DotToken)
            && token.Parent is MemberAccessExpressionSyntax;
    }

    /// True when node is the receiver of its parent's member access, call
    /// or index, so the parent goes on with the same chain.
    static bool ChildIsHead(SyntaxNode node)
    {
        return node.Parent switch
        {
            MemberAccessExpressionSyntax m => m.Expression == node,
            InvocationExpressionSyntax i => i.Expression == node,
            ElementAccessExpressionSyntax e => e.Expression == node,
            _ => false,
        };
    }

    static SyntaxToken OpenOf(SyntaxNode node)
    {
        return node switch
        {
            ArgumentListSyntax a => a.OpenParenToken,
            BracketedArgumentListSyntax b => b.OpenBracketToken,
            AttributeArgumentListSyntax aa => aa.OpenParenToken,
            ParenthesizedExpressionSyntax p => p.OpenParenToken,
            TupleExpressionSyntax t => t.OpenParenToken,
            _ => default,
        };
    }

    /// How loosely an operator binds: operators of one rank are one chain.
    static int Rank(SyntaxToken op)
    {
        return op.Kind() switch
        {
            SyntaxKind.BarBarToken => 0,
            SyntaxKind.AmpersandAmpersandToken => 1,
            SyntaxKind.QuestionQuestionToken => 2,
            SyntaxKind.BarToken => 3,
            SyntaxKind.CaretToken => 4,
            SyntaxKind.AmpersandToken => 5,
            SyntaxKind.EqualsEqualsToken or SyntaxKind.ExclamationEqualsToken
                => 6,
            SyntaxKind.LessThanToken or SyntaxKind.GreaterThanToken
                or SyntaxKind.LessThanEqualsToken
                or SyntaxKind.GreaterThanEqualsToken => 7,
            SyntaxKind.LessThanLessThanToken
                or SyntaxKind.GreaterThanGreaterThanToken => 8,
            SyntaxKind.PlusToken or SyntaxKind.MinusToken => 9,
            _ => 10,
        };
    }

    /// The whitespace before a binary operator, replaced by BreakFor.
    public TextChange BreakBefore(SyntaxToken op)
    {
        var before = op.GetPreviousToken();
        return new TextChange(
            TextSpan.FromBounds(before.Span.End, op.SpanStart), BreakFor(op));
    }

    /// The whitespace between two tokens, replaced by a break one level in.
    public TextChange BreakBetween(SyntaxToken before, SyntaxToken after)
    {
        return new TextChange(
            TextSpan.FromBounds(before.Span.End, after.SpanStart), Inner);
    }

    /// True when nothing but whitespace sits between the tokens, so a break
    /// there loses no comment.
    public bool OnlySpaceBetween(SyntaxToken before, SyntaxToken after)
    {
        var gap = Source.ToString(
            TextSpan.FromBounds(before.Span.End, after.SpanStart));
        return gap.Trim().Length == 0 && !gap.Contains('\n');
    }
}
