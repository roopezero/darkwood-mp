using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace DarkwoodMP.Codemod.Rules;

/// What making a value into text runs. An interpolation hole, a string +
/// and string's own Format, Concat and Join call ToString (or
/// IFormattable.ToString) on each value that isn't a string yet, and that
/// can be code of the game or the mod where the source shows no call: a
/// ToString of their own, or of a class derived from the one written. When
/// it runs depends on how the compiler lowers the string: after every hole
/// (string.Format), or right after each value (an interpolation handler, a
/// + of strings). ternary-hoist assumes the earliest, so only a plain value
/// may be made into text before the code it moves.
static class Formatting
{
    /// Its text is its value and nothing of the game or the mod makes it: a
    /// string, a number, a bool, a char, an enum, a date, and the .NET and
    /// Unity value types KnownPure lists, such as Vector3. A null has no
    /// type and becomes "".
    public static bool IsPlain(ITypeSymbol type)
    {
        if (type == null)
        {
            return true;
        }
        if (type is INamedTypeSymbol named
            && named.OriginalDefinition.SpecialType
                == SpecialType.System_Nullable_T)
        {
            return IsPlain(named.TypeArguments[0]);
        }
        if (type.TypeKind == TypeKind.Enum)
        {
            return true;
        }
        switch (type.SpecialType)
        {
            case SpecialType.System_String:
            case SpecialType.System_Boolean:
            case SpecialType.System_Char:
            case SpecialType.System_SByte:
            case SpecialType.System_Byte:
            case SpecialType.System_Int16:
            case SpecialType.System_UInt16:
            case SpecialType.System_Int32:
            case SpecialType.System_UInt32:
            case SpecialType.System_Int64:
            case SpecialType.System_UInt64:
            case SpecialType.System_Single:
            case SpecialType.System_Double:
            case SpecialType.System_Decimal:
            case SpecialType.System_DateTime:
                return true;
        }
        return type.IsValueType && KnownPure.IsPureType(type);
    }

    /// The first value the operation makes into text that isn't plain, or
    /// null. The body of a lambda in it doesn't run here, so it is left out,
    /// and so is what moved into a temporary, whose value is still made
    /// into text where it was.
    public static IOperation Loud(
        IOperation operation, ICollection<SyntaxNode> moved)
    {
        if (operation == null
            || operation is IAnonymousFunctionOperation
            || operation is ILocalFunctionOperation
            || (operation is not IConversionOperation
                && moved.Contains(operation.Syntax)))
        {
            return null;
        }
        foreach (var (type, at) in MadeIntoText(operation))
        {
            if (!IsPlain(type))
            {
                return at;
            }
        }
        foreach (var child in operation.ChildOperations)
        {
            var found = Loud(child, moved);
            if (found != null)
            {
                return found;
            }
        }
        return null;
    }

    /// The types this one operation makes into text, with where.
    static IEnumerable<(ITypeSymbol Type, IOperation At)> MadeIntoText(
        IOperation operation)
    {
        switch (operation)
        {
            case IInterpolationOperation hole:
                yield return (hole.Expression.Type, hole.Expression);
                break;
            case IBinaryOperation { OperatorKind: BinaryOperatorKind.Add } add
                    when IsString(add.Type):
                var sides = new[] { add.LeftOperand, add.RightOperand };
                foreach (var side in sides)
                {
                    var value = Unconverted(side);
                    yield return (value.Type, value);
                }
                break;
            case IInvocationOperation call when Formats(call.TargetMethod):
                foreach (var argument in call.Arguments)
                {
                    if (IsFormatProvider(argument.Parameter))
                    {
                        continue;
                    }
                    foreach (var found in Elements(argument.Value))
                    {
                        yield return found;
                    }
                }
                break;
        }
    }

    /// string.Format, Concat and Join make their arguments into text.
    static bool Formats(IMethodSymbol method)
    {
        return IsString(method.ContainingType)
            && (method.Name == "Format" || method.Name == "Concat"
                || method.Name == "Join");
    }

    /// The values an argument hands over to be made into text: the
    /// elements of a params array or of a sequence, or the value itself.
    static IEnumerable<(ITypeSymbol Type, IOperation At)> Elements(
        IOperation value)
    {
        value = Unconverted(value);
        if (value is IArrayCreationOperation { Initializer: not null } array)
        {
            foreach (var element in array.Initializer.ElementValues)
            {
                var bare = Unconverted(element);
                yield return (bare.Type, bare);
            }
            yield break;
        }
        yield return (ElementType(value.Type), value);
    }

    /// The type of what a string, an array or a sequence holds; the type
    /// itself for any other.
    static ITypeSymbol ElementType(ITypeSymbol type)
    {
        if (type == null || IsString(type))
        {
            return type;
        }
        if (type is IArrayTypeSymbol array)
        {
            return array.ElementType;
        }
        var types = new List<ITypeSymbol> { type };
        types.AddRange(type.AllInterfaces);
        foreach (var face in types)
        {
            if (face is INamedTypeSymbol named
                && named.OriginalDefinition.SpecialType
                    == SpecialType.System_Collections_Generic_IEnumerable_T)
            {
                return named.TypeArguments[0];
            }
        }
        return type;
    }

    static bool IsFormatProvider(IParameterSymbol parameter)
    {
        return parameter != null
            && parameter.Type.ToDisplayString() == "System.IFormatProvider";
    }

    static IOperation Unconverted(IOperation operation)
    {
        while (operation is IConversionOperation
            {
                IsImplicit: true,
            } conversion)
        {
            operation = conversion.Operand;
        }
        return operation;
    }

    static bool IsString(ITypeSymbol type)
    {
        return type != null && type.SpecialType == SpecialType.System_String;
    }
}
