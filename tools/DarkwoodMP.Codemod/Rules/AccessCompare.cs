using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace DarkwoodMP.Codemod.Rules;

/// A comparison on one ?. that keeps its place as one of the fixed
/// translations of codemod.md section 7.3, so it works where no statement
/// can go: a loop condition, a when clause, the right side of &&.
///
///   x?.F == v (v not null)  ->  x != null && x.F == v
///   x?.F != v (v not null)  ->  x == null || x.F != v
///   x?.F > v  (and <, >=, <=, v not null)  ->  x != null && x.F > v
///   x?.F == null            ->  x == null || x.F == null
///   x?.F != null            ->  x != null && x.F != null
///   x?.B == true            ->  x != null && x.B
///   x?.B != true            ->  x == null || !x.B
///   x?.B == false           ->  x != null && !x.B
///   x?.B != false           ->  x == null || x.B
///   x?.B ?? false           ->  x != null && x.B
///   x?.B ?? true            ->  x == null || x.B
///
/// They hold only when v has no effects and can't throw, since the
/// original reads v for a null x too and the translation doesn't: v must
/// be a constant, typeof, a local, a parameter or this. "v not null" means
/// a value type that isn't a Nullable, a constant that isn't null, typeof
/// or a this that isn't a Unity object: "x?.F == v" is true for a null x
/// and a null v, and Unity's == counts a destroyed this as null. A
/// comparison that converts either side by a user-defined conversion,
/// which the original runs for a null x as well, is left to the hoist, and
/// so is one whose operator is a user-defined one that isn't lifted and
/// takes the null itself, such as the == of a member's type, unless it is
/// .NET's or the one UnityEngine.Object declares.
sealed class AccessComparison
{
    /// The expression the translation replaces: the comparison or the ??.
    public ExpressionSyntax Whole;

    /// v as written, or null for ?? false and ?? true.
    public ExpressionSyntax Other;

    /// The ?. is the left operand.
    public bool AccessFirst;

    public string Operator;

    /// The translation tests "x != null" with &&; otherwise "x == null"
    /// with ||.
    public bool Present;

    /// For a bool member and a true or false: "x.B" or "!x.B" alone.
    public string Bare;
}

sealed partial class AccessPlanner
{
    /// The comparison around the ?. becomes its fixed translation, with
    /// the receiver read once: a receiver that isn't a local, a parameter
    /// or this goes into a temporary in front of the statement first.
    bool Compare(AccessChain chain, ExpressionSyntax outer)
    {
        if (chain.IsChain)
        {
            return false;
        }
        var form = Comparison(chain, outer);
        if (form == null)
        {
            return false;
        }
        if (!NeedsTemporary(chain.Receiver))
        {
            var names = new AccessNames();
            _edits.Add(new PlannedEdit
            {
                Span = form.Whole.Span,
                Expression = form.Whole,
                Sites = Sites(chain, form),
                Build = text => Translate(
                    new AccessText(text, chain, names, _types, 0), form),
            });
            return true;
        }
        var refused = WhyNotHoist(chain.Receiver, out var place);
        if (refused != null)
        {
            Queue(chain, refused);
            return true;
        }
        var held = ReceiverNames(chain, place.SpanStart);
        if (held == null)
        {
            return true;
        }
        var hoist = new AccessHoist
        {
            Place = place,
            Moved = chain.Receiver,
            Sites = Sites(chain, form),
        };
        _hoists.Add(hoist);
        int column = PlaceColumn(place);
        _edits.Add(new PlannedEdit
        {
            Span = form.Whole.Span,
            Expression = form.Whole,
            Sites = Sites(chain, form),
            Build = text =>
            {
                var access = new AccessText(text, chain, held, _types, column);
                hoist.Text = access.ReceiverLine();
                return Translate(access, form);
            },
        });
        return true;
    }

    /// The ?. site, and the ?? site the translation removes with it.
    static List<SyntaxNode> Sites(AccessChain chain, AccessComparison form)
    {
        var sites = chain.Sites();
        if (form.Whole.IsKind(SyntaxKind.CoalesceExpression))
        {
            sites.Add(form.Whole);
        }
        return sites;
    }

    /// The translation that holds for the comparison around the ?., or
    /// null when none does.
    AccessComparison Comparison(AccessChain chain, ExpressionSyntax outer)
    {
        if (outer.Parent is not BinaryExpressionSyntax binary)
        {
            return null;
        }
        var member = _types.TypeOf(chain.Last);
        if (member == null || member.TypeKind == TypeKind.Error)
        {
            return null;
        }
        if (binary.IsKind(SyntaxKind.CoalesceExpression))
        {
            return Coalesce(binary, outer, member);
        }
        // A user-defined conversion of the ?.'s value runs for a null x too
        // in the original, and not in the translation: "h?.Go == false"
        // calls Unity's bool operator on null and is true.
        if (_model.GetConversion(outer).IsUserDefined)
        {
            return null;
        }
        // So does a user-defined operator that isn't lifted: it gets the
        // null itself, and "a?.Count == m" with an "==(int?, Meter)" of
        // Meter's is what that operator says, not false.
        if (!OperatorKeeps(binary))
        {
            return null;
        }
        var form = new AccessComparison
        {
            Whole = binary,
            AccessFirst = binary.Left == outer,
            Operator = binary.OperatorToken.Text,
        };
        form.Other = binary.Right;
        if (!form.AccessFirst)
        {
            form.Other = binary.Left;
        }
        var other = TernaryChain.Strip(form.Other);
        bool equality = binary.IsKind(SyntaxKind.EqualsExpression)
            || binary.IsKind(SyntaxKind.NotEqualsExpression);
        bool equal = binary.IsKind(SyntaxKind.EqualsExpression);
        if (equality && other.IsKind(SyntaxKind.NullLiteralExpression))
        {
            if (!member.IsReferenceType && !CoalesceTypes.IsNullable(member))
            {
                return null;
            }
            form.Present = !equal;
            return form;
        }
        if (equality && member.SpecialType == SpecialType.System_Boolean
            && (other.IsKind(SyntaxKind.TrueLiteralExpression)
                || other.IsKind(SyntaxKind.FalseLiteralExpression)))
        {
            bool truth = other.IsKind(SyntaxKind.TrueLiteralExpression);
            form.Present = equal;
            form.Bare = "";
            if (equal != truth)
            {
                form.Bare = "!";
            }
            return form;
        }
        bool ordering = binary.IsKind(SyntaxKind.LessThanExpression)
            || binary.IsKind(SyntaxKind.GreaterThanExpression)
            || binary.IsKind(SyntaxKind.LessThanOrEqualExpression)
            || binary.IsKind(SyntaxKind.GreaterThanOrEqualExpression);
        // A user-defined conversion of v runs for a null x too in the
        // original, and not in the translation.
        if ((!equality && !ordering) || !Quiet(other) || !NotNull(other)
            || _model.GetConversion(form.Other).IsUserDefined)
        {
            return null;
        }
        form.Present = !binary.IsKind(SyntaxKind.NotEqualsExpression);
        return form;
    }

    /// "x?.B ?? false" and "x?.B ?? true" on a bool member, alone: not a
    /// link of a longer chain of ??.
    AccessComparison Coalesce(
        BinaryExpressionSyntax coalesce,
        ExpressionSyntax outer,
        ITypeSymbol member)
    {
        var right = TernaryChain.Strip(coalesce.Right);
        if (coalesce.Left != outer
            || member.SpecialType != SpecialType.System_Boolean
            || coalesce.Parent.IsKind(SyntaxKind.CoalesceExpression)
            || (!right.IsKind(SyntaxKind.TrueLiteralExpression)
                && !right.IsKind(SyntaxKind.FalseLiteralExpression)))
        {
            return null;
        }
        return new AccessComparison
        {
            Whole = coalesce,
            AccessFirst = true,
            Present = right.IsKind(SyntaxKind.FalseLiteralExpression),
            Bare = "",
        };
    }

    /// The comparison's operator is one the fixed translations are written
    /// for: a built-in one (string's == and the reference == are), a lifted
    /// one, or the == of .NET or Unity's, which UnityEngine.Object
    /// declares; they give the original's result for a null x. An == that a
    /// class deriving from UnityEngine.Object declares of its own is like
    /// any other user-defined one. A user-defined operator that isn't
    /// lifted gets the null of a null x itself. The operator of a reference
    /// type is never lifted, so this is also the check that sends to the
    /// hoist a member whose type's own == the comparison calls. Where the
    /// reference == is called instead, as for "h?.It == this" with an It
    /// of a type that derives from this one's, both forms compare
    /// references. Tuple equality has no operator of its own and is lifted
    /// for a Nullable tuple: false for a null x before any element is
    /// compared, as the translation gives.
    bool OperatorKeeps(BinaryExpressionSyntax binary)
    {
        if (_model.GetOperation(binary) is not IBinaryOperation operation)
        {
            return true;
        }
        if (operation.OperatorMethod == null || operation.IsLifted)
        {
            return true;
        }
        var owner = operation.OperatorMethod.ContainingType;
        return AccessTypes.DotNetEquality(owner)
            || _types.Types.IsUnityObject(owner);
    }

    /// A value whose reading has no effects and can't throw.
    bool Quiet(ExpressionSyntax value)
    {
        if (_model.GetConstantValue(value).HasValue
            || value is TypeOfExpressionSyntax
            || value is ThisExpressionSyntax)
        {
            return true;
        }
        if (value is not IdentifierNameSyntax)
        {
            return false;
        }
        var symbol = _model.GetSymbolInfo(value).Symbol;
        return symbol is ILocalSymbol || symbol is IParameterSymbol;
    }

    /// A value that is never null: of a value type that isn't a Nullable,
    /// a constant that isn't null, typeof, or a this that isn't a Unity
    /// object. Unity's == counts a destroyed object as null, so with a null
    /// x, "x?.Go == this" is true for a destroyed this.
    bool NotNull(ExpressionSyntax value)
    {
        var constant = _model.GetConstantValue(value);
        if (constant.HasValue)
        {
            return constant.Value != null;
        }
        if (value is TypeOfExpressionSyntax)
        {
            return true;
        }
        var type = _types.TypeOf(value);
        if (value is ThisExpressionSyntax)
        {
            return type != null && !_types.Types.IsUnity(type);
        }
        return type != null && type.IsValueType
            && !CoalesceTypes.IsNullable(type);
    }

    /// The translation, in parentheses where the code around it would
    /// otherwise bind it another way or make it hard to read.
    static string Translate(AccessText text, AccessComparison form)
    {
        var receiver = text.ReceiverName();
        var member = text.Apply(0, receiver);
        string test;
        string joint;
        if (form.Present)
        {
            test = receiver + " != null";
            joint = " && ";
        }
        else
        {
            test = receiver + " == null";
            joint = " || ";
        }
        string kept;
        if (form.Bare != null)
        {
            kept = form.Bare + member;
        }
        else if (form.AccessFirst)
        {
            kept = member + " " + form.Operator + " " + text.Of(form.Other);
        }
        else
        {
            kept = text.Of(form.Other) + " " + form.Operator + " " + member;
        }
        var translation = test + joint + kept;
        if (NeedsParentheses(form.Whole, form.Present))
        {
            return "(" + translation + ")";
        }
        return translation;
    }

    static bool NeedsParentheses(ExpressionSyntax whole, bool present)
    {
        var kind = SyntaxKind.LogicalOrExpression;
        if (present)
        {
            kind = SyntaxKind.LogicalAndExpression;
        }
        switch (whole.Parent)
        {
            case BinaryExpressionSyntax binary:
                return !binary.IsKind(kind);
            case ParenthesizedExpressionSyntax:
            case ArgumentSyntax:
            case EqualsValueClauseSyntax:
            case ArrowExpressionClauseSyntax:
            case LambdaExpressionSyntax:
            case InterpolationSyntax:
            case WhenClauseSyntax:
            case StatementSyntax:
                return false;
            case AssignmentExpressionSyntax assignment:
                return assignment.Right != whole;
            default:
                return true;
        }
    }
}
