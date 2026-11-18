using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace DarkwoodMP.Codemod.Census;

/// What kind of value an operator works on, and how stable a receiver is.
static class OperandClass
{
    public static string TypeClass(ITypeSymbol type, INamedTypeSymbol unity)
    {
        if (type == null)
        {
            return "unknown";
        }
        if (DerivesFrom(type, unity))
        {
            return "UnityEngine.Object (derived)";
        }
        if (type is ITypeParameterSymbol parameter)
        {
            if (parameter.ConstraintTypes.Any(c => DerivesFrom(c, unity)))
            {
                return "type parameter : UnityEngine.Object";
            }
            return "type parameter";
        }
        if (type.TypeKind == TypeKind.Interface)
        {
            return "interface";
        }
        if (type.SpecialType == SpecialType.System_Object)
        {
            return "System.Object";
        }
        if (type.TypeKind == TypeKind.Delegate)
        {
            return "delegate";
        }
        if (type.OriginalDefinition.SpecialType
            == SpecialType.System_Nullable_T)
        {
            return "Nullable<T>";
        }
        if (type.SpecialType == SpecialType.System_String)
        {
            return "string";
        }
        if (type.TypeKind == TypeKind.Array)
        {
            return "array";
        }
        if (type.IsValueType)
        {
            return "value type";
        }
        return "other class";
    }

    static bool DerivesFrom(ITypeSymbol type, INamedTypeSymbol unity)
    {
        for (var b = type as INamedTypeSymbol; b != null; b = b.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(b, unity))
            {
                return true;
            }
        }
        return false;
    }

    /// How stable a receiver is: can it change between two reads?
    public static string Stability(ExpressionSyntax receiver, SemanticModel model)
    {
        var operation = model.GetOperation(receiver);
        while (operation is IParenthesizedOperation parenthesized)
        {
            operation = parenthesized.Operand;
        }
        while (operation is IConversionOperation { IsImplicit: true } conversion)
        {
            operation = conversion.Operand;
        }
        switch (operation)
        {
            case ILocalReferenceOperation:
                return "local";
            case IParameterReferenceOperation:
                return "parameter";
            case IInstanceReferenceOperation:
                return "this";
            case IFieldReferenceOperation field:
                if (field.Field.IsStatic)
                {
                    return "static field";
                }
                if (field.Instance is IInstanceReferenceOperation)
                {
                    return "this.field";
                }
                return "field of another object";
            case IPropertyReferenceOperation property:
                if (property.Property.IsStatic)
                {
                    return "static property";
                }
                return "instance property";
            case IEventReferenceOperation:
                return "event";
            case IInvocationOperation:
                return "method call";
            case IArrayElementReferenceOperation:
                return "array element";
            case null:
                return "unknown";
            default:
                return operation.Kind.ToString();
        }
    }
}
