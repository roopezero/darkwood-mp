using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace DarkwoodMP.Codemod.Rules;

/// Whether moving an expression in front of its statement can be noticed
/// (guide section 4.6, codemod.md section 5.3). It can't in two cases only:
/// the expression has no effects and nothing that runs before it has any,
/// or it has effects and everything before it is a value it can't change
/// and that can't throw. On top of Purity's view of effects, the code that
/// makes a value into text counts as a call (Formatting), and so does a
/// user-defined conversion the compiler applies to an earlier operand. A
/// volatile read keeps the reads after it behind it, so none may move in
/// front of one. Which exception comes first when both the expression and
/// something before it throw is not looked at.
sealed class HoistHazards
{
    /// A read that would throw before the moved code ran then throws after
    /// it, once the moved code's effects have happened.
    public const string MayThrowEarlier =
        "the hoisted code has effects and a read earlier can throw";

    /// A volatile read keeps every read after it behind it, and the move
    /// would put one in front of it.
    public const string VolatileEarlier = "a volatile field is read earlier";

    static readonly SyntaxNode[] NoneMoved = Array.Empty<SyntaxNode>();

    readonly SemanticModel _model;
    readonly Purity _purity;

    public HoistHazards(SemanticModel model, Purity purity)
    {
        _model = model;
        _purity = purity;
    }

    /// Null when the expression may run before everything the path says
    /// runs before it, apart from the expressions in moved, which move in
    /// front of the statement too, in their order; otherwise why not.
    public string WhyNotBefore(
        ExpressionSyntax hoisted,
        HoistPath path,
        ICollection<SyntaxNode> moved)
    {
        bool loud = HasEffects(hoisted)
            || ReadsVolatile(_model.GetOperation(hoisted), NoneMoved);
        foreach (var operand in path.Earlier)
        {
            // A conversion of a value stays where the value was used, even
            // when the value itself moved.
            if (ConvertsByCall(operand))
            {
                return Purity.CallEarlier + ": converting " + operand;
            }
            if (moved.Contains(TernaryChain.Strip(operand)))
            {
                continue;
            }
            if (ReadsVolatile(_model.GetOperation(operand), moved))
            {
                return VolatileEarlier + ": " + operand;
            }
            if (!loud && HasEffects(operand, moved))
            {
                return Purity.CallEarlier + ": " + operand;
            }
            if (loud && !_purity.IsStable(operand, hoisted))
            {
                return Purity.ReadEarlier + ": " + operand;
            }
            if (loud && MayThrow(operand))
            {
                return MayThrowEarlier + ": " + operand;
            }
        }
        // A value made into text stays where it is, moved or not: only a
        // plain value's text can't depend on when the moved code runs.
        foreach (var value in path.Formatted)
        {
            if (!Formatting.IsPlain(_model.GetTypeInfo(value).Type))
            {
                return Purity.CallEarlier + ": making " + value + " into text";
            }
        }
        return null;
    }

    /// Purity's effects, and code that makes a value into text.
    public bool HasEffects(ExpressionSyntax expression)
    {
        return HasEffects(expression, NoneMoved);
    }

    /// The same for what is left of the expression once the parts in moved
    /// have gone into temporaries.
    bool HasEffects(ExpressionSyntax expression, ICollection<SyntaxNode> moved)
    {
        if (_purity.HasEffects(expression, moved))
        {
            return true;
        }
        var operation = _model.GetOperation(expression);
        return Formatting.Loud(operation, moved) != null;
    }

    /// A read of a volatile field in the operation, apart from what moved
    /// and the bodies of lambdas.
    static bool ReadsVolatile(
        IOperation operation, ICollection<SyntaxNode> moved)
    {
        if (operation == null
            || operation is IAnonymousFunctionOperation
            || operation is ILocalFunctionOperation
            || (operation is not IConversionOperation
                && moved.Contains(operation.Syntax)))
        {
            return false;
        }
        if (operation is IFieldReferenceOperation { Field.IsVolatile: true })
        {
            return true;
        }
        foreach (var child in operation.ChildOperations)
        {
            if (ReadsVolatile(child, moved))
            {
                return true;
            }
        }
        return false;
    }

    /// A user-defined conversion the compiler applies to the operand where
    /// it is used, such as an argument converted to its parameter's type.
    /// The source shows no call, but the conversion runs then.
    bool ConvertsByCall(ExpressionSyntax operand)
    {
        var operation = _model.GetOperation(operand);
        if (operation == null)
        {
            return false;
        }
        for (var parent = operation.Parent;
            parent is IConversionOperation { IsImplicit: true } conversion;
            parent = parent.Parent)
        {
            if (conversion.OperatorMethod != null
                && !KnownPure.IsPureOperator(conversion.OperatorMethod))
            {
                return true;
            }
        }
        return false;
    }

    /// A member read through an object that may be null throws there.
    /// Locals, parameters, this and literals never throw.
    bool MayThrow(ExpressionSyntax operand)
    {
        var bare = TernaryChain.Strip(operand);
        if (bare is not MemberAccessExpressionSyntax member)
        {
            return false;
        }
        var receiver = TernaryChain.Strip(member.Expression);
        if (receiver is ThisExpressionSyntax
            || receiver is BaseExpressionSyntax)
        {
            return false;
        }
        if (_model.GetSymbolInfo(receiver).Symbol is INamespaceOrTypeSymbol)
        {
            return false;
        }
        var type = _model.GetTypeInfo(receiver).Type;
        if (type == null || !type.IsValueType)
        {
            return true;
        }
        return MayThrow(receiver);
    }
}
