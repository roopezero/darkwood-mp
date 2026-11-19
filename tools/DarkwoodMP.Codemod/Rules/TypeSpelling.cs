using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DarkwoodMP.Codemod.Rules;

/// How a rule writes a type into the file: the shortest spelling that binds
/// back to the same type at that place.
static class TypeSpelling
{
    /// The type as written at position, or null and why it can't be. The
    /// text must bind back to the same type there.
    public static string Spell(
        SemanticModel model,
        ITypeSymbol type,
        int position,
        out string refused)
    {
        refused = null;
        if (HasAnonymous(type))
        {
            refused = "an anonymous type, which can't be written";
            return null;
        }
        var text = Written(model, type, position);
        var written = SyntaxFactory.ParseTypeName(text);
        var bound = model.GetSpeculativeTypeInfo(
            position, written, SpeculativeBindingOption.BindAsTypeOrNamespace);
        if (!SymbolEqualityComparer.Default.Equals(bound.Type, type))
        {
            refused = "its type can't be written here: " + text;
            return null;
        }
        return text;
    }

    /// The shortest spelling, with "global::" in front of a type of the
    /// global namespace that a reader could take for another one: a type the
    /// file writes as "global::Name", such as the game's Button and
    /// EventTrigger, or one whose name a public type of .NET, of Unity or of
    /// a namespace the file imports also has, such as Cursor beside
    /// UnityEngine.Cursor. It binds to the game's type either way, but
    /// CLAUDE.md asks for these to be qualified.
    static string Written(SemanticModel model, ITypeSymbol type, int position)
    {
        var output = new StringBuilder();
        foreach (var part in type.ToMinimalDisplayParts(model, position))
        {
            if (part.Symbol is INamedTypeSymbol named
                && named.ContainingType == null
                && named.ContainingNamespace != null
                && named.ContainingNamespace.IsGlobalNamespace
                && Mistakable(model, named))
            {
                output.Append("global::");
            }
            output.Append(part.ToString());
        }
        return output.ToString();
    }

    static bool Mistakable(SemanticModel model, INamedTypeSymbol type)
    {
        var file = FileSpellings.Of(model);
        if (file.WrittenGlobal.Contains(type.OriginalDefinition))
        {
            return true;
        }
        if (FileSpellings.NetAndUnity(model.Compilation)
            .Contains(type.MetadataName))
        {
            return true;
        }
        foreach (var space in file.Imports)
        {
            if (space.GetTypeMembers(type.Name, type.Arity)
                .Any(FileSpellings.Visible))
            {
                return true;
            }
        }
        return false;
    }

    static bool HasAnonymous(ITypeSymbol type)
    {
        switch (type)
        {
            case IArrayTypeSymbol array:
                return HasAnonymous(array.ElementType);
            case IPointerTypeSymbol pointer:
                return HasAnonymous(pointer.PointedAtType);
            case INamedTypeSymbol named:
                return named.IsAnonymousType
                    || named.TypeArguments.Any(HasAnonymous);
            default:
                return false;
        }
    }
}
