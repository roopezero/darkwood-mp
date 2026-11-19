using Microsoft.CodeAnalysis;
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
        "object.ReferenceEquals", "object.Equals", "object.GetType",
        "UnityEngine.Object.ReferenceEquals",
        "UnityEngine.Component.GetComponent",
        "UnityEngine.Component.CompareTag",
        "UnityEngine.GameObject.GetComponent",
        "UnityEngine.GameObject.CompareTag",
    };

    public static bool IsPureCall(IMethodSymbol method)
    {
        var type = method.ContainingType;
        if (type == null)
        {
            return false;
        }
        var original = type.OriginalDefinition.ToDisplayString();
        if (PureTypes.Contains(original))
        {
            return true;
        }
        return PureMethods.Contains(original + "." + method.Name);
    }

    /// .NET's and Unity's getters and indexers have no effects, and neither
    /// do this mod's auto-properties. Every other getter is a call.
    public static bool IsPureGetter(IPropertySymbol property)
    {
        return IsFramework(property) || IsAutoProperty(property);
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
