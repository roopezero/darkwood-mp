using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DarkwoodMP.Codemod.Census;

/// How an operator site can become statements, and what must be true for that.
static class RewriteKind
{
    public static string Of(ExpressionSyntax site)
    {
        SyntaxNode child = site;
        for (var parent = site.Parent; parent != null; child = parent, parent = parent.Parent)
        {
            switch (parent)
            {
                case ParenthesizedExpressionSyntax:
                    continue;
                case BinaryExpressionSyntax b when b.Right == child
                        && (b.IsKind(SyntaxKind.LogicalAndExpression)
                            || b.IsKind(SyntaxKind.LogicalOrExpression)
                            || b.IsKind(SyntaxKind.CoalesceExpression)):
                    return "conditionally evaluated: right of &&, || or ??";
                case ConditionalExpressionSyntax c when c.Condition != child:
                    return "conditionally evaluated: branch of ?:";
                case ConditionalAccessExpressionSyntax a when a.WhenNotNull == child:
                    return "conditionally evaluated: after ?.";
                case AnonymousFunctionExpressionSyntax lambda:
                    if (lambda.Body == child)
                    {
                        return "lambda with an expression body: make it a block first";
                    }
                    return "other";
                case ArrowExpressionClauseSyntax:
                    if (parent.Parent is LocalFunctionStatementSyntax)
                    {
                        return "expression-bodied local function: make it a block first";
                    }
                    return "expression-bodied member: make it a block first";
                case EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax { Parent: VariableDeclarationSyntax { Parent: FieldDeclarationSyntax } } }:
                    return "field initializer: needs a helper method";
                case EqualsValueClauseSyntax { Parent: PropertyDeclarationSyntax }:
                    return "property initializer: needs a helper method";
                case EqualsValueClauseSyntax { Parent: ParameterSyntax }:
                    return "default parameter (constant)";
                case AttributeArgumentSyntax:
                    return "attribute argument (constant)";
                case SwitchExpressionArmSyntax:
                    return "switch expression arm";
                case InitializerExpressionSyntax:
                    return "object or collection initializer (runs after new)";
                case WhileStatementSyntax:
                case DoStatementSyntax:
                    return "loop condition";
                case ForStatementSyntax f:
                    if (f.Declaration != null && f.Declaration.DescendantNodes().Contains(child))
                    {
                        return "for initializer";
                    }
                    return "for condition or iterator";
                case ElseClauseSyntax:
                    return "else-if condition";
                case IfStatementSyntax i when i.Parent is ElseClauseSyntax:
                    return "else-if condition";
                case StatementSyntax statement:
                    return Direct(site);
            }
            if (!EarlierOperandsPure(child, parent))
            {
                return "embedded: a call runs before it, needs spilling";
            }
        }
        return "other";
    }

    static string Direct(ExpressionSyntax site)
    {
        SyntaxNode node = site;
        while (node.Parent is ParenthesizedExpressionSyntax)
        {
            node = node.Parent;
        }
        switch (node.Parent)
        {
            case EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax }:
                return "direct: local declaration";
            case AssignmentExpressionSyntax a when a.Right == node && a.Parent is ExpressionStatementSyntax:
                if (a.Left is IdentifierNameSyntax)
                {
                    return "direct: assignment to a simple name";
                }
                return "direct: assignment to a longer target";
            case ReturnStatementSyntax:
                return "direct: return";
            case YieldStatementSyntax:
                return "direct: yield return";
            case ExpressionStatementSyntax:
                return "direct: statement";
            case ThrowStatementSyntax:
                return "direct: throw";
            case ForEachStatementSyntax:
                return "direct: foreach source";
            case IfStatementSyntax:
                return "direct: if condition";
            case InterpolationSyntax:
                return "embedded, hoistable: interpolation hole";
        }
        return "embedded, hoistable: operand or argument";
    }

    static bool EarlierOperandsPure(SyntaxNode child, SyntaxNode parent)
    {
        IEnumerable<SyntaxNode> earlier = parent switch
        {
            BinaryExpressionSyntax b when b.Right == child => new SyntaxNode[] { b.Left },
            ArgumentListSyntax a => a.Arguments.TakeWhile(x => x != child),
            BracketedArgumentListSyntax a => a.Arguments.TakeWhile(x => x != child),
            InvocationExpressionSyntax i when i.ArgumentList == child => new SyntaxNode[] { i.Expression },
            ElementAccessExpressionSyntax e when e.ArgumentList == child => new SyntaxNode[] { e.Expression },
            AssignmentExpressionSyntax a when a.Right == child => new SyntaxNode[] { a.Left },
            InterpolatedStringExpressionSyntax s => s.Contents.TakeWhile(x => x != child).OfType<InterpolationSyntax>(),
            TupleExpressionSyntax t => t.Arguments.TakeWhile(x => x != child),
            _ => Array.Empty<SyntaxNode>(),
        };
        return earlier.All(Pure);
    }

    /// Syntax only: a getter counts as pure here, as it did in the prototype.
    /// Stage 4 has to close that gap (see the guide's gap table).
    static bool Pure(SyntaxNode expression)
    {
        foreach (var node in expression.DescendantNodesAndSelf())
        {
            if (node is InvocationExpressionSyntax invocation)
            {
                if (invocation.Expression.ToString() == "nameof")
                {
                    continue;
                }
                return false;
            }
            if (node is ObjectCreationExpressionSyntax
                || node is ImplicitObjectCreationExpressionSyntax
                || node is AssignmentExpressionSyntax
                || node is AwaitExpressionSyntax)
            {
                return false;
            }
            if (node is PrefixUnaryExpressionSyntax prefix
                && (prefix.IsKind(SyntaxKind.PreIncrementExpression)
                    || prefix.IsKind(SyntaxKind.PreDecrementExpression)))
            {
                return false;
            }
            if (node is PostfixUnaryExpressionSyntax
                && !node.IsKind(SyntaxKind.SuppressNullableWarningExpression))
            {
                return false;
            }
        }
        return true;
    }
}
