using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DarkwoodMP.Codemod.Rules;

/// How a probe rule writes its rewrite. Everything but Correct is wrong on
/// purpose, to prove that the differential harness catches it.
enum ProbeForm
{
    Correct,
    EvaluatesTwice,
    ChangesOrder,
    DropsEffect,
}

/// A trivial rule that exists for the tool's own tests and is never run on the
/// plugin's sources in a stage: "return c ? a : b;" becomes
/// "if (c) { return a; } return b;".
sealed class ProbeReturnRule : Rule
{
    readonly ProbeForm _form;

    public ProbeReturnRule(ProbeForm form)
    {
        _form = form;
    }

    public override string Name => "probe-return";

    public override void Plan(RulePlan plan)
    {
        var sites = plan.Root.DescendantNodes()
            .OfType<ConditionalExpressionSyntax>();
        foreach (var site in sites)
        {
            if (site.Parent is not ReturnStatementSyntax target)
            {
                plan.Later(site, "not the whole value of a return");
            }
            else if (!plan.Purity.BranchesHaveNaturalType(site))
            {
                plan.Queue(site, "a branch converts to another type");
            }
            else
            {
                plan.Propose(site, target, Rewrite(site));
            }
        }
    }

    string Rewrite(ConditionalExpressionSyntax site)
    {
        var condition = site.Condition.ToString();
        var yes = site.WhenTrue.ToString();
        var no = site.WhenFalse.ToString();
        switch (_form)
        {
            case ProbeForm.EvaluatesTwice:
                return $"if ({condition})\n{{\nvar twice = {yes};\n"
                    + $"return {yes};\n}}\nreturn {no};";
            case ProbeForm.ChangesOrder:
                return $"var second = {no};\nif ({condition})\n{{\n"
                    + $"return {yes};\n}}\nreturn second;";
            case ProbeForm.DropsEffect:
                return $"if ({condition})\n{{\nreturn {yes};\n}}\n"
                    + $"return {WithoutLog(site.WhenFalse)};";
            default:
                return $"if ({condition})\n{{\nreturn {yes};\n}}\nreturn {no};";
        }
    }

    /// Log("name", value) becomes value: the result stays, the effect goes.
    static string WithoutLog(ExpressionSyntax expression)
    {
        if (expression is InvocationExpressionSyntax
            {
                ArgumentList.Arguments.Count: 2,
            } call)
        {
            return call.ArgumentList.Arguments[1].ToString();
        }
        return expression.ToString();
    }
}

/// A trivial null-check rule for the harness's destroyed-object rows:
/// "return x?.F();" becomes a local, an overloaded != and a plain call. Under
/// the name "unity-null" the harness allows, and checks, the one difference a
/// destroyed object may show.
sealed class ProbeUnityRule : Rule
{
    readonly bool _checksReference;

    /// With checksReference the rewrite tests the reference only, the mistake
    /// the unity-null rule must never make: a destroyed object gets through.
    public ProbeUnityRule(string name, bool checksReference = false)
    {
        Name = name;
        _checksReference = checksReference;
    }

    public override string Name { get; }

    public override void Plan(RulePlan plan)
    {
        var sites = plan.Root.DescendantNodes()
            .OfType<ConditionalAccessExpressionSyntax>();
        foreach (var site in sites)
        {
            if (site.Parent is ReturnStatementSyntax target)
            {
                var name = plan.Temps(site).Unique("probe");
                var check = name + " != null";
                if (_checksReference)
                {
                    check = "(object)" + check;
                }
                plan.Propose(
                    site,
                    target,
                    $"var {name} = {site.Expression};\nif ({check})\n{{\n"
                        + $"return {name}{site.WhenNotNull};\n}}\nreturn default;");
            }
        }
    }
}

/// A rule for the harness's own tests: the first statement that holds an
/// operator site becomes the given text, right or wrong. It lets the self test
/// show which wrong rewrites the harness catches.
sealed class ProbeTextRule : Rule
{
    readonly string _text;

    public ProbeTextRule(string name, string text)
    {
        Name = name;
        _text = text;
    }

    public override string Name { get; }

    public override void Plan(RulePlan plan)
    {
        var first = plan.Root.DescendantNodes()
            .FirstOrDefault(node => OperatorCount.KindOf(node) != null);
        if (first == null)
        {
            return;
        }
        var target = first.AncestorsAndSelf()
            .OfType<StatementSyntax>()
            .First();
        var sites = target.DescendantNodes()
            .Where(node => OperatorCount.KindOf(node) != null)
            .ToList();
        plan.ProposeAll(sites, target, _text);
    }
}
