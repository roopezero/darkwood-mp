using System.Text;
using DarkwoodMP.Codemod.Stages;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DarkwoodMP.Codemod.Interpolated;

/// Stage 11's gate on the text: in both trees every string, plain or
/// interpolated, becomes one literal that spells out what it holds (its
/// literal text by value, and each hole with its expression, alignment and
/// format, in order), the operands of each + chain are flattened, and
/// neighbouring strings in a chain are joined. Parentheses around a lone
/// string go. The trees must then be the same code, so each split string's
/// parts, joined, are the original: the same text and the same holes in
/// the same order.
static class JoinedParts
{
    const char Hole = '\u0001';
    const char Align = '\u0002';
    const char Form = '\u0003';
    const char End = '\u0004';

    public static bool SameCode(SyntaxNode before, SyntaxNode after)
    {
        var joiner = new Joiner();
        return Stages.SameCode.Of(joiner.Visit(before), joiner.Visit(after));
    }

    /// The tree as the gate compares it, for the comment places check.
    public static SyntaxNode Joined(SyntaxNode root)
    {
        return new Joiner().Visit(root);
    }

    /// The first token where the joined trees differ, for the message.
    public static string FirstDifference(SyntaxNode before, SyntaxNode after)
    {
        var joiner = new Joiner();
        return TokenDiff.First(joiner.Visit(before), joiner.Visit(after));
    }

    static LiteralExpressionSyntax Canonical(string spelled)
    {
        return SyntaxFactory.LiteralExpression(
            SyntaxKind.StringLiteralExpression,
            SyntaxFactory.Literal(spelled));
    }

    static bool IsString(ExpressionSyntax expression)
    {
        return expression is LiteralExpressionSyntax literal
            && literal.IsKind(SyntaxKind.StringLiteralExpression);
    }

    sealed class Joiner : CSharpSyntaxRewriter
    {
        public override SyntaxNode VisitLiteralExpression(
            LiteralExpressionSyntax node)
        {
            if (!node.IsKind(SyntaxKind.StringLiteralExpression)
                || node.Token.IsKind(SyntaxKind.Utf8StringLiteralToken))
            {
                return base.VisitLiteralExpression(node);
            }
            return Canonical(node.Token.ValueText).WithTriviaFrom(node);
        }

        public override SyntaxNode VisitInterpolatedStringExpression(
            InterpolatedStringExpressionSyntax node)
        {
            var spelled = new StringBuilder();
            foreach (var content in node.Contents)
            {
                if (content is InterpolatedStringTextSyntax text)
                {
                    // The token's value keeps a brace doubled ("{{"),
                    // which the string holds once.
                    spelled.Append(text.TextToken.ValueText
                        .Replace("{{", "{")
                        .Replace("}}", "}"));
                    continue;
                }
                var hole = (InterpolationSyntax)content;
                var expression = (ExpressionSyntax)Visit(hole.Expression);
                spelled.Append(Hole)
                    .Append(expression.NormalizeWhitespace().ToFullString())
                    .Append(Align);
                if (hole.AlignmentClause != null)
                {
                    spelled.Append(
                        hole.AlignmentClause.Value.NormalizeWhitespace());
                }
                spelled.Append(Form);
                if (hole.FormatClause != null)
                {
                    spelled.Append(hole.FormatClause.FormatStringToken.Text);
                }
                spelled.Append(End);
            }
            return Canonical(spelled.ToString()).WithTriviaFrom(node);
        }

        public override SyntaxNode VisitParenthesizedExpression(
            ParenthesizedExpressionSyntax node)
        {
            var inner = (ExpressionSyntax)Visit(node.Expression);
            if (IsString(inner))
            {
                return inner.WithTriviaFrom(node);
            }
            return node.WithExpression(inner);
        }

        public override SyntaxNode VisitBinaryExpression(
            BinaryExpressionSyntax node)
        {
            if (!node.IsKind(SyntaxKind.AddExpression))
            {
                return base.VisitBinaryExpression(node);
            }
            var operands = new List<ExpressionSyntax>();
            Flatten(node, operands);
            var joined = new List<ExpressionSyntax>();
            foreach (var operand in operands)
            {
                var visited = (ExpressionSyntax)Visit(operand);
                if (joined.Count > 0 && IsString(visited)
                    && IsString(joined[^1]))
                {
                    var last = (LiteralExpressionSyntax)joined[^1];
                    var next = (LiteralExpressionSyntax)visited;
                    joined[^1] = Canonical(
                            last.Token.ValueText + next.Token.ValueText)
                        .WithLeadingTrivia(last.GetLeadingTrivia())
                        .WithTrailingTrivia(next.GetTrailingTrivia());
                    continue;
                }
                joined.Add(visited);
            }
            var chain = joined[0];
            for (int i = 1; i < joined.Count; i++)
            {
                chain = SyntaxFactory.BinaryExpression(
                    SyntaxKind.AddExpression, chain, joined[i]);
            }
            return chain;
        }

        /// The operands of a left-leaning + chain, with a parenthesised
        /// chain of strings taken in, because stage 11 adds those
        /// parentheses.
        static void Flatten(ExpressionSyntax node, List<ExpressionSyntax> into)
        {
            if (node is BinaryExpressionSyntax binary
                && binary.IsKind(SyntaxKind.AddExpression))
            {
                Flatten(binary.Left, into);
                Flatten(binary.Right, into);
                return;
            }
            if (node is ParenthesizedExpressionSyntax parens
                && OnlyStrings(parens.Expression))
            {
                Flatten(parens.Expression, into);
                return;
            }
            into.Add(node);
        }

        static bool OnlyStrings(ExpressionSyntax node)
        {
            switch (node)
            {
                case ParenthesizedExpressionSyntax parens:
                    return OnlyStrings(parens.Expression);
                case BinaryExpressionSyntax binary
                    when binary.IsKind(SyntaxKind.AddExpression):
                    return OnlyStrings(binary.Left)
                        && OnlyStrings(binary.Right);
                case InterpolatedStringExpressionSyntax:
                    return true;
                case LiteralExpressionSyntax literal:
                    return literal.IsKind(SyntaxKind.StringLiteralExpression);
                default:
                    return false;
            }
        }
    }
}
