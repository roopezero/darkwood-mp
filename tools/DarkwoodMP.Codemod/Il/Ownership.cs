using System.Text.RegularExpressions;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace DarkwoodMP.Codemod.Il;

/// Which source member the compiler made a generated type, method or field
/// for. A lambda is "<Foo>b__3_0", a local function "<Foo>g__Bar|3_0" and a
/// state machine "<Foo>d__12", all named after the member Foo. A closure class
/// "<>c__DisplayClass3_0" names no member, so it counts for the members whose
/// code mentions it.
sealed class Ownership
{
    static readonly Regex Named = new Regex("^<+(?<owner>[^<>]+)>");
    static readonly Regex CacheField = new Regex("^<>9__(?<id>[0-9]+_[0-9]+)$");
    static readonly Regex LambdaId = new Regex("b__(?<id>[0-9]+_[0-9]+)$");

    readonly Dictionary<string, HashSet<string>> _types = new();

    public Ownership(ModuleDefinition module)
    {
        var generated = IlTools.AllTypes(module).Where(IsGenerated).ToList();
        foreach (var t in generated)
        {
            _types[t.FullName] = NamedOwners(t);
        }
        var users = Users(module, generated);
        bool grew = true;
        while (grew)
        {
            grew = false;
            foreach (var (type, methods) in users)
            {
                foreach (var user in methods)
                {
                    foreach (var owner in OwnersOf(user))
                    {
                        grew |= _types[type].Add(owner);
                    }
                }
            }
        }
    }

    /// Made by the compiler: "<>c", "<Foo>d__12", "<>f__AnonymousType0", ...
    public static bool IsGenerated(TypeDefinition t)
    {
        return t.Name.StartsWith('<') && t.Name != "<Module>";
    }

    /// Every member the method belongs to, as "Ns.Type::Name".
    public IReadOnlyCollection<string> OwnersOf(MethodDefinition m)
    {
        var real = RealType(m.DeclaringType);
        var named = NamedOwner(m.Name);
        if (named != null && real != null)
        {
            return new[] { real + "::" + named };
        }
        if (IsGenerated(m.DeclaringType))
        {
            return OwnersOf(m.DeclaringType);
        }
        return new[] { m.DeclaringType.FullName + "::" + m.Name };
    }

    /// The members a generated type belongs to; empty when it has no owner,
    /// like <PrivateImplementationDetails>.
    public IReadOnlyCollection<string> OwnersOf(TypeDefinition t)
    {
        if (_types.TryGetValue(t.FullName, out var owners))
        {
            return owners;
        }
        return Array.Empty<string>();
    }

    /// A delegate cache field "<>9__3_0" of the shared closure class belongs
    /// to the lambda "<Foo>b__3_0" beside it; other fields to the whole type.
    public IReadOnlyCollection<string> OwnersOf(FieldDefinition f)
    {
        var cache = CacheField.Match(f.Name);
        if (cache.Success)
        {
            foreach (var m in f.DeclaringType.Methods)
            {
                var id = LambdaId.Match(m.Name);
                if (id.Success && id.Groups["id"].Value == cache.Groups["id"].Value)
                {
                    return OwnersOf(m);
                }
            }
        }
        return OwnersOf(f.DeclaringType);
    }

    static string NamedOwner(string name)
    {
        var match = Named.Match(name);
        if (match.Success)
        {
            return match.Groups["owner"].Value;
        }
        return null;
    }

    /// The nearest enclosing type written in source; null for a top-level
    /// generated type.
    static string RealType(TypeDefinition t)
    {
        while (t != null && IsGenerated(t))
        {
            t = t.DeclaringType;
        }
        if (t == null)
        {
            return null;
        }
        return t.FullName;
    }

    static HashSet<string> NamedOwners(TypeDefinition t)
    {
        var owners = new HashSet<string>();
        var real = RealType(t);
        if (real == null)
        {
            return owners;
        }
        var own = NamedOwner(t.Name);
        if (own != null)
        {
            owners.Add(real + "::" + own);
        }
        foreach (var m in t.Methods)
        {
            var named = NamedOwner(m.Name);
            if (named != null)
            {
                owners.Add(real + "::" + named);
            }
        }
        return owners;
    }

    /// For each generated type, the methods whose code mentions it.
    static Dictionary<string, List<MethodDefinition>> Users(
        ModuleDefinition module, List<TypeDefinition> generated)
    {
        var users = generated.ToDictionary(
            t => t.FullName, t => new List<MethodDefinition>());
        foreach (var t in IlTools.AllTypes(module))
        {
            foreach (var m in t.Methods.Where(m => m.HasBody))
            {
                foreach (var name in Mentioned(m).Distinct())
                {
                    if (users.TryGetValue(name, out var list))
                    {
                        list.Add(m);
                    }
                }
            }
        }
        return users;
    }

    static IEnumerable<string> Mentioned(MethodDefinition m)
    {
        foreach (var local in m.Body.Variables)
        {
            yield return Definition(local.VariableType);
        }
        foreach (var instruction in m.Body.Instructions)
        {
            switch (instruction.Operand)
            {
                case TypeReference type:
                    yield return Definition(type);
                    break;
                case MethodReference method:
                    yield return Definition(method.DeclaringType);
                    break;
                case FieldReference field:
                    yield return Definition(field.DeclaringType);
                    break;
            }
        }
    }

    /// The name of the type definition behind an array, pointer, by-ref or
    /// generic instantiation.
    static string Definition(TypeReference type)
    {
        return type.GetElementType().FullName;
    }
}
