using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DarkwoodMP.Codemod.Rules;

/// The manifest's name for the member that holds a piece of code, spelled the
/// way Mono.Cecil spells it in the built DLL: "Ns.Outer/Inner::Name(Param,Types)".
/// Code in a field or property initializer runs in the constructors, so it is
/// credited to ".ctor" or ".cctor" of every overload.
static class MemberKeys
{
    public static string Of(SyntaxNode node, SemanticModel model)
    {
        foreach (var ancestor in node.AncestorsAndSelf())
        {
            switch (ancestor)
            {
                case AccessorDeclarationSyntax a:
                    return Key((IMethodSymbol)model.GetDeclaredSymbol(a));
                case BaseMethodDeclarationSyntax m:
                    return Key((IMethodSymbol)model.GetDeclaredSymbol(m));
                case ArrowExpressionClauseSyntax { Parent: BasePropertyDeclarationSyntax p }:
                    var property = (IPropertySymbol)model.GetDeclaredSymbol(p);
                    return Key(property.GetMethod);
                case PropertyDeclarationSyntax p:
                    return Initializer(model.GetDeclaredSymbol(p));
                case VariableDeclaratorSyntax
                {
                    Parent.Parent: BaseFieldDeclarationSyntax,
                } v:
                    return Initializer(model.GetDeclaredSymbol(v));
            }
        }
        return null;
    }

    static string Initializer(ISymbol member)
    {
        var name = ".ctor";
        if (member.IsStatic)
        {
            name = ".cctor";
        }
        return TypeKey(member.ContainingType) + "::" + name;
    }

    public static string Key(IMethodSymbol method)
    {
        var parameters = method.Parameters.Select(ParameterType);
        return TypeKey(method.ContainingType) + "::" + method.MetadataName
            + "(" + string.Join(",", parameters) + ")";
    }

    static string ParameterType(IParameterSymbol parameter)
    {
        var name = TypeName(parameter.Type);
        if (parameter.RefKind != RefKind.None)
        {
            name += "&";
        }
        return name;
    }

    public static string TypeKey(INamedTypeSymbol type)
    {
        var chain = new Stack<string>();
        for (var t = type; t != null; t = t.ContainingType)
        {
            chain.Push(t.MetadataName);
        }
        var prefix = "";
        var ns = type.ContainingNamespace;
        if (ns != null && !ns.IsGlobalNamespace)
        {
            prefix = ns.ToDisplayString() + ".";
        }
        return prefix + string.Join("/", chain);
    }

    static string TypeName(ITypeSymbol type)
    {
        switch (type)
        {
            case IArrayTypeSymbol array:
                return TypeName(array.ElementType) + Rank(array.Rank);
            case IPointerTypeSymbol pointer:
                return TypeName(pointer.PointedAtType) + "*";
            case ITypeParameterSymbol parameter:
                return parameter.Name;
            case IDynamicTypeSymbol:
                return "System.Object";
            case INamedTypeSymbol named:
                return Named(named);
            default:
                return type.ToDisplayString();
        }
    }

    /// Cecil writes int[,] as "System.Int32[0...,0...]".
    static string Rank(int rank)
    {
        if (rank == 1)
        {
            return "[]";
        }
        return "[" + string.Join(",", Enumerable.Repeat("0...", rank)) + "]";
    }

    /// Generic arguments of the whole nesting chain come at the end, in order.
    static string Named(INamedTypeSymbol type)
    {
        var arguments = new List<string>();
        CollectArguments(type, arguments);
        if (arguments.Count == 0)
        {
            return TypeKey(type);
        }
        return TypeKey(type) + "<" + string.Join(",", arguments) + ">";
    }

    static void CollectArguments(INamedTypeSymbol type, List<string> into)
    {
        if (type.ContainingType != null)
        {
            CollectArguments(type.ContainingType, into);
        }
        into.AddRange(type.TypeArguments.Select(TypeName));
    }
}
