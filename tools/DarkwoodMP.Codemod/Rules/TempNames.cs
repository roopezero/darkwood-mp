using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace DarkwoodMP.Codemod.Rules;

/// Names for temporaries in one member. A name comes from what the value is
/// (codemod.md section 7.4) and is unique across the whole member: every
/// identifier in it counts as taken, in all switch sections and lambdas and
/// in code an #if leaves out, so a new local can never clash with or shadow
/// anything there, in any build. A keyword is never a name.
sealed class TempNames
{
    const string Fallback = "value";

    /// Anything in disabled text that could be an identifier.
    static readonly Regex Word = new Regex(@"[\p{L}_][\p{L}\p{Nd}_]*");

    readonly HashSet<string> _taken = new();

    public TempNames(SyntaxNode member)
    {
        foreach (var token in member.DescendantTokens())
        {
            if (token.IsKind(SyntaxKind.IdentifierToken))
            {
                _taken.Add(token.ValueText);
            }
        }
        // The parser keeps code that an #if leaves out as text, not tokens.
        foreach (var trivia in member.DescendantTrivia())
        {
            if (trivia.IsKind(SyntaxKind.DisabledTextTrivia))
            {
                foreach (Match word in Word.Matches(trivia.ToString()))
                {
                    _taken.Add(word.Value);
                }
            }
        }
        foreach (var node in member.AncestorsAndSelf())
        {
            if (node is AccessorDeclarationSyntax
                || node is BasePropertyDeclarationSyntax)
            {
                _taken.Add("value");
            }
        }
    }

    /// A name of its own for a temporary that holds this expression.
    public string For(ExpressionSyntax expression, SemanticModel model)
    {
        return Unique(Hint(expression, model));
    }

    /// The hint, or the hint with a number when it is taken.
    public string Unique(string hint)
    {
        var name = hint;
        for (int i = 2; _taken.Contains(name) || IsKeyword(name); i++)
        {
            name = hint + i;
        }
        _taken.Add(name);
        return name;
    }

    static bool IsKeyword(string name)
    {
        return SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None;
    }

    /// The name the value suggests: the parameter it is passed to, "text" in
    /// an interpolation hole, the declared name, or the last identifier of
    /// the expression ("_foo" becomes "foo", "X.Instance" becomes "instance").
    public static string Hint(ExpressionSyntax expression, SemanticModel model)
    {
        SyntaxNode node = expression;
        while (node.Parent is ParenthesizedExpressionSyntax)
        {
            node = node.Parent;
        }
        switch (node.Parent)
        {
            case ArgumentSyntax argument:
                if (model.GetOperation(argument) is IArgumentOperation
                    {
                        Parameter: not null,
                    } passed)
                {
                    return Clean(passed.Parameter.Name);
                }
                break;
            case InterpolationSyntax:
                return "text";
            case EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax v }:
                return Clean(v.Identifier.ValueText);
        }
        return Clean(LastIdentifier(expression));
    }

    static string LastIdentifier(ExpressionSyntax expression)
    {
        switch (expression)
        {
            case ParenthesizedExpressionSyntax p:
                return LastIdentifier(p.Expression);
            case IdentifierNameSyntax name:
                return name.Identifier.ValueText;
            case GenericNameSyntax generic:
                return generic.Identifier.ValueText;
            case MemberAccessExpressionSyntax member:
                return member.Name.Identifier.ValueText;
            case MemberBindingExpressionSyntax binding:
                return binding.Name.Identifier.ValueText;
            case InvocationExpressionSyntax call:
                return LastIdentifier(call.Expression);
            case ElementAccessExpressionSyntax element:
                return LastIdentifier(element.Expression);
            case ConditionalAccessExpressionSyntax access:
                return LastIdentifier(access.WhenNotNull);
            default:
                return Fallback;
        }
    }

    /// Drops leading underscores and lowers the first letter; a keyword gets
    /// a suffix.
    static string Clean(string name)
    {
        name = name.TrimStart('_');
        if (name.Length == 0 || !SyntaxFacts.IsValidIdentifier(name))
        {
            return Fallback;
        }
        name = char.ToLowerInvariant(name[0]) + name.Substring(1);
        if (SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None
            || SyntaxFacts.GetContextualKeywordKind(name) != SyntaxKind.None)
        {
            return name + "Value";
        }
        return name;
    }
}
