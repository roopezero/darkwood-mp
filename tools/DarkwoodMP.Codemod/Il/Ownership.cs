using System.Text.RegularExpressions;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace DarkwoodMP.Codemod.Il;

/// Which source member the compiler made a generated type, method or field
/// for. A lambda is "<Foo>b__3_0", a local function "<Foo>g__Bar|3_0" and a
/// state machine "<Foo>d__12", all named after the member Foo. A closure class
/// "<>c__DisplayClass3_0" names no member, so it counts for the members whose
/// code mentions it.
///
/// A name is the same for every overload, so the exact owners are traced as
/// well: the source methods whose code leads to the generated code, through
/// the methods it calls or makes delegates of and the state machines and
/// closure classes it uses, as IlDiff.MemberKey spells them.
sealed class Ownership
{
    static readonly Regex Named = new Regex("^<+(?<owner>[^<>]+)>");
    static readonly Regex CacheField = new Regex("^<>9__(?<id>[0-9]+_[0-9]+)$");
    static readonly Regex LambdaId = new Regex("b__(?<id>[0-9]+_[0-9]+)$");

    /// The generated types that belong whole to the code that uses them: a
    /// state machine "<Foo>d__12" and a closure class. The shared "<>c" holds
    /// the lambdas of many members, so only the lambdas used count there.
    static readonly Regex WholeType = new Regex("^<[^<>]*>d__|^<>c__DisplayClass");

    readonly Dictionary<string, HashSet<string>> _types = new();
    readonly Dictionary<string, TypeDefinition> _generated = new();
    readonly Dictionary<string, TypeDefinition> _all = new();
    readonly Dictionary<MethodDefinition, HashSet<string>> _exactMethods = new();
    readonly Dictionary<TypeDefinition, HashSet<string>> _exactTypes = new();

    public Ownership(ModuleDefinition module)
    {
        var generated = IlTools.AllTypes(module).Where(IsGenerated).ToList();
        foreach (var t in generated)
        {
            _types[t.FullName] = NamedOwners(t);
            _generated[t.FullName] = t;
        }
        foreach (var t in IlTools.AllTypes(module))
        {
            _all[t.FullName] = t;
        }
        foreach (var t in IlTools.AllTypes(module))
        {
            foreach (var m in t.Methods.Where(m => !IsGeneratedMethod(m)))
            {
                Trace(m, IlDiff.MemberKey(m));
            }
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

    /// A lambda, local function or method of a generated type.
    public static bool IsGeneratedMethod(MethodDefinition m)
    {
        return m.Name.StartsWith('<') || IsGenerated(m.DeclaringType);
    }

    /// The source methods the generated method was traced to; empty when
    /// none leads to it.
    public IReadOnlyCollection<string> ExactOwnersOf(MethodDefinition m)
    {
        if (_exactMethods.TryGetValue(m, out var owners))
        {
            return owners;
        }
        return Array.Empty<string>();
    }

    /// The source methods a generated type was traced to: for a state machine
    /// or closure class the code that uses it, for any other generated type
    /// the owners of its methods together. A type written in source has none,
    /// although it holds the lambdas and local functions of its members: its
    /// fields are its own, and must be compared whatever the manifest holds.
    public IReadOnlyCollection<string> ExactOwnersOf(TypeDefinition t)
    {
        if (!IsGenerated(t))
        {
            return Array.Empty<string>();
        }
        if (_exactTypes.TryGetValue(t, out var owners))
        {
            return owners;
        }
        var all = new HashSet<string>();
        foreach (var m in t.Methods)
        {
            all.UnionWith(ExactOwnersOf(m));
        }
        return all;
    }

    /// A delegate cache field belongs to its lambda, as in OwnersOf; another
    /// field to its type, so a field of a type written in source to nothing.
    public IReadOnlyCollection<string> ExactOwnersOf(FieldDefinition f)
    {
        var lambda = LambdaOf(f);
        if (lambda != null)
        {
            return ExactOwnersOf(lambda);
        }
        return ExactOwnersOf(f.DeclaringType);
    }

    /// Credits to key every generated method and type that the source
    /// method's code leads to, directly or through other generated code.
    void Trace(MethodDefinition source, string key)
    {
        var seen = new HashSet<MethodDefinition>();
        var work = new Stack<MethodDefinition>();
        work.Push(source);
        while (work.Count > 0)
        {
            foreach (var next in Leads(work.Pop(), key))
            {
                if (seen.Add(next))
                {
                    Credit(_exactMethods, next, key);
                    work.Push(next);
                }
            }
        }
    }

    /// The generated methods a method's code leads to directly: the ones it
    /// calls or makes delegates of, and every method of a state machine or
    /// closure class it uses (MoveNext is never called by name). Such a type
    /// is credited to key as well.
    IEnumerable<MethodDefinition> Leads(MethodDefinition m, string key)
    {
        if (!m.HasBody)
        {
            yield break;
        }
        foreach (var instruction in m.Body.Instructions)
        {
            if (instruction.Operand is MethodReference called)
            {
                var element = called.GetElementMethod();
                _all.TryGetValue(Definition(element.DeclaringType), out var declaring);
                if (declaring == null)
                {
                    continue;
                }
                foreach (var target in declaring.Methods)
                {
                    if (target.Name == element.Name && IsGeneratedMethod(target))
                    {
                        yield return target;
                    }
                }
            }
        }
        foreach (var name in Mentioned(m).Distinct())
        {
            if (_generated.TryGetValue(name, out var type)
                && WholeType.IsMatch(type.Name))
            {
                Credit(_exactTypes, type, key);
                foreach (var whole in type.Methods)
                {
                    yield return whole;
                }
            }
        }
    }

    static void Credit<T>(Dictionary<T, HashSet<string>> map, T item, string key)
    {
        if (!map.TryGetValue(item, out var keys))
        {
            keys = new HashSet<string>();
            map[item] = keys;
        }
        keys.Add(key);
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
        var lambda = LambdaOf(f);
        if (lambda != null)
        {
            return OwnersOf(lambda);
        }
        return OwnersOf(f.DeclaringType);
    }

    /// The lambda "<Foo>b__3_0" a cache field "<>9__3_0" holds, or null.
    static MethodDefinition LambdaOf(FieldDefinition f)
    {
        var cache = CacheField.Match(f.Name);
        if (!cache.Success)
        {
            return null;
        }
        foreach (var m in f.DeclaringType.Methods)
        {
            var id = LambdaId.Match(m.Name);
            if (id.Success && id.Groups["id"].Value == cache.Groups["id"].Value)
            {
                return m;
            }
        }
        return null;
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
