using Mono.Cecil;

namespace DarkwoodMP.Codemod.Il;

/// Compares two builds method by method, ignoring metadata tokens and order.
static class IlDiff
{
    public sealed class Result
    {
        public readonly List<string> Changed = new(), OnlyA = new(), OnlyB = new(), Meta = new();
        public int Same;
    }

    static Dictionary<string, string> Methods(ModuleDefinition mod, out List<string> meta)
    {
        var map = new Dictionary<string, string>();
        meta = new List<string>();
        foreach (var t in IlTools.AllTypes(mod))
        {
            meta.Add($"type {t.FullName} {t.Attributes} : {t.BaseType?.FullName}");
            foreach (var f in t.Fields) { meta.Add($"field {f.FullName} {f.Attributes}"); }
            foreach (var p in t.Properties) { meta.Add($"prop {p.FullName}"); }
            foreach (var e in t.Events) { meta.Add($"event {e.FullName}"); }
            foreach (var a in t.CustomAttributes) { meta.Add($"attr {t.FullName} {a.AttributeType.FullName}"); }
            foreach (var m in t.Methods) { map[m.FullName] = IlTools.Render(m); }
        }
        return map;
    }

    public static Result Compare(string a, string b, string searchDir)
    {
        var res = new DefaultAssemblyResolver();
        res.AddSearchDirectory(searchDir);
        var rp = new ReaderParameters { AssemblyResolver = res };
        using var ma = ModuleDefinition.ReadModule(a, rp);
        using var mb = ModuleDefinition.ReadModule(b, rp);
        var x = Methods(ma, out var metaA);
        var y = Methods(mb, out var metaB);
        var r = new Result();
        foreach (var kv in x)
        {
            if (!y.TryGetValue(kv.Key, out var other)) { r.OnlyA.Add(kv.Key); }
            else if (other != kv.Value) { r.Changed.Add(kv.Key); }
            else { r.Same++; }
        }
        foreach (var k in y.Keys) { if (!x.ContainsKey(k)) { r.OnlyB.Add(k); } }
        r.Meta.AddRange(metaA.Except(metaB).Select(s => "- " + s));
        r.Meta.AddRange(metaB.Except(metaA).Select(s => "+ " + s));
        return r;
    }

    /// The source method a compiler-generated method belongs to: <Foo>b__3_0, <Foo>d__12.MoveNext -> Foo.
    public static string Owner(string fullName)
    {
        int cc = fullName.IndexOf("::");
        string type = fullName.Substring(0, cc);
        type = type.Substring(type.LastIndexOf(' ') + 1);
        string name = fullName.Substring(cc + 2);
        name = name.Substring(0, name.IndexOf('('));
        // the generated nested type (a state machine or closure) carries the owner in its name
        var parts = type.Split('/');
        var owner = parts[parts.Length - 1];
        var ownerOf = System.Text.RegularExpressions.Regex.Match(owner, "^<(?<n>[^>]+)>");
        string outerType = string.Join("/", parts.TakeWhile(p => !p.StartsWith("<")));
        var byName = System.Text.RegularExpressions.Regex.Match(name, "^<(?<n>[^>]+)>");
        string member = byName.Success ? byName.Groups["n"].Value : ownerOf.Success ? ownerOf.Groups["n"].Value : name;
        return outerType + "::" + member;
    }
}
