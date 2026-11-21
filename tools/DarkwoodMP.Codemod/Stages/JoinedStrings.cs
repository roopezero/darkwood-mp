using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DarkwoodMP.Codemod.Stages;

/// The wrap stage's code check for a file where it split a string: both
/// trees with every "a" + "b" of two plain string literals joined back into
/// one literal, and the parentheses around a lone literal dropped. The parts
/// are joined by their text, so the joined literal is the original one to the
/// character. Both trees get the same treatment, so a + that was already in
/// the code is joined on both sides.
static class JoinedStrings
{
    public static bool SameCode(SyntaxNode before, SyntaxNode after)
    {
        var joiner = new Joiner();
        return Stages.SameCode.Of(joiner.Visit(before), joiner.Visit(after));
    }

    /// The tree with every split string joined back, as SameCode compares
    /// it.
    public static SyntaxNode Joined(SyntaxNode root)
    {
        return new Joiner().Visit(root);
    }

    /// The number of plain string literals in a tree. A file where it grew
    /// had a string split.
    public static int Count(SyntaxNode root)
    {
        return root.DescendantTokens().Count(Plain);
    }

    static bool Plain(SyntaxToken token)
    {
        return token.IsKind(SyntaxKind.StringLiteralToken)
            && token.Text.StartsWith('"')
            && !token.Text.StartsWith("\"\"\"");
    }

    sealed class Joiner : CSharpSyntaxRewriter
    {
        public override SyntaxNode VisitBinaryExpression(
            BinaryExpressionSyntax node)
        {
            var visited =
                (BinaryExpressionSyntax)base.VisitBinaryExpression(node);
            if (!visited.IsKind(SyntaxKind.AddExpression)
                || !(visited.Right is LiteralExpressionSyntax right)
                || !Plain(right.Token))
            {
                return visited;
            }
            if (visited.Left is LiteralExpressionSyntax left
                && Plain(left.Token))
            {
                return Join(left, right);
            }
            if (visited.Left is BinaryExpressionSyntax inner
                && inner.IsKind(SyntaxKind.AddExpression)
                && inner.Right is LiteralExpressionSyntax end
                && Plain(end.Token))
            {
                return inner.WithRight(Join(end, right));
            }
            return visited;
        }

        public override SyntaxNode VisitParenthesizedExpression(
            ParenthesizedExpressionSyntax node)
        {
            var visited = (ParenthesizedExpressionSyntax)
                base.VisitParenthesizedExpression(node);
            if (visited.Expression is LiteralExpressionSyntax literal
                && Plain(literal.Token))
            {
                return literal;
            }
            return visited;
        }

        static LiteralExpressionSyntax Join(
            LiteralExpressionSyntax left, LiteralExpressionSyntax right)
        {
            var a = left.Token.Text;
            var b = right.Token.Text;
            var text = a.Substring(0, a.Length - 1) + b.Substring(1);
            var value = left.Token.ValueText + right.Token.ValueText;
            var token = SyntaxFactory.Literal(
                default, text, value, default);
            return SyntaxFactory.LiteralExpression(
                SyntaxKind.StringLiteralExpression, token);
        }
    }
}
