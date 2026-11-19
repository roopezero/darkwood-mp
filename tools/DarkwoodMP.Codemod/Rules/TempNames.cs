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

    /// The methods whose value is one element of what they are called on.
    static readonly HashSet<string> Elements = new()
    {
        "First", "FirstOrDefault", "Last", "LastOrDefault", "Single",
        "SingleOrDefault", "ElementAt", "ElementAtOrDefault",
    };

    readonly HashSet<string> _taken = new();

    /// The names a new local could clash with or change the meaning of:
    /// all but the names after a dot, which are looked up in what is in
    /// front of the dot. Pick checks only these.
    readonly HashSet<string> _simple = new();

    /// The names this member's temporaries got.
    readonly HashSet<string> _made = new();

    public TempNames(SyntaxNode member)
    {
        foreach (var token in member.DescendantTokens())
        {
            if (token.IsKind(SyntaxKind.IdentifierToken))
            {
                _taken.Add(token.ValueText);
                if (!AfterDot(token))
                {
                    _simple.Add(token.ValueText);
                }
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
                    _simple.Add(word.Value);
                }
            }
        }
        foreach (var node in member.AncestorsAndSelf())
        {
            if (node is AccessorDeclarationSyntax
                || node is BasePropertyDeclarationSyntax)
            {
                _taken.Add("value");
                _simple.Add("value");
            }
        }
    }

    static bool AfterDot(SyntaxToken token)
    {
        if (token.Parent is not SimpleNameSyntax name)
        {
            return false;
        }
        switch (name.Parent)
        {
            case MemberAccessExpressionSyntax access:
                return access.Name == name;
            case MemberBindingExpressionSyntax binding:
                return binding.Name == name;
            case QualifiedNameSyntax qualified:
                return qualified.Right == name;
            default:
                return false;
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
        return Take(name);
    }

    /// The hints in order, "value" last: the first that is free, or that
    /// another temporary of the member or one of the related names has,
    /// and then gets a number, as a second version of it. A hint that some
    /// other variable or member has is passed over, because a number would
    /// make it read as a copy of that; when every hint is one of those,
    /// the first gets the number.
    public string Pick(List<string> hints, List<string> related)
    {
        var words = hints.Distinct()
            .OrderBy(word => word == Fallback)
            .ToList();
        if (words.Count == 0)
        {
            words.Add(Fallback);
        }
        foreach (var word in words)
        {
            if (!_simple.Contains(word) && !IsKeyword(word))
            {
                return Take(word);
            }
            if (_made.Contains(word) || related.Contains(word))
            {
                return Number(word);
            }
        }
        return Number(words[0]);
    }

    /// Not one letter and not ending in a digit, as the parameters of
    /// Math.Max(val1, val2) do.
    public static bool IsWord(string name)
    {
        return name.Length > 1 && !char.IsDigit(name[name.Length - 1]);
    }

    string Number(string word)
    {
        var name = word + 2;
        for (int i = 3; _simple.Contains(name) || IsKeyword(name); i++)
        {
            name = word + i;
        }
        return Take(name);
    }

    string Take(string name)
    {
        _taken.Add(name);
        _simple.Add(name);
        _made.Add(name);
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

    /// The name the value itself suggests, for Pick: a call is named
    /// after its method without a leading get ("getObjects()" gives
    /// "objects"), and one that picks an element after what the element
    /// is ("Select(f => f.type).First()" gives "type", and a call that
    /// returns a Slot "slot"); anything else after its last identifier.
    public static string Named(ExpressionSyntax expression, SemanticModel model)
    {
        switch (expression)
        {
            case ParenthesizedExpressionSyntax p:
                return Named(p.Expression, model);
            case ConditionalAccessExpressionSyntax access:
                return Named(access.WhenNotNull, model);
            case InvocationExpressionSyntax call:
                var method = LastIdentifier(call.Expression);
                if (Elements.Contains(method))
                {
                    return CleanName(Element(call, model));
                }
                return CleanName(Verbless(method));
            default:
                return CleanName(LastIdentifier(expression));
        }
    }

    /// Clean, but "type" stays "type": it is a keyword only in front of
    /// an attribute ("[type: ...]"), where no local can be.
    static string CleanName(string name)
    {
        var clean = Clean(name);
        if (clean == "typeValue" && name == "type")
        {
            return name;
        }
        return clean;
    }

    static string Element(InvocationExpressionSyntax call, SemanticModel model)
    {
        if (call.Expression is MemberAccessExpressionSyntax
            {
                Expression: InvocationExpressionSyntax select,
            }
            && LastIdentifier(select.Expression) == "Select"
            && select.ArgumentList.Arguments.Count == 1
            && select.ArgumentList.Arguments[0].Expression
                is AnonymousFunctionExpressionSyntax
                {
                    ExpressionBody: not null,
                } selector)
        {
            return LastIdentifier(selector.ExpressionBody);
        }
        if (model.GetTypeInfo(call).Type is INamedTypeSymbol
            {
                SpecialType: SpecialType.None,
                IsTupleType: false,
                TypeArguments.Length: 0,
            } type)
        {
            return type.Name;
        }
        return Fallback;
    }

    /// "getObjects" as "Objects"; any other name as it is.
    static string Verbless(string method)
    {
        if (method.Length > 3
            && (method.StartsWith("get") || method.StartsWith("Get"))
            && char.IsUpper(method[3]))
        {
            return method.Substring(3);
        }
        return method;
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

    /// The words two names end with, as a name: "runs" for "storyStepRuns"
    /// and "runs", "isReady" and "ready" give "ready". Null when the last
    /// words differ.
    public static string CommonEnd(string a, string b)
    {
        var first = Words(a);
        var second = Words(b);
        int common = 0;
        while (common < first.Count && common < second.Count
            && string.Equals(
                first[first.Count - 1 - common],
                second[second.Count - 1 - common],
                StringComparison.OrdinalIgnoreCase))
        {
            common++;
        }
        if (common == 0)
        {
            return null;
        }
        var end = string.Concat(first.Skip(first.Count - common));
        return Clean(end);
    }

    /// "storyStepRuns" as "story", "Step", "Runs": a word starts at a
    /// capital that follows a small letter or a digit.
    static List<string> Words(string name)
    {
        var words = new List<string>();
        int start = 0;
        for (int i = 1; i < name.Length; i++)
        {
            if (char.IsUpper(name[i])
                && (char.IsLower(name[i - 1]) || char.IsDigit(name[i - 1])))
            {
                words.Add(name.Substring(start, i - start));
                start = i;
            }
        }
        words.Add(name.Substring(start));
        return words;
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
