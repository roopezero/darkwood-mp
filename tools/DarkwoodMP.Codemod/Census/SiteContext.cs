using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DarkwoodMP.Codemod.Census;

/// Where an operator sits in its statement.
static class SiteContext
{
    public static bool IsDirect(string context)
    {
        return context is "local-init" or "assign" or "return"
            or "yield-return" or "arrow-body" or "lambda-body" or "field-init"
            or "property-init" or "compound-assign" or "expression-statement";
    }

    public static string Of(SyntaxNode expression)
    {
        SyntaxNode node = expression;
        while (node.Parent is ParenthesizedExpressionSyntax)
        {
            node = node.Parent;
        }
        var parent = node.Parent;
        switch (parent)
        {
            case EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax { Parent: VariableDeclarationSyntax { Parent: LocalDeclarationStatementSyntax } } }:
                return "local-init";
            case EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax { Parent: VariableDeclarationSyntax { Parent: FieldDeclarationSyntax } } }:
                return "field-init";
            case EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax { Parent: VariableDeclarationSyntax { Parent: ForStatementSyntax } } }:
                return "for-init";
            case EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax { Parent: VariableDeclarationSyntax { Parent: UsingStatementSyntax } } }:
                return "using-resource";
            case EqualsValueClauseSyntax { Parent: PropertyDeclarationSyntax }:
                return "property-init";
            case EqualsValueClauseSyntax { Parent: ParameterSyntax }:
                return "default-parameter";
            case AssignmentExpressionSyntax a when a.Right == node:
                return Assignment(a);
            case ExpressionStatementSyntax:
                return "expression-statement";
            case ReturnStatementSyntax:
                return "return";
            case YieldStatementSyntax:
                return "yield-return";
            case ThrowStatementSyntax:
                return "throw";
            case ArrowExpressionClauseSyntax:
                return "arrow-body";
            case LambdaExpressionSyntax:
                return "lambda-body";
            case ArgumentSyntax argument:
                if (argument.Parent?.Parent is ObjectCreationExpressionSyntax)
                {
                    return "argument (new)";
                }
                return "argument";
            case InterpolationSyntax:
                return "interpolation";
            case BinaryExpressionSyntax b:
                return Binary(b, node);
            case ConditionalExpressionSyntax c:
                if (c.Condition == node)
                {
                    return "?: condition";
                }
                return "?: branch";
            case IfStatementSyntax:
                return "if-condition";
            case WhileStatementSyntax:
                return "while-condition";
            case DoStatementSyntax:
                return "do-condition";
            case ForStatementSyntax:
                return "for-header";
            case ForEachStatementSyntax:
                return "foreach-source";
            case SwitchStatementSyntax:
                return "switch-on";
            case SwitchExpressionSyntax:
                return "switch-expression-on";
            case SwitchExpressionArmSyntax:
                return "switch-arm";
            case InitializerExpressionSyntax:
                return "collection-initializer";
            case AnonymousObjectMemberDeclaratorSyntax:
                return "anonymous-object";
            case MemberAccessExpressionSyntax:
                return "receiver of .";
            case ElementAccessExpressionSyntax:
                return "receiver of []";
            case BracketedArgumentListSyntax:
                return "index";
            case CastExpressionSyntax:
                return "cast";
            case PrefixUnaryExpressionSyntax:
                return "unary";
            case IsPatternExpressionSyntax:
                return "is-pattern";
            case TupleExpressionSyntax:
                return "tuple";
            case WhenClauseSyntax:
                return "when-clause";
            case UsingStatementSyntax:
                return "using-resource";
            case LockStatementSyntax:
                return "lock";
            case null:
                return "other: null";
            default:
                return "other: " + parent.Kind();
        }
    }

    static string Assignment(AssignmentExpressionSyntax assignment)
    {
        if (assignment.Parent is InitializerExpressionSyntax)
        {
            return "object-initializer";
        }
        if (assignment.Parent is ExpressionStatementSyntax)
        {
            if (assignment.IsKind(SyntaxKind.SimpleAssignmentExpression))
            {
                return "assign";
            }
            return "compound-assign";
        }
        return "assign-nested";
    }

    static string Binary(BinaryExpressionSyntax binary, SyntaxNode node)
    {
        if (binary.IsKind(SyntaxKind.CoalesceExpression))
        {
            if (binary.Left == node)
            {
                return "?? left";
            }
            return "?? right";
        }
        if (binary.IsKind(SyntaxKind.LogicalAndExpression)
            || binary.IsKind(SyntaxKind.LogicalOrExpression))
        {
            return "&&/|| operand";
        }
        if (binary.IsKind(SyntaxKind.AddExpression))
        {
            return "+ operand";
        }
        if (binary.IsKind(SyntaxKind.EqualsExpression)
            || binary.IsKind(SyntaxKind.NotEqualsExpression))
        {
            return "==/!= operand";
        }
        return "other binary operand";
    }
}
