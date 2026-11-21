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

    /// The break before a binary operator: one level in, or at this line's
    /// own indentation when the line already starts with an operator of the
    /// same kind, so a long chain of + or && stays in one column.
    public string BreakFor(SyntaxToken op)
    {
        var first = Root.FindToken(Start + Indent);
        if (first.SpanStart == Start + Indent && first.IsKind(op.Kind())
            && first.Parent is BinaryExpressionSyntax)
        {
            return "\n" + Pad;
        }
        return Inner;
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
