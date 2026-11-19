using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DarkwoodMP.Codemod.Rules;

/// What a value's own text suggests as its temporary's name (Named): a
/// call after what it gives, an element after what it is, anything else
/// after its last identifier. A name reads as a noun for the value, never
/// as a method: "record" for Get(slot), not "getValue".
sealed partial class TempNames
{
    /// The methods whose value is one element of what they are called on.
    static readonly HashSet<string> Elements = new()
    {
        "First", "FirstOrDefault", "Last", "LastOrDefault", "Single",
        "SingleOrDefault", "ElementAt", "ElementAtOrDefault",
    };

    /// Contextual keywords that are keywords only in front of an attribute
    /// ("[method: ...]") or in a type's declaration ("record R"), where no
    /// local can be, so a local may have the name.
    static readonly HashSet<string> Locals = new()
    {
        "type", "method", "param", "property", "module", "assembly",
        "record",
    };

    /// The first words of the names of methods named for what they do, not
    /// for what they give: their value is named after the rest of the name
    /// or its type. A "Get" here is one with nothing after it, as "Get(slot)"
    /// (Noun takes the rest of a get's name). "To" converts: "ToArray()"
    /// gives "array".
    static readonly HashSet<string> Verbs =
        new(StringComparer.OrdinalIgnoreCase)
    {
        "Get", "Find", "Make", "Create", "Build", "Load", "Read", "Parse",
        "Deserialize", "Spawn", "Clone", "Instantiate", "Fetch", "Take",
        "Pop", "Peek", "Dequeue", "Pick", "To",
    };

    /// The string methods that trim: their value is the text, trimmed.
    static readonly HashSet<string> Trims = new()
    {
        "Trim", "TrimStart", "TrimEnd",
    };

    /// The last words of the name of a field or property found by
    /// reflection, which the value it reads doesn't have: the value of
    /// SawField is the saw.
    static readonly string[] Reflected = { "Field", "Property", "Info" };

    /// The name the value itself suggests, for Pick.
    public static string Named(ExpressionSyntax expression, SemanticModel model)
    {
        switch (expression)
        {
            case ParenthesizedExpressionSyntax p:
                return Named(p.Expression, model);
            case ConditionalAccessExpressionSyntax access:
                return Named(access.WhenNotNull, model);
            case InvocationExpressionSyntax call:
                return Called(call, model);
            case ElementAccessExpressionSyntax element:
                return ElementName(
                    element.Expression, model.GetTypeInfo(element).Type, model);
            case ElementBindingExpressionSyntax binding:
                return ElementName(
                    BoundTo(binding), model.GetTypeInfo(binding).Type, model);
            default:
                return CleanName(LastIdentifier(expression));
        }
    }

    /// A call after what it gives: an element it picks after what the
    /// element is ("Select(f => f.type).First()" gives "type"), a get call
    /// after what it gets ("getObjects()" gives "objects", and
    /// "SawField.GetValue(x)" "saw"), a trimmed text after the text
    /// ("line.Trim()" gives "trimmedLine"), another call that makes a
    /// string of a string after that string, and a call whose method is
    /// named for what it does after the rest of the name ("FindTarget()"
    /// gives "target") or the type of its value ("Get(slot)" of a Record
    /// gives "record", "DeserializeObject<SaveState>(s)" "saveState": an
    /// object is a keyword) or "text" for a string. Any other call is
    /// named after its method ("a.Child()" gives "child").
    static string Called(InvocationExpressionSyntax call, SemanticModel model)
    {
        var method = LastIdentifier(call.Expression);
        if (Elements.Contains(method))
        {
            return CleanName(Element(call, model));
        }
        var noun = Noun(method);
        var receiver = ReceiverOf(call);
        if (noun == "Value"
            && receiver is IdentifierNameSyntax or MemberAccessExpressionSyntax)
        {
            return CleanName(Unreflected(LastIdentifier(receiver)));
        }
        if (noun != null)
        {
            return CleanName(noun);
        }
        var type = model.GetTypeInfo(call).Type;
        bool text = type?.SpecialType == SpecialType.System_String;
        if (text && receiver != null
            && model.GetTypeInfo(receiver).Type?.SpecialType
                == SpecialType.System_String)
        {
            var changed = Named(receiver, model);
            if (Trims.Contains(method))
            {
                return "trimmed" + char.ToUpperInvariant(changed[0])
                    + changed.Substring(1);
            }
            return changed;
        }
        var words = Words(method);
        if (!Verbs.Contains(words[0]))
        {
            return CleanName(method);
        }
        var rest = Usable(string.Concat(words.Skip(1)));
        if (rest != null)
        {
            return rest;
        }
        var typed = TypeWord(type);
        if (typed != null)
        {
            return typed;
        }
        if (text)
        {
            return "text";
        }
        return CleanName(method);
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

    /// An element after what it is: "line" for a line of a text split at
    /// its line breaks and "part" for another part of a split text, the
    /// singular of its collection's name ("slots[i]" gives "slot"), the
    /// name of its type ("objects[i]" of RandomObject gives "randomObject",
    /// as "object" is a keyword) or "text" for a string, and only then the
    /// collection's name.
    static string ElementName(
        ExpressionSyntax collection, ITypeSymbol element, SemanticModel model)
    {
        if (collection == null)
        {
            return Fallback;
        }
        while (collection is ParenthesizedExpressionSyntax wrapped)
        {
            collection = wrapped.Expression;
        }
        if (collection is InvocationExpressionSyntax split
            && IsStringSplit(split, model))
        {
            if (SplitsLines(split, model))
            {
                return "line";
            }
            return "part";
        }
        var whole = LastIdentifier(collection);
        if (collection is InvocationExpressionSyntax)
        {
            var noun = Noun(whole);
            if (noun != null)
            {
                whole = noun;
            }
        }
        var single = Usable(Singular(whole));
        if (single != null)
        {
            return single;
        }
        var typed = TypeWord(element);
        if (typed != null)
        {
            return typed;
        }
        if (element?.SpecialType == SpecialType.System_String)
        {
            return "text";
        }
        return CleanName(whole);
    }

    static bool IsStringSplit(
        InvocationExpressionSyntax call, SemanticModel model)
    {
        return model.GetSymbolInfo(call).Symbol is IMethodSymbol
            {
                Name: "Split",
                ContainingType.SpecialType: SpecialType.System_String,
            };
    }

    /// A line break among the separators given as constants.
    static bool SplitsLines(
        InvocationExpressionSyntax split, SemanticModel model)
    {
        foreach (var argument in split.ArgumentList.Arguments)
        {
            var constant = model.GetConstantValue(argument.Expression);
            if (Equals(constant.Value, '\n') || Equals(constant.Value, "\n"))
            {
                return true;
            }
        }
        return false;
    }

    /// "slots" as "slot", "entries" as "entry", "boxes" as "box"; null for
    /// a name that doesn't end in a plural s ("status", "class", "axis").
    static string Singular(string name)
    {
        if (name.Length < 3 || !name.EndsWith('s') || name.EndsWith("ss")
            || name.EndsWith("us") || name.EndsWith("is"))
        {
            return null;
        }
        if (name.EndsWith("ies"))
        {
            return name.Substring(0, name.Length - 3) + "y";
        }
        foreach (var end in new[] { "xes", "ches", "shes", "sses" })
        {
            if (name.EndsWith(end))
            {
                return name.Substring(0, name.Length - 2);
            }
        }
        return name.Substring(0, name.Length - 1);
    }

    /// A type's own name as a variable's ("Player.SaveState" gives
    /// "saveState", an interface's without its I), for a class, struct,
    /// enum or interface that isn't one of .NET's special types, a tuple,
    /// an array or generic; null for any other.
    static string TypeWord(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol
            {
                SpecialType: SpecialType.None,
                IsTupleType: false,
                TypeArguments.Length: 0,
            } named
            || named.TypeKind == TypeKind.Error)
        {
            return null;
        }
        var name = named.Name;
        if (named.TypeKind == TypeKind.Interface && name.Length > 2
            && name[0] == 'I' && char.IsUpper(name[1]))
        {
            name = name.Substring(1);
        }
        return Usable(name);
    }

    /// "getObjects" and "GetObjects" as "Objects", or null for a method
    /// whose name doesn't start with a get.
    static string Noun(string method)
    {
        if (method.Length > 3
            && (method.StartsWith("get") || method.StartsWith("Get"))
            && char.IsUpper(method[3]))
        {
            return method.Substring(3);
        }
        return null;
    }

    /// "SawField" as "Saw": what a FieldInfo or a PropertyInfo reads.
    static string Unreflected(string name)
    {
        foreach (var end in Reflected)
        {
            if (name.Length > end.Length && name.EndsWith(end))
            {
                return name.Substring(0, name.Length - end.Length);
            }
        }
        return name;
    }

    /// What a call is made on: x in "x.F()" and in "x?.F()", or null.
    static ExpressionSyntax ReceiverOf(InvocationExpressionSyntax call)
    {
        switch (call.Expression)
        {
            case MemberAccessExpressionSyntax member:
                return member.Expression;
            case MemberBindingExpressionSyntax binding:
                return BoundTo(binding);
            default:
                return null;
        }
    }

    /// The value a ".F" or "[i]" after a ?. reads from: what stands in
    /// front of the ?. it starts the part after.
    static ExpressionSyntax BoundTo(SyntaxNode binding)
    {
        for (var node = binding.Parent; node != null; node = node.Parent)
        {
            if (node is ConditionalAccessExpressionSyntax access
                && access.WhenNotNull.SpanStart == binding.SpanStart)
            {
                return access.Expression;
            }
        }
        return null;
    }

    /// Clean, but a contextual keyword that no local can clash with stays
    /// as it is: "type" and "method" are keywords only in front of an
    /// attribute ("[type: ...]").
    static string CleanName(string name)
    {
        var lowered = Lowered(name);
        if (lowered != null && Locals.Contains(lowered))
        {
            return lowered;
        }
        return Clean(name);
    }

    /// The name as CleanName gives it, or null when that would add a
    /// suffix to a keyword or the name isn't one.
    static string Usable(string name)
    {
        if (name == null)
        {
            return null;
        }
        var lowered = Lowered(name);
        if (lowered == null
            || (IsAnyKeyword(lowered) && !Locals.Contains(lowered)))
        {
            return null;
        }
        return lowered;
    }
}
