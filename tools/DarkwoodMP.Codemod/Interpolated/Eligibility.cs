using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DarkwoodMP.Codemod.Interpolated;

/// Which interpolated strings stage 11 must leave whole: those where "a" +
/// "b" isn't the same thing as the one string, or can't be written.
static class Eligibility
{
    /// Why the string stays whole, or null when it may be split.
    public static string WhyWhole(
        InterpolatedStringExpressionSyntax text, SemanticModel model)
    {
        var start = text.StringStartToken;
        if (text.ToString().Contains('\n'))
        {
            return "a verbatim or raw string with line breaks in it";
        }
        if (start.IsKind(SyntaxKind.InterpolatedSingleLineRawStringStartToken)
            || start.IsKind(SyntaxKind.InterpolatedMultiLineRawStringStartToken))
        {
            return "a raw string";
        }
        var place = Place(text);
        if (place != null)
        {
            return place;
        }
        var top = Top(text);
        foreach (var expression in new[] { text, top })
        {
            var info = model.GetTypeInfo(expression);
            if (info.Type?.SpecialType != SpecialType.System_String)
            {
                return "its type isn't string";
            }
            var converted = Converted(info.ConvertedType);
            if (converted != null)
            {
                return converted;
            }
        }
        if (top.Parent is BinaryExpressionSyntax binary
            && model.GetSymbolInfo(binary).Symbol is IMethodSymbol
            {
                MethodKind: MethodKind.UserDefinedOperator,
            })
        {
            return "an operand of a type's own operator, which + would change";
        }
        if (InExpressionTree(text, model))
        {
            return "in a lambda made into an expression tree, whose shape + "
                + "would change";
        }
        return null;
    }

    /// The places a string must keep its form: a constant, an attribute,
    /// a pattern, or a hole of another interpolated string.
    static string Place(SyntaxNode text)
    {
        foreach (var node in text.Ancestors())
        {
            switch (node)
            {
                case InterpolationSyntax:
                    return "inside a hole of another interpolated string";
                case AttributeSyntax:
                    return "an attribute argument";
                case PatternSyntax:
                case SwitchLabelSyntax:
                    return "a constant in a pattern or a case label";
                case ParameterSyntax:
                    return "a parameter's default value";
                case LocalDeclarationStatementSyntax local
                    when local.IsConst:
                    return "a const";
                case FieldDeclarationSyntax field
                    when field.Modifiers.Any(SyntaxKind.ConstKeyword):
                    return "a const";
                case StatementSyntax:
                case MemberDeclarationSyntax:
                    return null;
            }
        }
        return null;
    }

    /// The string with the parentheses around it and the + chain of string
    /// literals it is in: what the compiler converts as one.
    public static ExpressionSyntax Top(ExpressionSyntax text)
    {
        var top = text;
        while (true)
        {
            if (top.Parent is ParenthesizedExpressionSyntax parens)
            {
                top = parens;
                continue;
            }
            if (top.Parent is BinaryExpressionSyntax binary
                && binary.IsKind(SyntaxKind.AddExpression)
                && IsStringPart(binary.Left)
                && IsStringPart(binary.Right))
            {
                top = binary;
                continue;
            }
            return top;
        }
    }

    static bool IsStringPart(ExpressionSyntax expression)
    {
        switch (expression)
        {
            case ParenthesizedExpressionSyntax parens:
                return IsStringPart(parens.Expression);
            case InterpolatedStringExpressionSyntax:
                return true;
            case LiteralExpressionSyntax literal:
                return literal.IsKind(SyntaxKind.StringLiteralExpression);
            case BinaryExpressionSyntax binary:
                return binary.IsKind(SyntaxKind.AddExpression)
                    && IsStringPart(binary.Left)
                    && IsStringPart(binary.Right);
            default:
                return false;
        }
    }

    static string Converted(ITypeSymbol type)
    {
        if (type == null)
        {
            return null;
        }
        var name = type.ToDisplayString();
        if (name == "System.FormattableString")
        {
            return "converted to FormattableString";
        }
        if (name == "System.IFormattable")
        {
            return "converted to IFormattable";
        }
        foreach (var attribute in type.GetAttributes())
        {
            if (attribute.AttributeClass?.Name
                == "InterpolatedStringHandlerAttribute")
            {
                return "converted to an interpolated string handler type";
            }
        }
        return null;
    }

    static bool InExpressionTree(SyntaxNode text, SemanticModel model)
    {
        foreach (var lambda in text.Ancestors()
            .OfType<AnonymousFunctionExpressionSyntax>())
        {
            var type = model.GetTypeInfo(lambda).ConvertedType;
            if (type?.ContainingNamespace?.ToDisplayString()
                    == "System.Linq.Expressions"
                && type.Name == "Expression")
            {
                return true;
            }
        }
        return false;
    }
}
