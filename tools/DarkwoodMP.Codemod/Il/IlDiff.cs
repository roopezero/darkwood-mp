using Mono.Cecil;

namespace DarkwoodMP.Codemod.Il;

/// Compares two builds of a DLL against a manifest. Methods are matched by
/// full name and their bodies rendered with names instead of metadata tokens,
/// so the order of things doesn't matter. See the gate in the guide, section
/// 4.5.
sealed class IlDiff
{
    readonly Manifest _manifest;
    readonly IlDiffReport _report = new IlDiffReport();
    readonly Ownership _ownersA;
    readonly Ownership _ownersB;
    readonly GeneratedNames _namesA;
    readonly GeneratedNames _namesB;
    readonly HashSet<string> _exact = new();
    readonly HashSet<string> _anyOverload = new();
    readonly Dictionary<string, HashSet<string>> _overloads = new();
    readonly HashSet<string> _allowed;

    IlDiff(ModuleDefinition a, ModuleDefinition b, Manifest manifest)
    {
        _manifest = manifest;
        _ownersA = new Ownership(a);
        _ownersB = new Ownership(b);
        _namesA = new GeneratedNames(a, _ownersA);
        _namesB = new GeneratedNames(b, _ownersB);
        _allowed = new HashSet<string>(manifest.Allowed);
        AddOverloads(a);
        AddOverloads(b);
        foreach (var member in manifest.Members)
        {
            if (member.Contains('('))
            {
                _exact.Add(member);
            }
            else
            {
                _anyOverload.Add(member);
            }
        }
    }

    public static IlDiffReport Compare(string a, string b, Manifest manifest)
    {
        using var first = ModuleDefinition.ReadModule(a);
        using var second = ModuleDefinition.ReadModule(b);
        return new IlDiff(first, second, manifest).Run(first, second);
    }

    public static IlDiffReport Compare(byte[] a, byte[] b, Manifest manifest)
    {
        using var firstBytes = new MemoryStream(a);
        using var secondBytes = new MemoryStream(b);
        using var first = ModuleDefinition.ReadModule(firstBytes);
        using var second = ModuleDefinition.ReadModule(secondBytes);
        return new IlDiff(first, second, manifest).Run(first, second);
    }

    /// The key of a method written in source, as the manifest spells it.
    public static string MemberKey(MethodDefinition m)
    {
        var types = m.Parameters.Select(p => p.ParameterType.FullName);
        return m.DeclaringType.FullName + "::" + m.Name
            + "(" + string.Join(",", types) + ")";
    }

    IlDiffReport Run(ModuleDefinition a, ModuleDefinition b)
    {
        if (IlFacts.Of(a, _namesA) != IlFacts.Of(b, _namesB))
        {
            Problem("assembly attributes, references or resources changed");
        }
        var typesA = IlTools.AllTypes(a).ToDictionary(t => _namesA.Of(t));
        var typesB = IlTools.AllTypes(b).ToDictionary(t => _namesB.Of(t));
        foreach (var (name, type) in typesA)
        {
            if (typesB.TryGetValue(name, out var other))
            {
                CompareType(type, other);
            }
            else
            {
                MissingType(type, _ownersA, "removed");
            }
        }
        foreach (var (name, type) in typesB)
        {
            if (!typesA.ContainsKey(name))
            {
                MissingType(type, _ownersB, "added");
            }
        }
        CompareStrings(typesA.Values, typesB.Values);
        CompareFrames(a, b);
        return _report;
    }

    /// No member may gain a lambda, local function or state machine, listed
    /// or not, because the game's stack readers would see the extra frame
    /// (see Frames). The manifest can allow one by name: "method <full
    /// name>", or "type <full name>" for a state machine.
    void CompareFrames(ModuleDefinition a, ModuleDefinition b)
    {
        var before = Frames.Of(a, _ownersA, _namesA);
        foreach (var (key, frames) in Frames.Of(b, _ownersB, _namesB))
        {
            var counted = frames
                .Where(f => !_allowed.Contains(f.Allowance))
                .ToList();
            var names = new List<string>();
            if (before.TryGetValue(key, out var old))
            {
                names = old.Select(f => f.Name).ToList();
            }
            if (counted.Count <= names.Count)
            {
                continue;
            }
            foreach (var frame in counted)
            {
                if (names.Remove(frame.Name))
                {
                    continue;
                }
                if (frame.Allowance.StartsWith("type "))
                {
                    Problem("a state machine was added: " + frame.FullName);
                }
                else
                {
                    Problem("a lambda or local function was added: "
                        + frame.FullName);
                }
            }
        }
    }

    void Problem(string text)
    {
        _report.Problems.Add(text);
    }

    /// True when the manifest holds every member that generated code was
    /// made for. A name is the same for every overload, so the exact members
    /// traced to the code decide; when none was traced, every overload of
    /// each owner's name must be listed.
    bool Holds(IReadOnlyCollection<string> exact, IReadOnlyCollection<string> named)
    {
        if (exact.Count > 0)
        {
            return exact.All(key => _exact.Contains(key)
                || _anyOverload.Contains(Manifest.NameLevel(key)));
        }
        return named.Count > 0 && named.All(HoldsEveryOverload);
    }

    bool HoldsEveryOverload(string name)
    {
        if (_anyOverload.Contains(name))
        {
            return true;
        }
        return _overloads.TryGetValue(name, out var keys)
            && keys.All(_exact.Contains);
    }

    /// Every source method of both builds, by name.
    void AddOverloads(ModuleDefinition module)
    {
        foreach (var t in IlTools.AllTypes(module))
        {
            foreach (var m in t.Methods.Where(m => !IsGeneratedMethod(m)))
            {
                var key = MemberKey(m);
                if (!_overloads.TryGetValue(Manifest.NameLevel(key), out var keys))
                {
                    keys = new HashSet<string>();
                    _overloads[Manifest.NameLevel(key)] = keys;
                }
                keys.Add(key);
            }
        }
    }

    bool HoldsGenerated(TypeDefinition t, Ownership owners)
    {
        return Ownership.IsGenerated(t)
            && Holds(owners.ExactOwnersOf(t), owners.OwnersOf(t));
    }

    void MissingType(TypeDefinition t, Ownership owners, string what)
    {
        if (HoldsGenerated(t, owners) || _allowed.Contains("type " + t.FullName))
        {
            return;
        }
        Problem($"type {what}: {t.FullName}");
    }

    void CompareType(TypeDefinition a, TypeDefinition b)
    {
        bool owned = HoldsGenerated(a, _ownersA) || HoldsGenerated(b, _ownersB);
        if (!owned && IlFacts.Of(a, _namesA) != IlFacts.Of(b, _namesB))
        {
            Problem("type facts changed: " + a.FullName);
        }
        CompareFields(a, b, owned);
        CompareNamed(
            "property",
            a.Properties.ToDictionary(
                p => PropertyKey(p, _namesA), p => IlFacts.Of(p, _namesA)),
            b.Properties.ToDictionary(
                p => PropertyKey(p, _namesB), p => IlFacts.Of(p, _namesB)));
        CompareNamed(
            "event",
            a.Events.ToDictionary(
                e => _namesA.Of(e.DeclaringType) + "::" + e.Name,
                e => IlFacts.Of(e, _namesA)),
            b.Events.ToDictionary(
                e => _namesB.Of(e.DeclaringType) + "::" + e.Name,
                e => IlFacts.Of(e, _namesB)));
        CompareMethods(a, b);
    }

    /// "T::Name", with the parameter types of an indexer.
    static string PropertyKey(PropertyDefinition p, GeneratedNames names)
    {
        var key = names.Of(p.DeclaringType) + "::" + p.Name;
        if (!p.HasParameters)
        {
            return key;
        }
        var types = p.Parameters.Select(x => names.Of(x.ParameterType));
        return key + "(" + string.Join(",", types) + ")";
    }

    void CompareFields(TypeDefinition a, TypeDefinition b, bool owned)
    {
        var fieldsA = a.Fields.ToDictionary(f => _namesA.MemberName(f.Name, a));
        var fieldsB = b.Fields.ToDictionary(f => _namesB.MemberName(f.Name, b));
        foreach (var (name, field) in fieldsA)
        {
            if (fieldsB.TryGetValue(name, out var other))
            {
                if (!owned
                    && !Holds(_ownersA.ExactOwnersOf(field), _ownersA.OwnersOf(field))
                    && IlFacts.Of(field, _namesA) != IlFacts.Of(other, _namesB))
                {
                    Problem("field facts changed: " + field.FullName);
                }
            }
            else
            {
                MissingField(field, _ownersA, owned, "removed");
            }
        }
        foreach (var (name, field) in fieldsB)
        {
            if (!fieldsA.ContainsKey(name))
            {
                MissingField(field, _ownersB, owned, "added");
            }
        }
        // The runtime lays out the instance fields of a sequential type, as
        // every struct is by default, in the order they are declared.
        if (!owned && a.IsSequentialLayout && b.IsSequentialLayout)
        {
            var orderA = InstanceFields(a, _namesA).Where(fieldsB.ContainsKey);
            var orderB = InstanceFields(b, _namesB).Where(fieldsA.ContainsKey);
            if (!orderA.SequenceEqual(orderB))
            {
                Problem("field order changed: " + a.FullName);
            }
        }
    }

    static IEnumerable<string> InstanceFields(TypeDefinition t, GeneratedNames names)
    {
        return t.Fields
            .Where(f => !f.IsStatic)
            .Select(f => names.MemberName(f.Name, t));
    }

    void MissingField(
        FieldDefinition f, Ownership owners, bool owned, string what)
    {
        var generated = Ownership.IsGenerated(f.DeclaringType);
        if (owned
            || (generated && Holds(owners.ExactOwnersOf(f), owners.OwnersOf(f)))
            || _allowed.Contains($"field {f.DeclaringType.FullName}::{f.Name}"))
        {
            return;
        }
        Problem($"field {what}: {f.FullName}");
    }

    /// Properties and events: nothing may appear, go or change, unless listed.
    void CompareNamed(
        string kind,
        Dictionary<string, string> a,
        Dictionary<string, string> b)
    {
        foreach (var (name, facts) in a)
        {
            if (!b.TryGetValue(name, out var other))
            {
                if (!_allowed.Contains(kind + " " + name))
                {
                    Problem($"{kind} removed: {name}");
                }
            }
            else if (other != facts)
            {
                Problem($"{kind} facts changed: {name}");
            }
        }
        foreach (var name in b.Keys)
        {
            if (!a.ContainsKey(name) && !_allowed.Contains(kind + " " + name))
            {
                Problem($"{kind} added: {name}");
            }
        }
    }

    void CompareMethods(TypeDefinition a, TypeDefinition b)
    {
        var methodsA = a.Methods.ToDictionary(m => _namesA.Of(m));
        var methodsB = b.Methods.ToDictionary(m => _namesB.Of(m));
        foreach (var (name, method) in methodsA)
        {
            if (methodsB.TryGetValue(name, out var other))
            {
                CompareMethod(method, other);
            }
            else
            {
                MissingMethod(method, _ownersA, "removed");
            }
        }
        foreach (var (name, method) in methodsB)
        {
            if (!methodsA.ContainsKey(name))
            {
                MissingMethod(method, _ownersB, "added");
            }
        }
    }

    /// True for a compiler-generated method of a member in the manifest.
    bool GeneratedAndHeld(MethodDefinition m, Ownership owners)
    {
        return IsGeneratedMethod(m)
            && Holds(owners.ExactOwnersOf(m), owners.OwnersOf(m));
    }

    static bool IsGeneratedMethod(MethodDefinition m)
    {
        return Ownership.IsGeneratedMethod(m);
    }

    void MissingMethod(MethodDefinition m, Ownership owners, string what)
    {
        if (GeneratedAndHeld(m, owners) || _allowed.Contains("method " + m.FullName))
        {
            if (what == "added")
            {
                _report.Added++;
            }
            else
            {
                _report.Removed++;
            }
            return;
        }
        Problem($"method {what}: {m.FullName}");
    }

    void CompareMethod(MethodDefinition a, MethodDefinition b)
    {
        bool generated = GeneratedAndHeld(a, _ownersA);
        if (!generated && IlFacts.Of(a, _namesA) != IlFacts.Of(b, _namesB))
        {
            Problem("method facts changed (attributes, parameter names, "
                + "defaults or constants): " + a.FullName);
        }
        if (IlTools.Render(a, _namesA) == IlTools.Render(b, _namesB))
        {
            _report.Same++;
            return;
        }
        if (generated || HoldsSource(a))
        {
            _report.Changed.Add(a.FullName);
            return;
        }
        Problem("changed outside the manifest: " + a.FullName);
    }

    /// A method written in source: its own entry, or its name for every
    /// overload.
    bool HoldsSource(MethodDefinition m)
    {
        if (IsGeneratedMethod(m))
        {
            return false;
        }
        return _exact.Contains(MemberKey(m))
            || _anyOverload.Contains(m.DeclaringType.FullName + "::" + m.Name);
    }

    /// Per member of the manifest, as it is listed, the string constants of
    /// its code and of what the compiler generated for it must be the same,
    /// unless the rule recorded a reason for exactly this change. An overload
    /// is a member of its own, so strings that move between two overloads
    /// change both.
    void CompareStrings(
        IEnumerable<TypeDefinition> typesA, IEnumerable<TypeDefinition> typesB)
    {
        var a = Strings(typesA, _ownersA);
        var b = Strings(typesB, _ownersB);
        var members = a.Keys.Union(b.Keys)
            .OrderBy(m => m, StringComparer.Ordinal);
        foreach (var member in members)
        {
            a.TryGetValue(member, out var before);
            b.TryGetValue(member, out var after);
            var removed = Minus(before, after);
            var added = Minus(after, before);
            if (removed.Count + added.Count == 0 || Recorded(member, removed, added))
            {
                continue;
            }
            Problem($"string constants changed in {member}: removed "
                + Quote(removed) + ", added " + Quote(added));
        }
    }

    Dictionary<string, List<string>> Strings(
        IEnumerable<TypeDefinition> types, Ownership owners)
    {
        var map = new Dictionary<string, List<string>>();
        foreach (var t in types)
        {
            foreach (var m in t.Methods.Where(m => m.HasBody))
            {
                var literals = m.Body.Instructions
                    .Where(i => i.Operand is string)
                    .Select(i => (string)i.Operand)
                    .ToList();
                if (literals.Count == 0)
                {
                    continue;
                }
                foreach (var member in StringMembers(m, owners))
                {
                    if (!map.TryGetValue(member, out var list))
                    {
                        list = new List<string>();
                        map[member] = list;
                    }
                    list.AddRange(literals);
                }
            }
        }
        return map;
    }

    /// The manifest members a method's strings count for. A method written
    /// in source counts for its own overload, generated code for the
    /// overloads its code was traced to; each also for its name, when the
    /// manifest lists the name. Generated code that no overload leads to
    /// counts for its name only when every overload of it is listed, as in
    /// Holds.
    HashSet<string> StringMembers(MethodDefinition m, Ownership owners)
    {
        var members = new HashSet<string>();
        IReadOnlyCollection<string> exact = new[] { MemberKey(m) };
        if (IsGeneratedMethod(m))
        {
            exact = owners.ExactOwnersOf(m);
            if (exact.Count == 0)
            {
                members.UnionWith(owners.OwnersOf(m).Where(HoldsEveryOverload));
            }
        }
        foreach (var key in exact)
        {
            if (_exact.Contains(key))
            {
                members.Add(key);
            }
            if (_anyOverload.Contains(Manifest.NameLevel(key)))
            {
                members.Add(Manifest.NameLevel(key));
            }
        }
        return members;
    }

    /// The items of a that are not matched by an item of b (multiset minus).
    static List<string> Minus(List<string> a, List<string> b)
    {
        var left = new List<string>();
        if (a != null)
        {
            left.AddRange(a);
        }
        if (b != null)
        {
            foreach (var item in b)
            {
                left.Remove(item);
            }
        }
        return left;
    }

    /// True when the rule's records for this member, taken together, are
    /// exactly this change and each gives a reason. A rule records one change
    /// per site, so a member with two rewritten strings has two records.
    bool Recorded(string member, List<string> removed, List<string> added)
    {
        var records = _manifest.StringChanges
            .Where(change => Covers(change.Member, member))
            .ToList();
        if (records.Count == 0
            || records.Any(change => string.IsNullOrWhiteSpace(change.Reason)))
        {
            return false;
        }
        var recordedRemoved = records.SelectMany(c => Items(c.Removed)).ToList();
        var recordedAdded = records.SelectMany(c => Items(c.Added)).ToList();
        return SameItems(recordedRemoved, removed)
            && SameItems(recordedAdded, added);
    }

    /// A record names a member the way the manifest does. A record for an
    /// overload covers that overload only, a record for a name every overload
    /// of it (code in a field initializer runs in every constructor), and a
    /// member listed by name takes the records of all its overloads.
    static bool Covers(string recorded, string member)
    {
        if (!member.Contains('('))
        {
            return Manifest.NameLevel(recorded) == member;
        }
        return recorded == member || recorded == Manifest.NameLevel(member);
    }

    static List<string> Items(List<string> items)
    {
        if (items == null)
        {
            return new List<string>();
        }
        return items;
    }

    static bool SameItems(List<string> a, List<string> b)
    {
        return a.OrderBy(x => x, StringComparer.Ordinal)
            .SequenceEqual(b.OrderBy(x => x, StringComparer.Ordinal));
    }

    static string Quote(List<string> items)
    {
        return "[" + string.Join(", ", items.Select(i => "\"" + i + "\"")) + "]";
    }
}
