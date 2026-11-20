using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DarkwoodMP.Codemod.Rules;

/// One ?. chain as the conditional-access rule sees it: "a?.b.c?.d(x)" is
/// the receiver a and the steps ".b.c" and ".d(x)". Each ?. tests the value
/// in front of it once: the receiver, then the value of every step but the
/// last.
sealed class AccessChain
{
    public readonly ConditionalAccessExpressionSyntax Top;
    public readonly List<ExpressionSyntax> Steps = new();

    public AccessChain(ConditionalAccessExpressionSyntax top)
    {
        Top = top;
        var link = top;
        while (link.WhenNotNull is ConditionalAccessExpressionSyntax next)
        {
            Steps.Add(next.Expression);
            link = next;
        }
        Steps.Add(link.WhenNotNull);
    }

    public ExpressionSyntax Receiver => Top.Expression;

    public ExpressionSyntax Last => Steps[Steps.Count - 1];

    public bool IsChain => Steps.Count > 1;

    /// The values the ?. test, in the order they run.
    public List<ExpressionSyntax> Tested()
    {
        var tested = new List<ExpressionSyntax> { Receiver };
        tested.AddRange(Steps.Take(Steps.Count - 1));
        return tested;
    }

    public List<SyntaxNode> Sites()
    {
        return new List<SyntaxNode> { Top };
    }

    /// ".Invoke(a)" on a delegate, which the rewrite calls as "handler(a)".
    public static bool InvokesDelegate(
        ExpressionSyntax step, ITypeSymbol receiver)
    {
        return receiver?.TypeKind == TypeKind.Delegate
            && step is InvocationExpressionSyntax
            {
                Expression: MemberBindingExpressionSyntax
                {
                    Name.Identifier.ValueText: "Invoke",
                },
            };
    }
}

/// Which chains the rule may rewrite, by the types of the values they test
/// (codemod.md section 6). A Unity object anywhere is stage 7's, and the
/// whole chain waits for it.
sealed class AccessTypes
{
    public const string Unity =
        "a Unity object is tested: left whole for stage 7 (unity-null)";

    readonly SemanticModel _model;
    readonly CoalesceTypes _types;

    public AccessTypes(
        SemanticModel model, NullCheck checks = NullCheck.Reference)
    {
        _model = model;
        _types = new CoalesceTypes(model, checks);
    }

    public CoalesceTypes Types => _types;

    public ITypeSymbol TypeOf(ExpressionSyntax expression)
    {
        return _model.GetTypeInfo(expression).Type;
    }

    /// Why the chain can't be rewritten, or null; later is set when the
    /// reason is stage 7's.
    public string Check(AccessChain chain, out bool later)
    {
        later = false;
        var tested = chain.Tested().Select(TypeOf).ToList();
        bool unity = tested.Any(type => type != null && _types.IsUnity(type));
        if (_types.Checks == NullCheck.Unity && !unity)
        {
            later = true;
            return NullChecks.NotUnity;
        }
        if (_types.Checks == NullCheck.Reference && unity)
        {
            later = true;
            return Unity;
        }
        foreach (var type in tested)
        {
            string refused;
            if (_types.Checks == NullCheck.Unity && type != null
                && _types.IsUnity(type))
            {
                refused = _types.UnityTests(type);
            }
            else
            {
                refused = Receiver(type);
            }
            if (refused != null)
            {
                return refused;
            }
        }
        var natural = TypeOf(chain.Top);
        if (natural == null || natural.TypeKind == TypeKind.Error
            || natural.TypeKind == TypeKind.Dynamic)
        {
            return "the value of the ?. has no type the rewrite can write";
        }
        return null;
    }

    static string Receiver(ITypeSymbol type)
    {
        if (type == null || type.TypeKind == TypeKind.Error)
        {
            return "a receiver of an unknown type";
        }
        if (type.TypeKind == TypeKind.Dynamic)
        {
            return "a dynamic receiver";
        }
        if (CoalesceTypes.IsNullable(type))
        {
            return "a Nullable receiver, which ?. reads through its Value";
        }
        if (type is ITypeParameterSymbol)
        {
            return "a receiver of a type parameter";
        }
        if (!type.IsReferenceType)
        {
            return "a receiver that isn't a reference";
        }
        if (CoalesceTypes.HasOwnEquality(type) && !DotNetEquality(type))
        {
            return "a receiver whose type has an == of its own, which "
                + "\"x != null\" would call";
        }
        return null;
    }

    /// The == the type has of its own is .NET's, such as Type's or
    /// MethodBase's. Those treat null as equal only to null, so
    /// "x != null" tests what ?. tests, and they have no effects.
    public static bool DotNetEquality(ITypeSymbol type)
    {
        for (var current = type; current != null; current = current.BaseType)
        {
            if (!current.GetMembers("op_Equality").OfType<IMethodSymbol>()
                .Any(m => m.MethodKind == MethodKind.UserDefinedOperator))
            {
                continue;
            }
            var space = current.ContainingNamespace.ToDisplayString();
            var assembly = current.ContainingAssembly.Name;
            return (space == "System" || space.StartsWith("System."))
                && (assembly == "mscorlib" || assembly == "netstandard"
                    || assembly.StartsWith("System"));
        }
        return false;
    }

    /// The value converts to the type by an identity or a reference
    /// conversion, which runs no code and keeps a null a null.
    public bool Keeps(ITypeSymbol from, ITypeSymbol to)
    {
        if (from == null || to == null)
        {
            return false;
        }
        if (_types.Identity(from, to))
        {
            return true;
        }
        if (!from.IsReferenceType || !to.IsReferenceType)
        {
            return false;
        }
        var conversion = _model.Compilation.ClassifyCommonConversion(from, to);
        return conversion.IsImplicit && conversion.IsReference
            && !conversion.IsUserDefined;
    }
}

/// The temporaries a chain's rewrite declares: one for the receiver when
/// it isn't a local, a parameter or this, and one for the value each inner
/// ?. tests.
sealed class AccessNames
{
    public string ReceiverType;
    public string Receiver;
    public readonly List<string> StepTypes = new();
    public readonly List<string> Steps = new();
}

/// The statements a chain becomes (codemod.md section 7.3): the receiver's
/// temporary, then an if per ?., each holding the temporary of the value
/// the next one tests. Pieces of the original come from the EditText, so
/// edits inside them are kept. A statement written over several lines
/// starts at the column of the original statement, so the layout pass
/// moves the lines that continue it as far as it moves the first.
sealed class AccessText
{
    readonly EditText _text;
    readonly AccessChain _chain;
    readonly AccessNames _names;
    readonly AccessTypes _types;
    readonly string _margin;

    public AccessText(
        EditText text,
        AccessChain chain,
        AccessNames names,
        AccessTypes types,
        int column)
    {
        _text = text;
        _chain = chain;
        _names = names;
        _types = types;
        _margin = new string(' ', column);
    }

    /// "T r = R;" when the receiver has a temporary, or nothing.
    public string ReceiverLine()
    {
        if (_names.Receiver == null)
        {
            return "";
        }
        return Line(
            _names.ReceiverType + " " + _names.Receiver + " = "
                + _text.Of(TernaryChain.Strip(_chain.Receiver)) + ";");
    }

    /// The receiver as the rewrite reads it: its temporary or its text.
    public string ReceiverName()
    {
        if (_names.Receiver != null)
        {
            return _names.Receiver;
        }
        return _text.Of(_chain.Receiver);
    }

    /// The ifs of the chain; last makes the statement that uses the value
    /// of the last step, such as "x = r.F;".
    public string Ifs(Func<string, string> last)
    {
        return Nest(0, ReceiverName(), last);
    }

    string Nest(int step, string value, Func<string, string> last)
    {
        var applied = Apply(step, value);
        string body;
        if (step == _chain.Steps.Count - 1)
        {
            body = last(applied);
        }
        else
        {
            var name = _names.Steps[step];
            body = Line(_names.StepTypes[step] + " " + name + " = " + applied
                    + ";")
                + Nest(step + 1, name, last);
        }
        return "if (" + value + " != null)\n{\n" + body + "}\n";
    }

    /// The step read from the value in front of it: "r.b.c", "r[i]", or
    /// "handler(a)" for ".Invoke(a)" on a delegate.
    public string Apply(int step, string value)
    {
        var node = _chain.Steps[step];
        ExpressionSyntax tested = _chain.Receiver;
        if (step > 0)
        {
            tested = _chain.Steps[step - 1];
        }
        if (AccessChain.InvokesDelegate(node, _types.TypeOf(tested)))
        {
            var call = (InvocationExpressionSyntax)node;
            return value + _text.Of(call.ArgumentList);
        }
        return value + _text.Of(node);
    }

    public string Of(SyntaxNode node)
    {
        return _text.Of(node);
    }

    /// A line of code; one that holds a line break keeps the column of
    /// the statement it came from.
    public string Line(string line)
    {
        if (line.Contains('\n'))
        {
            return _margin + line + "\n";
        }
        return line + "\n";
    }
}
