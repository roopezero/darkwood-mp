using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DarkwoodMP.Codemod.Interpolated;

/// Where a cut may go without anyone noticing that the holes in front of it
/// are now made into text earlier. The plugin's interpolated strings become
/// string.Format calls (net472 has no interpolated string handler), which
/// evaluate every hole first and only then call each value's ToString. Cut in
/// two, the head's values are made into text before the tail's holes are
/// evaluated. That can only show when making a head value into text runs code
/// that a tail hole could see or change, so a cut is safe when either:
///
/// - every hole in front of it has a type whose text depends only on its
///   value (string, a number, bool, char, an enum, or one of a few .NET and
///   Unity structs, which are copied when the hole is evaluated), or
/// - every hole behind it is inert: a constant, a literal, this, typeof,
///   default, or a local or parameter that isn't ref and that no lambda or
///   local function captures, so no ToString can change it.
///
/// The current culture is read by both forms at the same point in the text;
/// a hole that changes the culture itself is not looked for.
sealed class HoleOrder
{
    static readonly HashSet<string> PlainStructs = new()
    {
        "System.DateTime",
        "System.TimeSpan",
        "System.Guid",
        "UnityEngine.Vector2",
        "UnityEngine.Vector3",
        "UnityEngine.Vector4",
        "UnityEngine.Vector2Int",
        "UnityEngine.Vector3Int",
        "UnityEngine.Quaternion",
        "UnityEngine.Color",
        "UnityEngine.Color32",
        "UnityEngine.Rect",
        "UnityEngine.Bounds",
    };

    readonly bool[] _plain;
    readonly bool[] _inert;

    public HoleOrder(
        InterpolatedStringExpressionSyntax text, SemanticModel model)
    {
        var holes = text.Contents.OfType<InterpolationSyntax>().ToList();
        _plain = new bool[holes.Count];
        _inert = new bool[holes.Count];
        var captured = Captured(text, model);
        for (int i = 0; i < holes.Count; i++)
        {
            var type = model.GetTypeInfo(holes[i].Expression).Type;
            _plain[i] = PlainText(type);
            _inert[i] = Inert(holes[i].Expression, model, captured);
        }
    }

    /// May a cut have this many holes in front of it?
    public bool Safe(int before)
    {
        bool headPlain = true;
        for (int i = 0; i < before; i++)
        {
            headPlain &= _plain[i];
        }
        bool tailInert = true;
        for (int i = before; i < _inert.Length; i++)
        {
            tailInert &= _inert[i];
        }
        return headPlain || tailInert;
    }

    static bool PlainText(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol
            {
                OriginalDefinition.SpecialType:
                    SpecialType.System_Nullable_T,
            } nullable)
        {
            type = nullable.TypeArguments[0];
        }
        if (type == null)
        {
            return false;
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
                return true;
        }
        if (type.TypeKind == TypeKind.Enum)
        {
            return true;
        }
        return PlainStructs.Contains(type.ToDisplayString());
    }

    static bool Inert(
        ExpressionSyntax expression,
        SemanticModel model,
        HashSet<ISymbol> captured)
    {
        while (expression is ParenthesizedExpressionSyntax inner)
        {
            expression = inner.Expression;
        }
        if (model.GetConstantValue(expression).HasValue)
        {
            return true;
        }
        switch (expression.Kind())
        {
            case SyntaxKind.ThisExpression:
            case SyntaxKind.TypeOfExpression:
            case SyntaxKind.DefaultExpression:
            case SyntaxKind.DefaultLiteralExpression:
                return true;
        }
        if (expression is not IdentifierNameSyntax)
        {
            return false;
        }
        var symbol = model.GetSymbolInfo(expression).Symbol;
        if (captured == null || captured.Contains(symbol))
        {
            return false;
        }
        switch (symbol)
        {
            case ILocalSymbol local:
                return local.RefKind == RefKind.None;
            case IParameterSymbol parameter:
                return parameter.RefKind == RefKind.None;
            default:
                return false;
        }
    }

    /// The locals and parameters a lambda or local function captures in
    /// the member that holds the string, or null when the member's body
    /// can't be analysed (then no local counts as inert).
    static HashSet<ISymbol> Captured(SyntaxNode node, SemanticModel model)
    {
        SyntaxNode body = node.Ancestors().OfType<BlockSyntax>().LastOrDefault();
        if (body == null)
        {
            body = node.Ancestors()
                .OfType<ArrowExpressionClauseSyntax>()
                .LastOrDefault()
                ?.Expression;
        }
        DataFlowAnalysis flow = null;
        if (body is BlockSyntax block)
        {
            flow = model.AnalyzeDataFlow(block);
        }
        else if (body is ExpressionSyntax expression)
        {
            flow = model.AnalyzeDataFlow(expression);
        }
        if (flow == null || !flow.Succeeded)
        {
            return null;
        }
        return new HashSet<ISymbol>(
            flow.Captured, SymbolEqualityComparer.Default);
    }
}
