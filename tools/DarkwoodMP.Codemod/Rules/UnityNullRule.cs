using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DarkwoodMP.Codemod.Rules;

/// Which null checks a planner rewrites (codemod.md section 6). ?., ?? and
/// ??= check by reference. "x != null" on a Unity object calls the == of
/// UnityEngine.Object, which also counts a destroyed object as null.
enum NullCheck
{
    /// Stages 5 and 6: values that aren't Unity objects, whose "x != null"
    /// is the reference check ?. makes.
    Reference,

    /// Stage 7: Unity objects, whose "x != null" is Unity's check.
    Unity,
}

/// What the planners say about a site that isn't their mode's.
static class NullChecks
{
    public const string NotUnity =
        "no Unity object is tested: not unity-null's";

    /// Why --receivers leaves every site of the run alone, or null.
    public static string Filtered(ReceiverFilter receivers, NullCheck checks)
    {
        if (checks == NullCheck.Reference && receivers == ReceiverFilter.Unity)
        {
            return "--receivers unity: this rule rewrites no Unity object";
        }
        if (checks == NullCheck.Unity && receivers == ReceiverFilter.Other)
        {
            return "--receivers other: this rule rewrites only Unity objects";
        }
        return null;
    }
}

/// Stage 7 (guide section 4.7, codemod.md section 6). Every ?., ?[, ?? and
/// ??= that tests a Unity object becomes the ifs stages 5 and 6 write
/// (CoalescePlanner, AccessPlanner), with "x != null" and "x == null" on a
/// value whose static type is the Unity type, so the check is Unity's ==.
/// This is the one stage that changes what the code does: where the
/// original ran on a destroyed object, the rewrite skips it, as it does for
/// null. A destroyed a in "a ?? b" gives b, and "x ??= v" replaces a
/// destroyed x. It never writes "is null", ReferenceEquals or "(object)x",
/// which check the reference only.
///
/// The coalesce planner runs first, so it can open the ?. in the first
/// value of a ?? ("r?.m ?? c") with the ?? that holds it; the access
/// planner takes the other ?. chains that test a Unity object anywhere,
/// and "x?.B ?? false". A Unity tested value must reach its null check with
/// a Unity type: a ?? whose type isn't a Unity type would test it by
/// reference, and goes to the queue, as does a class deriving from
/// UnityEngine.Object that declares an == of its own and a type parameter.
/// Recheck then counts, in the compiled rewrite, the null checks that call
/// Unity's ==: one for each Unity object the sites test.
sealed class UnityNullRule : Rule
{
    public override string Name => "unity-null";

    public override string SiteList =>
        "tools/DarkwoodMP.Codemod/sites/unity-null.txt";

    public override string SiteListNote =>
        "Stage 7 of the formatting pass (guide section 4.7). Each site "
            + "tested a Unity\nobject by reference, with ?., ?? or ??=. The "
            + "new code tests it with\n\"x != null\" or \"x == null\", "
            + "which call Unity's ==, so code that ran on a\ndestroyed "
            + "object is now skipped, as it is for null, and \"a ?? b\" "
            + "gives b\nfor a destroyed a. The old code is from the start of "
            + "its line; lines are\nthose before the rewrite.";

    public override void Plan(RulePlan plan)
    {
        var nesting = new UnityNesting(plan);
        nesting.QueuePairs();
        new CoalescePlanner(plan, NullCheck.Unity).Run();
        nesting.QueueUnopened();
        new AccessPlanner(plan, NullCheck.Unity).Run();
    }

    /// A proposal whose new code tests a Unity object by reference, or
    /// adds a check that isn't Unity's where the original made none, is
    /// given up: the count of null checks calling Unity's == must grow by
    /// the number of Unity objects its sites test, and the others by no
    /// more than the number of other values they test ("x?.B ?? false"
    /// tests x?.B, and its "x != null && x.B" tests x alone).
    public override List<(Proposal Proposal, string Reason)> Recheck(
        RulePlan plan,
        SemanticModel rewritten,
        IReadOnlyDictionary<Proposal, TextSpan> spans)
    {
        var types = new CoalesceTypes(plan.Model, NullCheck.Unity);
        var given = new List<(Proposal Proposal, string Reason)>();
        foreach (var proposal in spans.Keys.OrderBy(p => p.Span.Start))
        {
            var tested = NullTests.Tested(proposal, plan.Model, types);
            var before = NullTests.In(plan.Model, proposal.Span);
            var after = NullTests.In(rewritten, spans[proposal]);
            int unity = after.Unity - before.Unity;
            int other = after.Other - before.Other;
            if (unity != tested.Unity)
            {
                given.Add((proposal, $"the rewrite makes {unity} null "
                    + $"checks with Unity's ==, for {tested.Unity} Unity "
                    + "objects tested"));
            }
            else if (other > tested.Other)
            {
                given.Add((proposal, $"the rewrite adds {other} null checks "
                    + $"that aren't Unity's, for {tested.Other} other values "
                    + "tested"));
            }
        }
        return given;
    }
}

/// A ?? or ??= and a ?. chain of the rule, one inside the other. The
/// rewrite of the outer one would move the inner one into a statement,
/// where a second run would rewrite it, so both go to the queue, as two ?.
/// or two ?? one inside the other do in their planners. The ?. that is the
/// first value of a ?? and that the coalesce planner opens with it is the
/// exception: one rewrite removes both, and when the coalesce planner
/// queues that ?? instead, the ?. goes to the queue after it.
sealed class UnityNesting
{
    const string HoldsAccess =
        "holds a ?. that tests a Unity object, and doesn't open it: "
            + "rewrite one level per run";

    const string InCoalesce =
        "inside a ?? or ??= that tests a Unity object: rewrite one level "
            + "per run";

    const string HoldsCoalesce =
        "holds a ?? or ??= that tests a Unity object: rewrite one level "
            + "per run";

    const string InAccess =
        "inside a ?. that tests a Unity object: rewrite one level per run";

    readonly RulePlan _plan;
    readonly List<ConditionalAccessExpressionSyntax> _opened = new();

    public UnityNesting(RulePlan plan)
    {
        _plan = plan;
    }

    /// Before the planners run: every pair but the ?. a ?? opens.
    public void QueuePairs()
    {
        if (NullChecks.Filtered(_plan.Receivers, NullCheck.Unity) != null)
        {
            return;
        }
        var coalesces = Coalesces(_plan);
        var accesses = Accesses(_plan);
        foreach (var (sites, chain) in coalesces)
        {
            var top = sites[0];
            foreach (var access in accesses)
            {
                if (top.Span.Contains(access.Span))
                {
                    if (chain != null && chain.Access == access)
                    {
                        _opened.Add(access);
                        continue;
                    }
                    Both(_plan, sites, HoldsAccess, access, InCoalesce);
                }
                else if (access.Span.Contains(top.Span))
                {
                    Both(_plan, sites, InAccess, access, HoldsCoalesce);
                }
            }
        }
    }

    /// After the coalesce planner: a ?. its ?? would have opened, when
    /// that ?? went to the queue, or the ?. would be rewritten alone.
    public void QueueUnopened()
    {
        foreach (var access in _opened)
        {
            if (!_plan.IsReported(access))
            {
                _plan.Queue(access, InCoalesce);
            }
        }
    }

    static void Both(
        RulePlan plan,
        List<SyntaxNode> coalesce,
        string coalesceReason,
        SyntaxNode access,
        string accessReason)
    {
        foreach (var link in coalesce)
        {
            if (!plan.IsReported(link))
            {
                plan.Queue(link, coalesceReason);
            }
        }
        if (!plan.IsReported(access))
        {
            plan.Queue(access, accessReason);
        }
    }

    /// Every ?? chain (its links, the top first) and ??= of the rule, with
    /// the chain when its first value is a ?. the rewrite opens.
    static List<(List<SyntaxNode> Sites, CoalesceChain Chain)> Coalesces(
        RulePlan plan)
    {
        var types = new CoalesceTypes(plan.Model, NullCheck.Unity);
        var found = new List<(List<SyntaxNode> Sites, CoalesceChain Chain)>();
        foreach (var node in plan.Root.DescendantNodes())
        {
            if (node is BinaryExpressionSyntax link
                && link.IsKind(SyntaxKind.CoalesceExpression)
                && !CoalesceChain.IsInnerLink(link))
            {
                var chain = new CoalesceChain(link, plan.Model);
                if (types.Mine(chain))
                {
                    chain.Access = types.Opens(chain);
                    found.Add((chain.Links.Cast<SyntaxNode>().ToList(), chain));
                }
            }
            else if (node is AssignmentExpressionSyntax assignment
                && assignment.IsKind(SyntaxKind.CoalesceAssignmentExpression)
                && types.MineTarget(types.TypeOf(assignment.Left)))
            {
                found.Add((new List<SyntaxNode> { assignment }, null));
            }
        }
        return found;
    }

    /// Every ?. chain that tests a Unity object, whatever its fate.
    static List<ConditionalAccessExpressionSyntax> Accesses(RulePlan plan)
    {
        var types = new AccessTypes(plan.Model, NullCheck.Unity);
        return plan.Root.DescendantNodes()
            .OfType<ConditionalAccessExpressionSyntax>()
            .Where(node => OperatorCount.KindOf(node) != null)
            .Where(node =>
            {
                types.Check(new AccessChain(node), out bool later);
                return !later;
            })
            .ToList();
    }
}

/// The null checks in a piece of code, by whose == they call.
static class NullTests
{
    /// The "x == null" and "x != null" inside the span: those that call
    /// the == of UnityEngine.Object, and the others.
    public static (int Unity, int Other) In(SemanticModel model, TextSpan span)
    {
        int unity = 0;
        int other = 0;
        var nodes = model.SyntaxTree.GetRoot().DescendantNodes(span)
            .OfType<BinaryExpressionSyntax>()
            .Where(node => span.Contains(node.Span));
        foreach (var node in nodes)
        {
            if (!node.IsKind(SyntaxKind.EqualsExpression)
                && !node.IsKind(SyntaxKind.NotEqualsExpression))
            {
                continue;
            }
            if (!IsNull(node.Left) && !IsNull(node.Right))
            {
                continue;
            }
            if (IsUnityOperator(model.GetSymbolInfo(node).Symbol))
            {
                unity++;
            }
            else
            {
                other++;
            }
        }
        return (unity, other);
    }

    /// The values the proposal's sites test, by whether their static type
    /// is a Unity type: a ?. chain its receiver and every step but the
    /// last, a link of ?? its left side, a ??= its target.
    public static (int Unity, int Other) Tested(
        Proposal proposal, SemanticModel model, CoalesceTypes types)
    {
        int unity = 0;
        int other = 0;
        foreach (var site in proposal.Sites)
        {
            foreach (var value in TestedBy(site))
            {
                if (types.IsUnity(model.GetTypeInfo(value).Type))
                {
                    unity++;
                }
                else
                {
                    other++;
                }
            }
        }
        return (unity, other);
    }

    static IEnumerable<ExpressionSyntax> TestedBy(SyntaxNode site)
    {
        switch (site)
        {
            case ConditionalAccessExpressionSyntax access:
                return new AccessChain(access).Tested();
            case BinaryExpressionSyntax link:
                return new[] { link.Left };
            case AssignmentExpressionSyntax assignment:
                return new[] { assignment.Left };
            default:
                throw new Stop(
                    "unity-null: a site of an unknown kind: " + site);
        }
    }

    static bool IsNull(ExpressionSyntax value)
    {
        return TernaryChain.Strip(value)
            .IsKind(SyntaxKind.NullLiteralExpression);
    }

    /// By name, because the rewritten file is another compilation, whose
    /// symbols aren't the original's.
    static bool IsUnityOperator(ISymbol symbol)
    {
        return symbol is IMethodSymbol method
            && method.MethodKind == MethodKind.UserDefinedOperator
            && method.ContainingType.Name == "Object"
            && method.ContainingType.ContainingNamespace?.ToDisplayString()
                == "UnityEngine";
    }
}

/// What the static types say under unity-null (codemod.md section 6): a
/// value of a class deriving from UnityEngine.Object is tested with "x !=
/// null", which calls Unity's == when no class between declares one of
/// its own.
sealed partial class CoalesceTypes
{
    /// Whether the ?? is this mode's: any ?? by reference, or under
    /// unity-null one that tests a Unity object or has a Unity type.
    public bool Mine(CoalesceChain chain)
    {
        if (_checks == NullCheck.Reference)
        {
            return true;
        }
        if (chain.Natural != null && IsUnity(chain.Natural))
        {
            return true;
        }
        return chain.Tested.Any(value => IsUnity(TypeOf(value)));
    }

    /// The ?. of the chain's first value that the rewrite would open with
    /// it, or null.
    public ConditionalAccessExpressionSyntax Opens(CoalesceChain chain)
    {
        return Opened(chain.First);
    }

    /// The same for the target of a ??=.
    public bool MineTarget(ITypeSymbol type)
    {
        return _checks == NullCheck.Reference || IsUnity(type);
    }

    /// Under unity-null, "x == null" on a value of this type is Unity's
    /// check: a class deriving from UnityEngine.Object, with the == that
    /// UnityEngine.Object declares.
    public bool ChecksAsUnity(ITypeSymbol type)
    {
        return _checks == NullCheck.Unity
            && type is INamedTypeSymbol { TypeKind: TypeKind.Class }
            && IsUnity(type)
            && OwnUnityEquality(type) == null;
    }

    /// Why a Unity type can't be tested with Unity's == as it is, or null.
    public string UnityTests(ITypeSymbol type)
    {
        if (type is ITypeParameterSymbol)
        {
            return "a Unity object of a type parameter, whose null check "
                + "the rewrite doesn't write";
        }
        var own = OwnUnityEquality(type);
        if (own != null)
        {
            return "the Unity class " + own.Name + " declares an == of its "
                + "own, which the rewrite's null check would call";
        }
        return null;
    }

    /// Under unity-null, why the ?? can't be rewritten, or null. Every
    /// value is tested in a temporary of the type of the ??, so that type
    /// must be a Unity type, or a Unity value would be tested by reference.
    string UnityChain(CoalesceChain chain)
    {
        var natural = chain.Natural;
        if (!IsUnity(natural))
        {
            return "a Unity object tested by a ?? of the type " + natural.Name
                + ", whose null check would be by reference";
        }
        return UnityTests(natural);
    }

    /// The first class from the type up to UnityEngine.Object, without it,
    /// that declares an == or != of its own, or null.
    INamedTypeSymbol OwnUnityEquality(ITypeSymbol type)
    {
        for (var current = type as INamedTypeSymbol;
            current != null && !IsUnityObject(current);
            current = current.BaseType)
        {
            bool declares = current.GetMembers("op_Equality")
                .Concat(current.GetMembers("op_Inequality"))
                .OfType<IMethodSymbol>()
                .Any(m => m.MethodKind == MethodKind.UserDefinedOperator);
            if (declares)
            {
                return current;
            }
        }
        return null;
    }
}
