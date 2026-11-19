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
/// anything there, in any build. A keyword is never a name. What a value's
/// own text suggests is in ValueNames.cs.
sealed partial class TempNames
{
    const string Fallback = "value";

    /// Anything in disabled text that could be an identifier.
    static readonly Regex Word = new Regex(@"[\p{L}_][\p{L}\p{Nd}_]*");

    /// The names .NET gives a parameter that takes any value, which say
    /// nothing about the value passed: ReferenceEquals(objA, objB).
    static readonly HashSet<string> Anything = new()
    {
        "obj", "objA", "objB", "value", "arg", "args", "item",
    };

    readonly HashSet<string> _taken = new();

    /// The names a new local could clash with or change the meaning of:
    /// all but the names after a dot, which are looked up in what is in
    /// front of the dot, and the names of tuple elements, arguments and
    /// property patterns (ElementName). Pick checks only these.
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
                if (!AfterDot(token) && !ElementName(token))
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

    /// The name of a tuple element ("(string type, bool exp)"), or a name
    /// in front of a colon: an argument's ("F(type: t)"), a tuple
    /// element's ("(type: t, exp: e)") or a property pattern's ("{ type:
    /// t }"). It is looked up in the tuple, the parameters of the method
    /// called or the type tested, so no local can clash with it.
    static bool ElementName(SyntaxToken token)
    {
        return token.Parent is TupleElementSyntax
            || token.Parent is IdentifierNameSyntax
            {
                Parent: NameColonSyntax,
            };
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

    /// A parameter's name that says what the value passed is: not one
    /// letter, not ending in a digit, as the parameters of Math.Max(val1,
    /// val2) do, and not one of the names .NET gives a parameter that takes
    /// any value, such as ReferenceEquals's objA.
    public static bool IsWord(string name)
    {
        return name.Length > 1 && !char.IsDigit(name[name.Length - 1])
            && !Anything.Contains(name);
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

    /// Drops leading underscores and lowers the first word; a keyword gets
    /// a suffix.
    static string Clean(string name)
    {
        var lowered = Lowered(name);
        if (lowered == null)
        {
            return Fallback;
        }
        if (IsAnyKeyword(lowered))
        {
            return lowered + "Value";
        }
        return lowered;
    }

    /// The name without leading underscores, its first word in small
    /// letters, or null when it isn't a name.
    static string Lowered(string name)
    {
        name = name.TrimStart('_');
        if (name.Length == 0 || !SyntaxFacts.IsValidIdentifier(name))
        {
            return null;
        }
        return LowerStart(name);
    }

    static bool IsAnyKeyword(string name)
    {
        return SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None
            || SyntaxFacts.GetContextualKeywordKind(name) != SyntaxKind.None;
    }

    /// The first word in small letters: "Mask" gives "mask", and a run of
    /// capitals in front goes small as a whole ("IPv4Mask" gives
    /// "ipv4Mask", "NPCs" "npcs", "ID" "id"), but for its last capital
    /// when that starts the next word ("IPAddress" gives "ipAddress").
    static string LowerStart(string name)
    {
        int run = 0;
        while (run < name.Length && char.IsUpper(name[run]))
        {
            run++;
        }
        int lowered = run;
        if (run > 1 && run < name.Length && char.IsLower(name[run])
            && !RunGoesOn(name.Substring(run)))
        {
            lowered = run - 1;
        }
        return name.Substring(0, lowered).ToLowerInvariant()
            + name.Substring(lowered);
    }

    /// The small letters after a run of capitals are still part of it: a
    /// plural s ("NPCs") or a version ("IPv4").
    static bool RunGoesOn(string rest)
    {
        if (rest.Length > 1 && rest[0] == 'v' && char.IsDigit(rest[1]))
        {
            return true;
        }
        return rest[0] == 's' && (rest.Length == 1 || !char.IsLower(rest[1]));
    }
}
