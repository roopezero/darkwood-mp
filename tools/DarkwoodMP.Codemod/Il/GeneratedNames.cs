using System.Text;
using System.Text.RegularExpressions;
using Mono.Cecil;

namespace DarkwoodMP.Codemod.Il;

/// Compiler-generated names carry the ordinal of their member inside its type
/// ("<Foo>b__3_0", "<>c__DisplayClass3_0", "<Foo>d__12"), and adding a helper
/// to a type shifts the ordinals of everything after it. This class spells a
/// name with the ordinal replaced by its rank among the ordinals of that
/// member's generated names, so a shifted name compares as the same name. Text
/// that names types and members goes through here before it is compared.
sealed class GeneratedNames
{
    /// Names everything as Cecil does; for rendering a single method.
    public static readonly GeneratedNames Identity = new GeneratedNames();

    static readonly Regex Lambda = new Regex(
        @"^<(?<owner>[^<>]+)>b__(?<n>\d+)_(?<m>\d+)$");
    static readonly Regex Local = new Regex(
        @"^<(?<owner>[^<>]+)>g__(?<name>.+)\|(?<n>\d+)_(?<m>\d+)$");
    static readonly Regex Machine = new Regex(
        @"^<(?<owner>[^<>]+)>d__(?<n>\d+)$");
    static readonly Regex LocalMachine = new Regex(
        @"^<<(?<owner>[^<>]+)>g__(?<name>.+)\|(?<n>\d+)_(?<m>\d+)>d$");
    static readonly Regex Closure = new Regex(
        @"^<>c__DisplayClass(?<n>\d+)_(?<m>\d+)(?<arity>`\d+)?$");
    static readonly Regex GenericClosure = new Regex(
        @"^<>c__(?<n>\d+)(?<arity>`\d+)?$");
    static readonly Regex CacheField = new Regex(
        @"^<>9__(?<n>\d+)_(?<m>\d+)$");

    readonly Dictionary<(string Real, string Raw), string> _canonical = new();
    readonly Dictionary<(string Real, string Owner), SortedSet<int>> _ordinals =
        new();

    GeneratedNames()
    {
    }

    public GeneratedNames(ModuleDefinition module, Ownership owners)
    {
        var types = IlTools.AllTypes(module).ToList();
        foreach (var t in types)
        {
            Collect(t, owners);
        }
        foreach (var t in types)
        {
            Assign(t, owners);
        }
    }

    public string Of(TypeReference type)
    {
        switch (type)
        {
            case GenericInstanceType generic:
                return Of(generic.ElementType) + "<"
                    + string.Join(",", generic.GenericArguments.Select(Of)) + ">";
            case ArrayType array:
                return Of(array.ElementType) + "[" + array.Rank + "]";
            case ByReferenceType reference:
                return Of(reference.ElementType) + "&";
            case PointerType pointer:
                return Of(pointer.ElementType) + "*";
            case PinnedType pinned:
                return Of(pinned.ElementType) + " pinned";
            case OptionalModifierType optional:
                return Of(optional.ElementType)
                    + " modopt(" + Of(optional.ModifierType) + ")";
            case RequiredModifierType required:
                return Of(required.ElementType)
                    + " modreq(" + Of(required.ModifierType) + ")";
            case GenericParameter parameter:
                return parameter.Name;
        }
        if (type.DeclaringType == null)
        {
            return type.FullName;
        }
        return Of(type.DeclaringType) + "/" + MemberName(type.Name, type.DeclaringType);
    }

    public string Of(MethodReference method)
    {
        var text = new StringBuilder();
        text.Append(Of(method.ReturnType)).Append(' ');
        text.Append(Of(method.DeclaringType)).Append("::");
        text.Append(MemberName(method.Name, method.DeclaringType));
        if (method is GenericInstanceMethod generic)
        {
            text.Append('<');
            text.Append(string.Join(",", generic.GenericArguments.Select(Of)));
            text.Append('>');
        }
        text.Append('(');
        text.Append(string.Join(
            ",", method.Parameters.Select(p => Of(p.ParameterType))));
        text.Append(')');
        return text.ToString();
    }

    public string Of(FieldReference field)
    {
        return Of(field.FieldType) + " " + Of(field.DeclaringType) + "::"
            + MemberName(field.Name, field.DeclaringType);
    }

    /// The name of a member of this type, without the member ordinal.
    public string MemberName(string raw, TypeReference declaring)
    {
        var key = (RealType(declaring), raw);
        if (_canonical.TryGetValue(key, out var canonical))
        {
            return canonical;
        }
        return raw;
    }

    /// The nearest enclosing type written in source.
    static string RealType(TypeReference type)
    {
        var t = type.GetElementType();
        while (t.DeclaringType != null && t.Name.StartsWith('<'))
        {
            t = t.DeclaringType.GetElementType();
        }
        return t.FullName;
    }

    void Collect(TypeDefinition t, Ownership owners)
    {
        var real = RealType(t);
        foreach (var m in t.Methods)
        {
            Note(real, Lambda.Match(m.Name));
            Note(real, Local.Match(m.Name));
        }
        Note(real, Machine.Match(t.Name));
        Note(real, LocalMachine.Match(t.Name));
        var closure = Closure.Match(t.Name);
        if (!closure.Success)
        {
            closure = GenericClosure.Match(t.Name);
        }
        if (closure.Success)
        {
            Add(real, OwnerOf(t, owners), Number(closure, "n"));
        }
    }

    void Note(string real, Match match)
    {
        if (match.Success)
        {
            Add(real, match.Groups["owner"].Value, Number(match, "n"));
        }
    }

    void Add(string real, string owner, int ordinal)
    {
        var key = (real, owner);
        if (!_ordinals.TryGetValue(key, out var set))
        {
            set = new SortedSet<int>();
            _ordinals[key] = set;
        }
        set.Add(ordinal);
    }

    static int Number(Match match, string group)
    {
        return int.Parse(match.Groups[group].Value);
    }

    /// The member a closure class belongs to, by name.
    static string OwnerOf(TypeDefinition t, Ownership owners)
    {
        var first = owners.OwnersOf(t)
            .OrderBy(o => o, StringComparer.Ordinal)
            .FirstOrDefault();
        if (first == null)
        {
            return "?";
        }
        return first.Substring(first.IndexOf("::", StringComparison.Ordinal) + 2);
    }

    /// The ordinal's place among the ordinals of this member's generated names.
    int Rank(string real, string owner, int ordinal)
    {
        return _ordinals[(real, owner)].TakeWhile(o => o < ordinal).Count();
    }

    void Assign(TypeDefinition t, Ownership owners)
    {
        var real = RealType(t);
        foreach (var m in t.Methods)
        {
            AssignMethod(real, m.Name);
        }
        AssignType(real, t, owners);
        foreach (var f in t.Fields)
        {
            AssignCacheField(real, t, f.Name);
        }
    }

    void AssignMethod(string real, string name)
    {
        var lambda = Lambda.Match(name);
        if (lambda.Success)
        {
            var rank = RankOf(real, lambda);
            _canonical[(real, name)] =
                $"<{lambda.Groups["owner"].Value}>b__#{rank}_{lambda.Groups["m"].Value}";
        }
        var local = Local.Match(name);
        if (local.Success)
        {
            var rank = RankOf(real, local);
            _canonical[(real, name)] = $"<{local.Groups["owner"].Value}>g__"
                + $"{local.Groups["name"].Value}|#{rank}_{local.Groups["m"].Value}";
        }
    }

    void AssignType(string real, TypeDefinition t, Ownership owners)
    {
        var machine = Machine.Match(t.Name);
        if (machine.Success)
        {
            var rank = RankOf(real, machine);
            _canonical[(real, t.Name)] =
                $"<{machine.Groups["owner"].Value}>d__#{rank}";
        }
        var local = LocalMachine.Match(t.Name);
        if (local.Success)
        {
            var rank = RankOf(real, local);
            _canonical[(real, t.Name)] = $"<<{local.Groups["owner"].Value}>g__"
                + $"{local.Groups["name"].Value}|#{rank}_{local.Groups["m"].Value}>d";
        }
        var closure = Closure.Match(t.Name);
        if (closure.Success)
        {
            var owner = OwnerOf(t, owners);
            var rank = Rank(real, owner, Number(closure, "n"));
            _canonical[(real, t.Name)] = $"<>c__DisplayClass#{owner}#{rank}"
                + $"_{closure.Groups["m"].Value}{closure.Groups["arity"].Value}";
        }
        var generic = GenericClosure.Match(t.Name);
        if (generic.Success)
        {
            var owner = OwnerOf(t, owners);
            var rank = Rank(real, owner, Number(generic, "n"));
            _canonical[(real, t.Name)] =
                $"<>c__#{owner}#{rank}{generic.Groups["arity"].Value}";
        }
    }

    /// "<>9__3_0" takes the owner and rank of the lambda "<Foo>b__3_0" it
    /// caches.
    void AssignCacheField(string real, TypeDefinition t, string name)
    {
        var cache = CacheField.Match(name);
        if (!cache.Success)
        {
            return;
        }
        var suffix = $"b__{cache.Groups["n"].Value}_{cache.Groups["m"].Value}";
        foreach (var m in t.Methods)
        {
            var lambda = Lambda.Match(m.Name);
            if (lambda.Success && m.Name.EndsWith(suffix, StringComparison.Ordinal))
            {
                var owner = lambda.Groups["owner"].Value;
                var rank = RankOf(real, lambda);
                _canonical[(real, name)] =
                    $"<>9__#{owner}#{rank}_{cache.Groups["m"].Value}";
                return;
            }
        }
    }

    int RankOf(string real, Match match)
    {
        return Rank(real, match.Groups["owner"].Value, Number(match, "n"));
    }
}
