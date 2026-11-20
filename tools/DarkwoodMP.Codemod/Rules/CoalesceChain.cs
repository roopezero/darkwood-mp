using DarkwoodMP.Codemod.Census;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DarkwoodMP.Codemod.Rules;

/// How the values of a ?? chain become statements (codemod.md section 7.2).
enum CoalesceForm
{
    /// "T t = a; if (t == null) { t = b; }": the temporary starts as the
    /// first value, and each later value fills it while it is null.
    Fill,

    /// "U t; if (a.HasValue) { t = a.Value; } else { t = b; }": the first
    /// value is a Nullable or a ?. of a value type, and the result is
    /// another type, such as the value type itself.
    Unwrap,
}

/// A ?? chain, "a ?? b ?? c", as the values it tries in their order, and
/// what its rewrite needs: the natural type, the form, and the ?. of the
/// first value when the rewrite opens it.
sealed class CoalesceChain
{
    public readonly List<BinaryExpressionSyntax> Links = new();

    /// The values in the order they are tried, without parentheses.
    public readonly List<ExpressionSyntax> Values = new();

    public readonly ITypeSymbol Natural;

    public CoalesceForm Form;

    /// The first value, "r?.m", when the rewrite opens it into "if (r !=
    /// null) { t = r.m; }". Null when the first value stays as written.
    public ConditionalAccessExpressionSyntax Access;

    public CoalesceChain(BinaryExpressionSyntax top, SemanticModel model)
    {
        var link = top;
        while (link != null)
        {
            Links.Add(link);
            Values.Add(TernaryChain.Strip(link.Left));
            var right = TernaryChain.Strip(link.Right);
            link = null;
            if (right.IsKind(SyntaxKind.CoalesceExpression))
            {
                link = (BinaryExpressionSyntax)right;
            }
            else
            {
                Values.Add(right);
            }
        }
        Natural = model.GetTypeInfo(top).Type;
    }

    public BinaryExpressionSyntax Top => Links[0];

    public ExpressionSyntax First => Values[0];

    public ExpressionSyntax Last => Values[Values.Count - 1];

    /// The values whose null check the chain makes: all but the last.
    public IEnumerable<ExpressionSyntax> Tested =>
        Values.Take(Values.Count - 1);

    /// What the rewrite removes: every link, and the ?. it opens.
    public List<SyntaxNode> Sites()
    {
        var sites = Links.Cast<SyntaxNode>().ToList();
        if (Access != null)
        {
            sites.Add(Access);
        }
        return sites;
    }

    /// A ?? that is the right side of another ?? is a link of that one's
    /// chain, and never a site of its own.
    public static bool IsInnerLink(ExpressionSyntax site)
    {
        var outer = TernaryChain.Outer(site);
        return outer.Parent is BinaryExpressionSyntax parent
            && parent.IsKind(SyntaxKind.CoalesceExpression)
            && parent.Right == outer;
    }
}

/// What the static types of a ?? or ??= say about its rewrite (codemod.md
/// section 6). ?? checks for null by reference, and the rewrite's "== null"
/// calls the == of the type it is written on: Unity's own for a Unity
/// object, which waits for stage 7, and a user-defined one for any type
/// that has one but string, which goes to the queue. The conversions decide
/// the form: a value may go into the temporary before its null check only
/// when that conversion keeps a null a null and runs no code. Under
/// unity-null the Unity objects are the ones rewritten, with Unity's ==
/// (UnityNullRule.cs).
sealed partial class CoalesceTypes
{
    public const string UnityLater =
        "a Unity object, whose null check is unity-null's (stage 7)";

    readonly SemanticModel _model;
    readonly INamedTypeSymbol _unity;
    readonly NullCheck _checks;

    public CoalesceTypes(
        SemanticModel model, NullCheck checks = NullCheck.Reference)
    {
        _model = model;
        _unity = model.Compilation.GetTypeByMetadataName("UnityEngine.Object");
        _checks = checks;
    }

    public NullCheck Checks => _checks;

    /// Null when the chain can be rewritten, after which its form and the
    /// ?. it opens are set; otherwise why not. later is set for a Unity
    /// object, which stays for stage 7.
    public string Check(CoalesceChain chain, out bool later)
    {
        later = false;
        var natural = chain.Natural;
        if (natural == null || natural.TypeKind == TypeKind.Error)
        {
            return "the ?? has no type of its own";
        }
        if (_checks == NullCheck.Unity)
        {
            var unity = UnityChain(chain);
            if (unity != null)
            {
                return unity;
            }
        }
        foreach (var value in chain.Tested)
        {
            if (IsUnity(TypeOf(value)) && _checks == NullCheck.Reference)
            {
                later = true;
                return UnityLater + ": " + value;
            }
        }
        if (IsUnity(natural) && _checks == NullCheck.Reference)
        {
            later = true;
            return UnityLater + ": the ?? has the type " + natural.Name;
        }
        var refused = Tests(natural);
        if (refused != null)
        {
            return refused;
        }
        foreach (var value in chain.Values)
        {
            if (IsDynamic(TypeOf(value)))
            {
                return Dynamic;
            }
        }
        chain.Access = Opened(chain.First);
        return Form(chain);
    }

    /// The same for the target of a ??=, whose type the null check is
    /// written on.
    public string CheckTarget(ITypeSymbol type, out bool later)
    {
        later = IsUnity(type) && _checks == NullCheck.Reference;
        if (later)
        {
            return UnityLater + ": the target";
        }
        if (type == null || type.TypeKind == TypeKind.Error)
        {
            return "the target has no type";
        }
        if (_checks == NullCheck.Unity)
        {
            var unity = UnityTests(type);
            if (unity != null)
            {
                return unity;
            }
        }
        return Tests(type);
    }

    const string Dynamic =
        "dynamic, whose == and conversions are bound when the code runs";

    /// Why "x == null" on a variable of this type wouldn't be the check ??
    /// makes, or null. Only a reference type is tested so: a Nullable is
    /// tested with HasValue, and a value type is the unwrapped result.
    string Tests(ITypeSymbol type)
    {
        if (IsDynamic(type))
        {
            return Dynamic;
        }
        if (type.IsReferenceType && HasOwnEquality(type)
            && !ChecksAsUnity(type))
        {
            return "the type " + type.Name + " has an == of its own, which "
                + "the rewrite's null check would call";
        }
        return null;
    }

    /// Fill when every tested value goes into the temporary keeping a null
    /// a null; else Unwrap for one ?? whose first value is unwrapped.
    string Form(CoalesceChain chain)
    {
        bool fills = chain.Natural.IsReferenceType || IsNullable(chain.Natural);
        foreach (var value in chain.Tested)
        {
            if (!KeepsNull(TypeOf(value), chain.Natural))
            {
                fills = false;
            }
        }
        if (fills)
        {
            chain.Form = CoalesceForm.Fill;
            return null;
        }
        if (chain.Links.Count == 1 && Unwraps(chain))
        {
            chain.Form = CoalesceForm.Unwrap;
            return null;
        }
        chain.Access = null;
        if (chain.Links.Count > 1)
        {
            return "a chain whose tested values can't share one temporary "
                + "as they are: a Nullable to unwrap, or a conversion that "
                + "runs code";
        }
        return "the left side converts to the type of the ?? by more than a "
            + "reference conversion: " + chain.First;
    }

    /// The first value is a Nullable, or a ?. of a value type, so it is
    /// tested with HasValue or on its receiver and then unwrapped.
    bool Unwraps(CoalesceChain chain)
    {
        if (chain.Access != null)
        {
            var member = TypeOf(chain.Access.WhenNotNull);
            return member.IsValueType && !IsNullable(member);
        }
        return IsNullable(TypeOf(chain.First));
    }

    /// The ?. a first value is, when the rewrite can open it: one link, on
    /// a receiver checked by reference, reading a member that isn't itself
    /// a Nullable. Any other stays as written, for the conditional-access
    /// and unity-null rules.
    ConditionalAccessExpressionSyntax Opened(ExpressionSyntax value)
    {
        if (value is not ConditionalAccessExpressionSyntax access
            || access.WhenNotNull.DescendantNodesAndSelf()
                .OfType<ConditionalAccessExpressionSyntax>().Any())
        {
            return null;
        }
        var receiver = TypeOf(access.Expression);
        if (receiver == null || !receiver.IsReferenceType
            || (IsUnity(receiver) && !ChecksAsUnity(receiver))
            || IsDynamic(receiver)
            || (HasOwnEquality(receiver) && !ChecksAsUnity(receiver)))
        {
            return null;
        }
        var member = TypeOf(access.WhenNotNull);
        if (member == null || member.TypeKind == TypeKind.Error
            || member.SpecialType == SpecialType.System_Void
            || IsDynamic(member) || IsNullable(member)
            || (!member.IsReferenceType && !member.IsValueType))
        {
            return null;
        }
        return access;
    }

    /// The conversion from a tested value to the temporary's type keeps a
    /// null a null and runs no code: an identity, a reference conversion to
    /// a reference type, or a nullable conversion between Nullables.
    bool KeepsNull(ITypeSymbol from, ITypeSymbol to)
    {
        if (from == null)
        {
            return false;
        }
        if (Identity(from, to))
        {
            return true;
        }
        var conversion = _model.Compilation.ClassifyCommonConversion(from, to);
        if (!conversion.IsImplicit || conversion.IsUserDefined)
        {
            return false;
        }
        if (to.IsReferenceType)
        {
            return conversion.IsReference;
        }
        return conversion.IsNullable && IsNullable(from);
    }

    public bool Identity(ITypeSymbol a, ITypeSymbol b)
    {
        if (SymbolEqualityComparer.Default.Equals(a, b))
        {
            return true;
        }
        if (a == null || b == null || IsDynamic(a) || IsDynamic(b))
        {
            return false;
        }
        return _model.Compilation.ClassifyCommonConversion(a, b).IsIdentity;
    }

    public ITypeSymbol TypeOf(ExpressionSyntax expression)
    {
        return _model.GetTypeInfo(expression).Type;
    }

    /// The type is UnityEngine.Object itself, the one that declares
    /// Unity's == and !=. A class that derives from it can declare an ==
    /// of its own.
    public bool IsUnityObject(ITypeSymbol type)
    {
        return _unity != null
            && SymbolEqualityComparer.Default.Equals(type, _unity);
    }

    public bool IsUnity(ITypeSymbol type)
    {
        var kind = OperandClass.TypeClass(type, _unity);
        return kind == "UnityEngine.Object (derived)"
            || kind == "type parameter : UnityEngine.Object";
    }

    public static bool IsNullable(ITypeSymbol type)
    {
        return type != null && type.OriginalDefinition.SpecialType
            == SpecialType.System_Nullable_T;
    }

    static bool IsDynamic(ITypeSymbol type)
    {
        return type != null && type.TypeKind == TypeKind.Dynamic;
    }

    /// A user-defined == on the type or a type it derives from, or on a
    /// constraint of a type parameter. string's compares the text and a
    /// delegate's the methods it calls, and with null both are a reference
    /// check, so they don't count.
    public static bool HasOwnEquality(ITypeSymbol type)
    {
        if (type is ITypeParameterSymbol parameter)
        {
            return parameter.ConstraintTypes.Any(HasOwnEquality);
        }
        for (var current = type; current != null; current = current.BaseType)
        {
            if (current.SpecialType == SpecialType.System_String
                || current.TypeKind == TypeKind.Delegate
                || current.SpecialType == SpecialType.System_MulticastDelegate
                || current.SpecialType == SpecialType.System_Delegate)
            {
                return false;
            }
            if (current.GetMembers("op_Equality").OfType<IMethodSymbol>()
                .Any(m => m.MethodKind == MethodKind.UserDefinedOperator))
            {
                return true;
            }
        }
        return false;
    }
}
