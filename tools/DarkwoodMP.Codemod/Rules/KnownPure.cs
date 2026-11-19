using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DarkwoodMP.Codemod.Rules;

/// Which getters, operators and calls are known to have no effects
/// (codemod.md section 8). Anything else of the game or the mod counts as a
/// call, because a getter or an operator can run any code. A name missing here
/// only sends a site to the queue.
static class KnownPure
{
    /// Every method of these types is free of effects.
    static readonly HashSet<string> PureTypes = new HashSet<string>
    {
        "System.Math", "System.MathF", "System.String", "System.Char",
        "System.Boolean", "System.Byte", "System.SByte", "System.Int16",
        "System.UInt16", "System.Int32", "System.UInt32", "System.Int64",
        "System.UInt64", "System.Single", "System.Double", "System.Decimal",
        "System.Convert", "System.Nullable<T>", "System.DateTime",
        "System.TimeSpan", "System.Guid",
        "UnityEngine.Mathf", "UnityEngine.Vector2", "UnityEngine.Vector3",
        "UnityEngine.Vector4", "UnityEngine.Vector2Int",
        "UnityEngine.Vector3Int", "UnityEngine.Quaternion", "UnityEngine.Color",
        "UnityEngine.Color32", "UnityEngine.Rect", "UnityEngine.Bounds",
    };

    /// Single methods of other types that only read.
    static readonly HashSet<string> PureMethods = new HashSet<string>
    {
        "System.Object.ReferenceEquals", "System.Object.Equals",
        "System.Object.GetType",
        "UnityEngine.Object.ReferenceEquals",
        "UnityEngine.Component.GetComponent",
        "UnityEngine.Component.CompareTag",
        "UnityEngine.GameObject.GetComponent",
        "UnityEngine.GameObject.CompareTag",
    };

    /// A type's name as the lists above write it: "System.String", where
    /// ToDisplayString() would say "string".
    static readonly SymbolDisplayFormat FullName = new SymbolDisplayFormat(
        typeQualificationStyle:
            SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters);

    public static bool IsPureCall(IMethodSymbol method)
    {
        var type = method.ContainingType;
        if (type == null)
        {
            return false;
        }
        if (IsPureType(type))
        {
            return true;
        }
        var original = type.OriginalDefinition.ToDisplayString(FullName);
        return PureMethods.Contains(original + "." + method.Name);
    }

    /// A type every method of which is free of effects.
    public static bool IsPureType(ITypeSymbol type)
    {
        var original = type.OriginalDefinition.ToDisplayString(FullName);
        return PureTypes.Contains(original);
    }

    /// .NET's and Unity's getters and indexers have no effects, and neither
    /// do this mod's auto-properties or getters that only read a field.
    /// Every other getter is a call.
    public static bool IsPureGetter(IPropertySymbol property)
    {
        return IsFramework(property) || IsAutoProperty(property)
            || FieldOf(property) != null;
    }

    /// The field a getter declared here does nothing but read, as
    /// "W => _w" or "get { return _w; }", or null for any other getter.
    /// Such a getter is an auto-property written out: reading it has no
    /// effects, and its value can change only when the field's can.
    public static IFieldSymbol FieldOf(IPropertySymbol property)
    {
        if (property.IsIndexer)
        {
            return null;
        }
        foreach (var reference in property.DeclaringSyntaxReferences)
        {
            var declaration =
                reference.GetSyntax() as PropertyDeclarationSyntax;
            if (declaration == null)
            {
                continue;
            }
            var name = FieldName(GetterValue(declaration));
            if (name == null)
            {
                continue;
            }
            // A simple name in a member's body binds to a member of its own
            // type first, so a field of that name is what the getter reads.
            foreach (var member in property.ContainingType.GetMembers(name))
            {
                if (member is IFieldSymbol field)
                {
                    return field;
                }
            }
        }
        return null;
    }

    /// What a property's getter returns, when its whole body is that one
    /// value; otherwise null.
    static ExpressionSyntax GetterValue(PropertyDeclarationSyntax declaration)
    {
        if (declaration.ExpressionBody != null)
        {
            return declaration.ExpressionBody.Expression;
        }
        if (declaration.AccessorList == null)
        {
            return null;
        }
        foreach (var accessor in declaration.AccessorList.Accessors)
        {
            if (!accessor.IsKind(SyntaxKind.GetAccessorDeclaration))
            {
                continue;
            }
            if (accessor.ExpressionBody != null)
            {
                return accessor.ExpressionBody.Expression;
            }
            if (accessor.Body != null
                && accessor.Body.Statements.Count == 1
                && accessor.Body.Statements[0] is ReturnStatementSyntax
                {
                    Expression: not null,
                } only)
            {
                return only.Expression;
            }
        }
        return null;
    }

    /// "_w" or "this._w" as a name, or null for anything else.
    static string FieldName(ExpressionSyntax value)
    {
        switch (value)
        {
            case IdentifierNameSyntax name:
                return name.Identifier.ValueText;
            case MemberAccessExpressionSyntax
            {
                Expression: ThisExpressionSyntax,
                Name: IdentifierNameSyntax name,
            }:
                return name.Identifier.ValueText;
            default:
                return null;
        }
    }

    /// Operators and conversions defined by .NET or Unity.
    public static bool IsPureOperator(IMethodSymbol method)
    {
        return IsFramework(method);
    }

    /// new of a .NET type or a Unity value type; a mod's or game's constructor
    /// and new GameObject() can do anything.
    public static bool IsPureCreation(IMethodSymbol constructor)
    {
        var type = constructor.ContainingType;
        if (InNamespace(type, "System"))
        {
            return true;
        }
        return type.IsValueType && InNamespace(type, "UnityEngine");
    }

    static bool IsFramework(ISymbol symbol)
    {
        return InNamespace(symbol, "System")
            || InNamespace(symbol, "UnityEngine");
    }

    static bool InNamespace(ISymbol symbol, string root)
    {
        var ns = symbol.ContainingNamespace;
        while (ns != null && !ns.IsGlobalNamespace)
        {
            if (ns.ContainingNamespace.IsGlobalNamespace && ns.Name == root)
            {
                return true;
            }
            ns = ns.ContainingNamespace;
        }
        return false;
    }

    /// Declared in this compilation with accessors that have no bodies.
    static bool IsAutoProperty(IPropertySymbol property)
    {
        if (property.IsIndexer)
        {
            return false;
        }
        foreach (var reference in property.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax() is PropertyDeclarationSyntax declaration
                && declaration.ExpressionBody == null
                && declaration.AccessorList != null
                && declaration.AccessorList.Accessors.All(
                    a => a.Body == null && a.ExpressionBody == null))
            {
                return true;
            }
        }
        return false;
    }
}
