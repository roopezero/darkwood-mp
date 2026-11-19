using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DarkwoodMP.Codemod.Census;

/// The ?:, ??, ??= and ?. operators the operator rules have to remove, by
/// where they are and what they work on.
sealed class OperatorCensus
{
    readonly INamedTypeSymbol _unity;
    readonly Samples _samples;

    public readonly Tally Ternary = new Tally("?: by context");
    public readonly Tally Coalesce = new Tally("?? by context");
    public readonly Tally CoalesceAssign = new Tally("??= by context");
    public readonly Tally Conditional = new Tally("?. chains by context");

    readonly Tally _ternaryRewrite = new Tally("?: how it can be rewritten");
    readonly Tally _ternaryType = new Tally("?: result type against its target");
    readonly Tally _coalesceRewrite = new Tally("?? how it can be rewritten");
    readonly Tally _coalesceLeft = new Tally("?? left operand type");
    readonly Tally _coalesceAssignLeft = new Tally("??= left operand type");
    readonly Tally _conditionalRewrite = new Tally("?. how it can be rewritten");
    readonly Tally _receiver = new Tally("?. receiver type");
    readonly Tally _stability = new Tally("?. receiver stability");

    public OperatorCensus(INamedTypeSymbol unity, Samples samples)
    {
        _unity = unity;
        _samples = samples;
    }

    public void Count(SyntaxNode root, SemanticModel model, string file)
    {
        foreach (var node in root.DescendantNodes())
        {
            switch (node)
            {
                case ConditionalExpressionSyntax c:
                    CountTernary(c, model, file);
                    break;
                case BinaryExpressionSyntax b
                        when b.IsKind(SyntaxKind.CoalesceExpression):
                    CountCoalesce(b, model, file);
                    break;
                case AssignmentExpressionSyntax a
                        when a.IsKind(SyntaxKind.CoalesceAssignmentExpression):
                    CountCoalesceAssign(a, model, file);
                    break;
                case ConditionalAccessExpressionSyntax a
                        when !Rules.OperatorCount.IsInnerLink(a):
                    CountConditional(a, model, file);
                    break;
            }
        }
    }

    void CountTernary(
        ConditionalExpressionSyntax node, SemanticModel model, string file)
    {
        var context = SiteContext.Of(node);
        Ternary.Add(context, file);
        _samples.Add("?: " + context, node);
        _ternaryRewrite.Add(RewriteKind.Of(node), file);
        var info = model.GetTypeInfo(node);
        if (info.Type == null)
        {
            _ternaryType.Add("no natural type (target-typed)", file);
        }
        else if (SymbolEqualityComparer.Default.Equals(
            info.Type, info.ConvertedType))
        {
            _ternaryType.Add("same type", file);
        }
        else if (info.ConvertedType?.SpecialType == SpecialType.System_Object)
        {
            _ternaryType.Add("converted after the branch: to object", file);
        }
        else
        {
            _ternaryType.Add("converted after the branch: other", file);
        }
    }

    void CountCoalesce(
        BinaryExpressionSyntax node, SemanticModel model, string file)
    {
        var context = SiteContext.Of(node);
        Coalesce.Add(context, file);
        _samples.Add("?? " + context, node);
        _coalesceRewrite.Add(RewriteKind.Of(node), file);
        var left = model.GetTypeInfo(node.Left).Type;
        _coalesceLeft.Add(OperandClass.TypeClass(left, _unity), file);
    }

    void CountCoalesceAssign(
        AssignmentExpressionSyntax node, SemanticModel model, string file)
    {
        var context = "nested in an expression";
        if (node.Parent is ExpressionStatementSyntax)
        {
            context = "statement";
        }
        CoalesceAssign.Add(context, file);
        _samples.Add("??= " + context, node);
        var left = model.GetTypeInfo(node.Left).Type;
        _coalesceAssignLeft.Add(OperandClass.TypeClass(left, _unity), file);
    }

    void CountConditional(
        ConditionalAccessExpressionSyntax node,
        SemanticModel model,
        string file)
    {
        var context = SiteContext.Of(node);
        Conditional.Add(context, file);
        _samples.Add("?. " + context, node);
        _conditionalRewrite.Add(RewriteKind.Of(node), file);
        var type = model.GetTypeInfo(node.Expression).Type;
        var receiver = OperandClass.TypeClass(type, _unity);
        _receiver.Add(receiver, file);
        _samples.Add("?. receiver " + receiver, node);
        _stability.Add(OperandClass.Stability(node.Expression, model), file);
    }

    public void Print()
    {
        Ternary.Print();
        _ternaryRewrite.Print();
        _ternaryType.Print();
        Coalesce.Print();
        _coalesceRewrite.Print();
        _coalesceLeft.Print();
        CoalesceAssign.Print();
        _coalesceAssignLeft.Print();
        Conditional.Print();
        _conditionalRewrite.Print();
        _receiver.Print();
        _stability.Print();
    }
}
