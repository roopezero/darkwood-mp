using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DarkwoodMP.Codemod.Format;

/// The last width rule: a plain string constant that crosses the limit is
/// split at a space into "a " + "b", which the compiler folds back into one
/// constant, so the DLL stays the same. The space stays at the end of the
/// first part. Verbatim and raw strings are never split (their line breaks
/// are part of the text) and neither are interpolated ones (stage 11).
static class WrapStrings
{
    public static LinePlan Plan(WrapLine line)
    {
        var token = Crossing(line);
        if (token == default)
        {
            return line.Leave("code with no place to break");
        }
        if (token.IsKind(SyntaxKind.InterpolatedStringEndToken))
        {
            return line.Leave("an interpolated string (stage 11)");
        }
        if (!token.IsKind(SyntaxKind.StringLiteralToken)
            && !token.IsKind(SyntaxKind.Utf8StringLiteralToken)
            && !token.IsKind(SyntaxKind.MultiLineRawStringLiteralToken)
            && !token.IsKind(SyntaxKind.SingleLineRawStringLiteralToken))
        {
            return line.Leave("code with no place to break");
        }
        var reason = Unsplittable(token);
        if (reason != null)
        {
            return line.Leave(reason);
        }
        var literal = (LiteralExpressionSyntax)token.Parent;
        var parens = NeedsParentheses(literal);
        int room = Wrap.Max - line.Col(token.SpanStart);
        if (parens)
        {
            room--;
        }
        int cut = Cut(token.Text, room);
        if (cut < 0)
        {
            return line.Leave("a string with no space to split at in time");
        }
        var at = token.SpanStart + cut;
        var edits = new List<TextChange>();
        if (parens)
        {
            edits.Add(new TextChange(new TextSpan(token.SpanStart, 0), "("));
        }
        var plus = SyntaxFactory.Token(SyntaxKind.PlusToken);
        edits.Add(new TextChange(
            new TextSpan(at, 0), "\"" + line.BreakFor(plus) + "+ \""));
        if (parens)
        {
            edits.Add(new TextChange(new TextSpan(token.Span.End, 0), ")"));
        }
        return line.Break("string constant split", edits);
    }

    /// The string on the line that crosses the limit, counting the ";", ")"
    /// or "," glued behind it: a literal token, or the end token of an
    /// interpolated string. Default when no string crosses.
    static SyntaxToken Crossing(WrapLine line)
    {
        int limit = line.Start + Wrap.Max;
        foreach (var node in line.Root.DescendantNodes(line.Line.Span))
        {
            if (node is InterpolatedStringExpressionSyntax s
                && s.SpanStart < limit
                && line.GluedEnd(s.StringEndToken) > limit)
            {
                return s.StringEndToken;
            }
        }
        foreach (var token in line.Root.DescendantTokens(line.Line.Span))
        {
            if (token.SpanStart >= limit || line.GluedEnd(token) <= limit)
            {
                continue;
            }
            if (token.IsKind(SyntaxKind.StringLiteralToken)
                || token.IsKind(SyntaxKind.Utf8StringLiteralToken)
                || token.IsKind(SyntaxKind.MultiLineRawStringLiteralToken)
                || token.IsKind(SyntaxKind.SingleLineRawStringLiteralToken))
            {
                return token;
            }
        }
        return default;
    }

    /// Why this literal can't be split, or null when it can.
    static string Unsplittable(SyntaxToken token)
    {
        var text = token.Text;
        if (text.StartsWith('@') || text.StartsWith("\"\"\"")
            || token.IsKind(SyntaxKind.MultiLineRawStringLiteralToken)
            || token.IsKind(SyntaxKind.SingleLineRawStringLiteralToken))
        {
            return "a verbatim or raw string, never split";
        }
        if (token.IsKind(SyntaxKind.Utf8StringLiteralToken)
            || !text.EndsWith('"'))
        {
            return "a UTF-8 string, never split";
        }
        if (!(token.Parent is LiteralExpressionSyntax))
        {
            return "a string outside an expression";
        }
        for (var node = token.Parent; node != null; node = node.Parent)
        {
            if (node is PatternSyntax || node is SwitchLabelSyntax)
            {
                return "a string in a pattern or a case label";
            }
            if (node is StatementSyntax || node is MemberDeclarationSyntax)
            {
                break;
            }
        }
        return null;
    }

    /// The place in the literal's text right after the last real space (not
    /// part of an escape) that keeps the first part and its closing quote
    /// within room columns, or -1. Both parts keep at least one character.
    public static int Cut(string text, int room)
    {
        int best = -1;
        for (int i = 1; i < text.Length - 1; i++)
        {
            if (text[i] == '\\')
            {
                i++;
                continue;
            }
            if (text[i] != ' ')
            {
                continue;
            }
            bool fits = i + 2 <= room;
            bool rest = i + 1 < text.Length - 1;
            if (fits && rest && i > 1)
            {
                best = i + 1;
            }
        }
        return best;
    }

    /// True unless the literal's parent keeps "a" + "b" together as one
    /// operand. As the receiver of a member access, "a" + "b".Trim() would
    /// trim the second part only.
    static bool NeedsParentheses(LiteralExpressionSyntax literal)
    {
        switch (literal.Parent)
        {
            case ArgumentSyntax:
            case AttributeArgumentSyntax:
            case EqualsValueClauseSyntax:
            case ReturnStatementSyntax:
            case ArrowExpressionClauseSyntax:
            case InitializerExpressionSyntax:
            case YieldStatementSyntax:
                return false;
            case AssignmentExpressionSyntax assignment:
                return assignment.Right != literal;
            case BinaryExpressionSyntax binary:
                return !binary.IsKind(SyntaxKind.AddExpression)
                    && !binary.IsKind(SyntaxKind.CoalesceExpression)
                    && !binary.IsKind(SyntaxKind.EqualsExpression)
                    && !binary.IsKind(SyntaxKind.NotEqualsExpression);
            default:
                return true;
        }
    }
}
