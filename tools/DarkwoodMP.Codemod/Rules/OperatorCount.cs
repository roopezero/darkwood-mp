using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DarkwoodMP.Codemod.Rules;

/// The operators the rules remove.
enum Operator
{
    Ternary,
    Coalesce,
    CoalesceAssign,
    ConditionalAccess,
}

/// Counts the sites of each operator the way the census does: a chain
/// "a?.b?.c" is one site.
static class OperatorCount
{
    public static readonly Operator[] All =
    {
        Operator.Ternary,
        Operator.Coalesce,
        Operator.CoalesceAssign,
        Operator.ConditionalAccess,
    };

    public static string Label(Operator op)
    {
        switch (op)
        {
            case Operator.Ternary:
                return "?:";
            case Operator.Coalesce:
                return "??";
            case Operator.CoalesceAssign:
                return "??=";
            default:
                return "?.";
        }
    }

    /// One chain counts once: the links inside it are not sites.
    public static bool IsInnerLink(ConditionalAccessExpressionSyntax node)
    {
        return node.Parent is ConditionalAccessExpressionSyntax outer
            && outer.WhenNotNull == node;
    }

    /// Which operator a node is a site of, or null when it isn't one.
    public static Operator? KindOf(SyntaxNode node)
    {
        switch (node)
        {
            case ConditionalExpressionSyntax:
                return Operator.Ternary;
            case BinaryExpressionSyntax b
                    when b.IsKind(SyntaxKind.CoalesceExpression):
                return Operator.Coalesce;
            case AssignmentExpressionSyntax a
                    when a.IsKind(SyntaxKind.CoalesceAssignmentExpression):
                return Operator.CoalesceAssign;
            case ConditionalAccessExpressionSyntax c when !IsInnerLink(c):
                return Operator.ConditionalAccess;
            default:
                return null;
        }
    }

    /// The number of sites per operator, indexed by the enum's value.
    public static int[] Of(SyntaxNode root)
    {
        var counts = new int[All.Length];
        foreach (var node in root.DescendantNodes())
        {
            var kind = KindOf(node);
            if (kind != null)
            {
                counts[(int)kind.Value]++;
            }
        }
        return counts;
    }
}
